using SGTest.Server.Gateway;
using SGTest.Server.Npc;
using SGTest.Server.World;
using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;

namespace SGTest.Server.Transport;

public sealed record AesWireConnectionOptions(
    int AuthorizationTimeoutMilliseconds = 120000,
    int ReadBufferBytes = 16 * 1024,
    int BackpressureRetryMilliseconds = 2,
    int MaxBackpressureMilliseconds = 2000);

public enum AesWireEndReason
{
    CleanEof,
    AuthorizationRejected,
    ProtocolViolation,
    BufferOverflow,
    BackpressureTimeout,
    GatewayClosed,
    Cancelled,
    AuthorizationTimeout,
    TransportError
}

public sealed record AesWireConnectionResult(
    AesWireEndReason Reason,
    long CommandsAccepted,
    long ReliableFramesSent,
    string? Fault = null);

/// <summary>
/// Per-client TCP socket, encrypted fixed-key/IV AES-CBC binary record transport.
/// Account and character binding MUST complete before gameplay frames are accepted.
/// </summary>
public static class AesWireConnection
{
    public static async Task<AesWireConnectionResult> RunServerAsync(
        TcpClient client,
        AesCbcSettings settings,
        Func<Stream, CancellationToken, ValueTask<GatewayConnection?>> authorizeAndBind,
        Func<MoveTargetCommand, bool> postMove,
        AesWireConnectionOptions? options = null,
        CancellationToken cancellationToken = default,
        Func<ConnectionBinding, Task>? releaseBoundSession = null,
        Func<TrustedNpcInteraction, Task<NpcPortalOutcome>>? handleNpc = null,
        Func<TrustedWorldResyncRequest, bool>? postResync = null,
        Func<TrustedNormalAttack, bool>? postAttack = null,
        Func<TrustedPetAttack, bool>? postPetAttack = null,
        Func<TrustedPetSkill, bool>? postPetSkill = null,
        Func<TrustedPlayerRespawn, Task>? handleRespawn = null,
        Func<TrustedGroundPickup, Task>? handlePickup = null,
        Func<TrustedRecoveryUse, Task>? handleRecovery = null,
        Func<TrustedInventoryDiscard, Task>? handleDiscard = null,
        Func<TrustedSkillUse, Task>? handleSkill = null,
        Func<TrustedShopCommand, Task>? handleShop = null,
        Func<TrustedStorageCommand, Task>? handleStorage = null,
        Func<TrustedEquipmentCommand, Task>? handleEquipment = null,
        Func<TrustedBankCommand, Task>? handleBank = null,
        Func<TrustedRepairCommand, Task>? handleRepair = null,
        Func<TrustedEnhancementCommand, Task>? handleEnhancement = null,
        Func<TrustedTradeCommand, Task>? handleTrade = null,
        Func<TrustedQuestCommand, Task>? handleQuest = null,
        Func<TrustedPvpModeCommand, Task>? handlePvp = null,
        Func<TrustedPvpAttack, Task>? handlePvpAttack = null,
        Func<TrustedPvpSkill, Task>? handlePvpSkill = null,
        Func<TrustedGmPvpNameCommand, Task>? handleGmPvpName = null,
        Func<TrustedPlayerShopCommand, Task>? handlePlayerShop = null,
        Func<TrustedChatCommand, Task>? handleChat = null,
        Func<TrustedPartyCommand, Task>? handleParty = null,
        Func<TrustedClanCommand, Task>? handleClan = null,
        Func<TrustedFriendCommand, Task>? handleFriend = null,
        Func<TrustedPetCommand, Task>? handlePet = null,
        Func<TrustedMechaCommand, Task>? handleMecha = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(authorizeAndBind);
        ArgumentNullException.ThrowIfNull(postMove);
        var configured = options ?? new AesWireConnectionOptions();
        Validate(configured);
        using (client)
        using (var tls = new AesCbcFrameStream(client.GetStream(), settings))
        {
            client.NoDelay = true;
            GatewayConnection? gateway;
            try
            {
                using var authCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                authCts.CancelAfter(configured.AuthorizationTimeoutMilliseconds);
                gateway = await authorizeAndBind(tls, authCts.Token)
                    .AsTask().WaitAsync(authCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(AesWireEndReason.AuthorizationTimeout, 0, 0);
            }
            catch (OperationCanceledException)
            {
                return new(AesWireEndReason.Cancelled, 0, 0);
            }
            catch (Exception error) when (error is IOException)
            {
                return new(AesWireEndReason.TransportError, 0, 0);
            }

            if (gateway is null)
                return new(AesWireEndReason.AuthorizationRejected, 0, 0);

            using var sessionCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            long reliableSent = 0;
            Exception? writerFault = null;
            var writer = WriteReliableLoopAsync();

            async Task WriteReliableLoopAsync()
            {
                try
                {
                    while (!sessionCts.IsCancellationRequested)
                    {
                        var frame = await gateway.DequeueReliableAsync(sessionCts.Token)
                            .ConfigureAwait(false);
                        if (frame is null) return;
                        await tls.WriteAsync(frame, sessionCts.Token).ConfigureAwait(false);
                        await tls.FlushAsync(sessionCts.Token).ConfigureAwait(false);
                        Interlocked.Increment(ref reliableSent);
                    }
                }
                catch (OperationCanceledException) when (sessionCts.IsCancellationRequested) { }
                catch (Exception error) when (error is IOException or ObjectDisposedException)
                {
                    writerFault = error;
                    sessionCts.Cancel();
                }
            }

            // NPC requests are separated from the socket read loop, so the
            // 30Hz World authorization cannot block TCP framing. A bounded
            // single-reader channel preserves per-connection click order.
            // Full ingress returns Backpressured and keeps the complete frame
            // in WireStreamParser until the worker has room.
            var npcCommands = Channel.CreateBounded<TrustedNpcInteraction>(
                new BoundedChannelOptions(16)
                {
                    SingleReader = true, SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });
            Func<TrustedNpcInteraction, bool>? postNpc = handleNpc is null
                ? null : request => npcCommands.Writer.TryWrite(request);
            Exception? npcFault = null;
            Task npcWorker = handleNpc is null ? Task.CompletedTask : ProcessNpcAsync();

            async Task ProcessNpcAsync()
            {
                try
                {
                    await foreach (var request in npcCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                    {
                        // Auth handler verifies this is STILL the currently
                        // bound actor; only the World can authorize/map transfer.
                        NpcPortalOutcome outcome =
                            await handleNpc!(request).ConfigureAwait(false);
                        var sent = gateway.QueueNpcResult(request, outcome);
                        if (sent != GatewaySendStatus.Enqueued)
                            throw new IOException("Reliable NPC result could not be queued: " + sent);
                    }
                }
                catch (OperationCanceledException) when (sessionCts.IsCancellationRequested) { }
                catch (Exception error)
                {
                    npcFault = error;
                    sessionCts.Cancel(); // terminal, never drop an NPC result
                }
            }

            var respawnQueue = new SessionCommandWorker<TrustedPlayerRespawn>(
                4, handleRespawn, sessionCts);
            Func<TrustedPlayerRespawn, bool>? postRespawn = respawnQueue.Post;

            var pickupQueue = new SessionCommandWorker<TrustedGroundPickup>(
                16, handlePickup, sessionCts);
            Func<TrustedGroundPickup, bool>? postPickup = pickupQueue.Post;

            var recoveryQueue = new SessionCommandWorker<TrustedRecoveryUse>(
                16, handleRecovery, sessionCts);
            Func<TrustedRecoveryUse, bool>? postRecovery = recoveryQueue.Post;

            var discardQueue = new SessionCommandWorker<TrustedInventoryDiscard>(
                16, handleDiscard, sessionCts);
            Func<TrustedInventoryDiscard, bool>? postDiscard = discardQueue.Post;

            var shopQueue = new SessionCommandWorker<TrustedShopCommand>(
                16, handleShop, sessionCts);
            Func<TrustedShopCommand, bool>? postShop = shopQueue.Post;

            var storageQueue = new SessionCommandWorker<TrustedStorageCommand>(
                16, handleStorage, sessionCts);
            Func<TrustedStorageCommand, bool>? postStorage = storageQueue.Post;

            var equipmentQueue = new SessionCommandWorker<TrustedEquipmentCommand>(
                16, handleEquipment, sessionCts);
            Func<TrustedEquipmentCommand, bool>? postEquipment = equipmentQueue.Post;

            var bankQueue = new SessionCommandWorker<TrustedBankCommand>(
                16, handleBank, sessionCts);
            Func<TrustedBankCommand, bool>? postBank = bankQueue.Post;

            var repairQueue = new SessionCommandWorker<TrustedRepairCommand>(
                16, handleRepair, sessionCts);
            Func<TrustedRepairCommand, bool>? postRepair = repairQueue.Post;

            var enhancementQueue = new SessionCommandWorker<TrustedEnhancementCommand>(
                16, handleEnhancement, sessionCts);
            Func<TrustedEnhancementCommand, bool>? postEnhancement = enhancementQueue.Post;

            var tradeQueue = new SessionCommandWorker<TrustedTradeCommand>(
                16, handleTrade, sessionCts);
            Func<TrustedTradeCommand, bool>? postTrade = tradeQueue.Post;

            var questQueue = new SessionCommandWorker<TrustedQuestCommand>(
                16, handleQuest, sessionCts);
            Func<TrustedQuestCommand, bool>? postQuest = questQueue.Post;

            var pvpQueue = new SessionCommandWorker<TrustedPvpModeCommand>(
                16, handlePvp, sessionCts);
            Func<TrustedPvpModeCommand, bool>? postPvp = pvpQueue.Post;

            var pvpAttackQueue = new SessionCommandWorker<TrustedPvpAttack>(
                16, handlePvpAttack, sessionCts);
            Func<TrustedPvpAttack, bool>? postPvpAttack = pvpAttackQueue.Post;

            var pvpSkillQueue = new SessionCommandWorker<TrustedPvpSkill>(
                16, handlePvpSkill, sessionCts);
            Func<TrustedPvpSkill, bool>? postPvpSkill = pvpSkillQueue.Post;

            var gmPvpNameQueue = new SessionCommandWorker<TrustedGmPvpNameCommand>(
                16, handleGmPvpName, sessionCts);
            Func<TrustedGmPvpNameCommand, bool>? postGmPvpName = gmPvpNameQueue.Post;

            var playerShopQueue = new SessionCommandWorker<TrustedPlayerShopCommand>(
                16, handlePlayerShop, sessionCts);
            Func<TrustedPlayerShopCommand, bool>? postPlayerShop = playerShopQueue.Post;

            var chatQueue = new SessionCommandWorker<TrustedChatCommand>(
                32, handleChat, sessionCts);
            Func<TrustedChatCommand, bool>? postChat = chatQueue.Post;

            var partyQueue = new SessionCommandWorker<TrustedPartyCommand>(
                16, handleParty, sessionCts);
            Func<TrustedPartyCommand, bool>? postParty = partyQueue.Post;

            var clanQueue = new SessionCommandWorker<TrustedClanCommand>(
                16, handleClan, sessionCts);
            Func<TrustedClanCommand, bool>? postClan = clanQueue.Post;

            var friendQueue = new SessionCommandWorker<TrustedFriendCommand>(
                16, handleFriend, sessionCts);
            Func<TrustedFriendCommand, bool>? postFriend = friendQueue.Post;

            var petQueue = new SessionCommandWorker<TrustedPetCommand>(
                16, handlePet, sessionCts);
            Func<TrustedPetCommand, bool>? postPet = petQueue.Post;

            var mechaQueue = new SessionCommandWorker<TrustedMechaCommand>(
                16, handleMecha, sessionCts);
            Func<TrustedMechaCommand, bool>? postMecha = mechaQueue.Post;

            var skillCommands=Channel.CreateBounded<TrustedSkillUse>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedSkillUse,bool>? postSkill=handleSkill is null
                ? null : request=>skillCommands.Writer.TryWrite(request);
            Exception? skillFault=null;
            Task skillWorker=handleSkill is null
                ? Task.CompletedTask : ProcessSkillAsync();

            async Task ProcessSkillAsync()
            {
                try
                {
                    // Accepted mutations drain on clean socket shutdown. Host
                    // lifecycle keeps AOI/World alive until sessions finish.
                    await foreach(var request in skillCommands.Reader.ReadAllAsync())
                        await handleSkill!(request).ConfigureAwait(false);
                }
                catch(Exception error)
                {
                    skillFault=error;
                    sessionCts.Cancel();
                }
            }

            long commandsAccepted = 0;
            var reason = AesWireEndReason.CleanEof;
            byte[] rented = ArrayPool<byte>.Shared.Rent(configured.ReadBufferBytes);
            try
            {
                while (!sessionCts.IsCancellationRequested)
                {
                    int count = await tls.ReadAsync(
                        rented.AsMemory(0, configured.ReadBufferBytes), sessionCts.Token)
                        .ConfigureAwait(false);
                    if (count == 0)
                    {
                        reason = AesWireEndReason.CleanEof;
                        break;
                    }

                    var status = gateway.FeedClientBytes(
                        rented.AsSpan(0, count), postMove, out int accepted,
                        postNpc, postResync, postAttack, postPetAttack, postPetSkill, postRespawn, postPickup,
                        postRecovery, postDiscard, postSkill, postShop, postStorage, postEquipment, postBank, postRepair, postEnhancement, postTrade, postQuest, postPvp, postPvpAttack, postPvpSkill, postGmPvpName, postPlayerShop, postChat, postParty, postClan, postFriend, postPet, postMecha);
                    commandsAccepted += accepted;

                    if (status == WireFeedStatus.Backpressured)
                    {
                        long started = Stopwatch.GetTimestamp();
                        do
                        {
                            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >=
                                configured.MaxBackpressureMilliseconds)
                            {
                                reason = AesWireEndReason.BackpressureTimeout;
                                goto finished;
                            }

                            await Task.Delay(
                                configured.BackpressureRetryMilliseconds, sessionCts.Token)
                                .ConfigureAwait(false);
                            status = gateway.ResumeClientIngress(
                                postMove, out accepted, postNpc, postResync,
                                postAttack, postPetAttack, postPetSkill, postRespawn, postPickup, postRecovery,
                                postDiscard, postSkill, postShop, postStorage, postEquipment, postBank, postRepair, postEnhancement, postTrade, postQuest, postPvp, postPvpAttack, postPvpSkill, postGmPvpName, postPlayerShop, postChat, postParty, postClan, postFriend, postPet, postMecha);
                            commandsAccepted += accepted;
                        }
                        while (status == WireFeedStatus.Backpressured);
                    }

                    reason = status switch
                    {
                        WireFeedStatus.Accepted or WireFeedStatus.NeedMoreData =>
                            AesWireEndReason.CleanEof,
                        WireFeedStatus.ProtocolViolation =>
                            AesWireEndReason.ProtocolViolation,
                        WireFeedStatus.BufferOverflow =>
                            AesWireEndReason.BufferOverflow,
                        WireFeedStatus.Closed =>
                            AesWireEndReason.GatewayClosed,
                        _ => throw new InvalidOperationException(
                            "Unhandled gateway feed status: " + status)
                    };

                    if (status is WireFeedStatus.ProtocolViolation or
                        WireFeedStatus.BufferOverflow or WireFeedStatus.Closed)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                reason = cancellationToken.IsCancellationRequested
                    ? AesWireEndReason.Cancelled
                    : writerFault is not null
                        ? AesWireEndReason.TransportError
                        : AesWireEndReason.Cancelled;
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                reason = AesWireEndReason.TransportError;
            }

        finished:
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
            sessionCts.Cancel();
            npcCommands.Writer.TryComplete();
            respawnQueue.Complete();
            pickupQueue.Complete();
            recoveryQueue.Complete();
            discardQueue.Complete();
            skillCommands.Writer.TryComplete();
            shopQueue.Complete();
            storageQueue.Complete();
            equipmentQueue.Complete();
            bankQueue.Complete();
            repairQueue.Complete();
            enhancementQueue.Complete();
            tradeQueue.Complete();
            questQueue.Complete();
            pvpQueue.Complete();
            pvpAttackQueue.Complete();
            pvpSkillQueue.Complete();
            gmPvpNameQueue.Complete();
            playerShopQueue.Complete();
            chatQueue.Complete();
            partyQueue.Complete();
            clanQueue.Complete();
            friendQueue.Complete();
            petQueue.Complete();
            mechaQueue.Complete();
            // In-flight authorized NPC/respawn/skill work may finish even after the
            // socket is closed; await both before final trusted release.
            await respawnQueue.Completion.ConfigureAwait(false);
            // An in-flight authorized World transfer may finish even after the
            // socket is closed. Await that result BEFORE capturing final MapEpoch
            // for trusted disconnect; never leak the new World generation.
            await npcWorker.ConfigureAwait(false);
            await pickupQueue.Completion.ConfigureAwait(false);
            await recoveryQueue.Completion.ConfigureAwait(false);
            await discardQueue.Completion.ConfigureAwait(false);
            await skillWorker.ConfigureAwait(false);
            await shopQueue.Completion.ConfigureAwait(false);
            await storageQueue.Completion.ConfigureAwait(false);
            await equipmentQueue.Completion.ConfigureAwait(false);
            await bankQueue.Completion.ConfigureAwait(false);
            await repairQueue.Completion.ConfigureAwait(false);
            await enhancementQueue.Completion.ConfigureAwait(false);
            await tradeQueue.Completion.ConfigureAwait(false);
            await questQueue.Completion.ConfigureAwait(false);
            await pvpQueue.Completion.ConfigureAwait(false);
            await pvpAttackQueue.Completion.ConfigureAwait(false);
            await pvpSkillQueue.Completion.ConfigureAwait(false);
            await gmPvpNameQueue.Completion.ConfigureAwait(false);
            await playerShopQueue.Completion.ConfigureAwait(false);
            await chatQueue.Completion.ConfigureAwait(false);
            await partyQueue.Completion.ConfigureAwait(false);
            await clanQueue.Completion.ConfigureAwait(false);
            await friendQueue.Completion.ConfigureAwait(false);
            await petQueue.Completion.ConfigureAwait(false);
            await mechaQueue.Completion.ConfigureAwait(false);
            Exception? workerFault =
                npcFault ?? respawnQueue.Fault ?? pickupQueue.Fault ?? recoveryQueue.Fault ??
                discardQueue.Fault ?? skillFault ?? shopQueue.Fault ?? storageQueue.Fault ??
                equipmentQueue.Fault ?? bankQueue.Fault ?? repairQueue.Fault ?? enhancementQueue.Fault ??
                tradeQueue.Fault ??
                questQueue.Fault ?? pvpQueue.Fault ?? pvpAttackQueue.Fault ?? pvpSkillQueue.Fault ??
                gmPvpNameQueue.Fault ?? playerShopQueue.Fault ?? chatQueue.Fault ?? partyQueue.Fault ??
                clanQueue.Fault ?? friendQueue.Fault ?? petQueue.Fault ?? mechaQueue.Fault;
            if (workerFault is not null)
                reason = AesWireEndReason.GatewayClosed;
            var finalBinding = gateway.Binding;
            gateway.Close();
            try { await writer.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            finally
            {
                // Release the authoritative actor after every bound session end
                // (clean EOF, protocol violation, writer fault or cancellation).
                // Cleanup is NOT cancelled with the socket: a cancelled socket
                // must not leave a permanently logged-in World actor.
                if (releaseBoundSession is not null)
                    await releaseBoundSession(finalBinding).ConfigureAwait(false);
            }

            return new(reason,                 commandsAccepted, Interlocked.Read(ref reliableSent),
                workerFault is null ? null :
                    workerFault.GetType().Name + ": " + workerFault.Message);
        }
    }

    internal static void Validate(AesWireConnectionOptions options)
    {
        if (options.AuthorizationTimeoutMilliseconds is < 1000 or > 600000 ||
            options.ReadBufferBytes is < 512 or > 65536 ||
            options.BackpressureRetryMilliseconds is < 1 or > 100 ||
            options.MaxBackpressureMilliseconds is < 10 or > 30000 ||
            options.MaxBackpressureMilliseconds < options.BackpressureRetryMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(options),
                "AES connection options exceed bounded runtime limits");
    }
}
