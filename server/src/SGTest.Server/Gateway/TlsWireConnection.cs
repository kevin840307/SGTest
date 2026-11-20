using System.Buffers;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public sealed record TlsWireConnectionOptions(
    int HandshakeTimeoutMilliseconds = 5000,
    int AuthorizationTimeoutMilliseconds = 120000,
    int ReadBufferBytes = 16 * 1024,
    int BackpressureRetryMilliseconds = 2,
    int MaxBackpressureMilliseconds = 2000);

public enum TlsWireEndReason
{
    CleanEof,
    AuthorizationRejected,
    ProtocolViolation,
    BufferOverflow,
    BackpressureTimeout,
    GatewayClosed,
    Cancelled,
    HandshakeFailed,
    HandshakeTimeout,
    AuthorizationTimeout,
    TransportError
}

public sealed record TlsWireConnectionResult(
    TlsWireEndReason Reason,
    SslProtocols Protocol,
    long CommandsAccepted,
    long ReliableFramesSent,
    string? Fault = null);

/// <summary>
/// Owns one accepted TCP client. TLS is mandatory; there is no plaintext fallback.
/// The authorization callback runs AFTER TLS and BEFORE any gameplay read. It must
/// validate account/session/CharacterChoose and return a GatewayConnection created
/// from a trusted ActorSnapshot. Returning null rejects the connection.
///
/// This class deliberately does NOT expose a listener or test credentials. The
/// future host may accept TcpClient instances only after deployment policy is ready.
/// </summary>
public static class TlsWireConnection
{
    public static async Task<TlsWireConnectionResult> RunServerAsync(
        TcpClient client,
        X509Certificate2 serverCertificate,
        Func<SslStream, CancellationToken, ValueTask<GatewayConnection?>> authorizeAndBind,
        Func<MoveTargetCommand, bool> postMove,
        TlsWireConnectionOptions? options = null,
        CancellationToken cancellationToken = default,
        Func<ConnectionBinding, Task>? releaseBoundSession = null,
        Func<TrustedNpcInteraction, Task<NpcPortalOutcome>>? handleNpc = null,
        Func<TrustedWorldResyncRequest, bool>? postResync = null,
        Func<TrustedNormalAttack, bool>? postAttack = null,
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
        Func<TrustedTradeCommand, Task>? handleTrade = null,
        Func<TrustedQuestCommand, Task>? handleQuest = null,
        Func<TrustedPvpModeCommand, Task>? handlePvp = null,
        Func<TrustedPvpAttack, Task>? handlePvpAttack = null,
        Func<TrustedPvpSkill, Task>? handlePvpSkill = null,
        Func<TrustedGmPvpNameCommand, Task>? handleGmPvpName = null,
        Func<TrustedPlayerShopCommand, Task>? handlePlayerShop = null,
        Func<TrustedChatCommand, Task>? handleChat = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(serverCertificate);
        ArgumentNullException.ThrowIfNull(authorizeAndBind);
        ArgumentNullException.ThrowIfNull(postMove);

        var configured = options ?? new TlsWireConnectionOptions();
        Validate(configured);
        ValidateCertificate(serverCertificate);

        using (client)
        using (var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
        {
            client.NoDelay = true;

            try
            {
                using var handshakeCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshakeCts.CancelAfter(configured.HandshakeTimeoutMilliseconds);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, handshakeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(TlsWireEndReason.HandshakeTimeout, SslProtocols.None, 0, 0);
            }
            catch (OperationCanceledException)
            {
                return new(TlsWireEndReason.Cancelled, SslProtocols.None, 0, 0);
            }
            catch (Exception error) when (error is AuthenticationException or IOException)
            {
                return new(TlsWireEndReason.HandshakeFailed, SslProtocols.None, 0, 0);
            }

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
                return new(TlsWireEndReason.AuthorizationTimeout, tls.SslProtocol, 0, 0);
            }
            catch (OperationCanceledException)
            {
                return new(TlsWireEndReason.Cancelled, tls.SslProtocol, 0, 0);
            }
            catch (Exception error) when (error is AuthenticationException or IOException)
            {
                return new(TlsWireEndReason.TransportError, tls.SslProtocol, 0, 0);
            }

            if (gateway is null)
                return new(TlsWireEndReason.AuthorizationRejected, tls.SslProtocol, 0, 0);

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
                catch (Exception error) when (error is IOException or AuthenticationException or ObjectDisposedException)
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

            var respawnCommands = Channel.CreateBounded<TrustedPlayerRespawn>(
                new BoundedChannelOptions(4)
                {
                    SingleReader = true, SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });
            Func<TrustedPlayerRespawn, bool>? postRespawn = handleRespawn is null
                ? null : request => respawnCommands.Writer.TryWrite(request);
            Exception? respawnFault = null;
            Task respawnWorker = handleRespawn is null
                ? Task.CompletedTask : ProcessRespawnAsync();

            async Task ProcessRespawnAsync()
            {
                try
                {
                    await foreach (var request in respawnCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleRespawn!(request).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (sessionCts.IsCancellationRequested) { }
                catch (Exception error)
                {
                    respawnFault = error;
                    sessionCts.Cancel();
                }
            }

            var pickupCommands = Channel.CreateBounded<TrustedGroundPickup>(
                new BoundedChannelOptions(16)
                {
                    SingleReader = true, SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });
            Func<TrustedGroundPickup,bool>? postPickup = handlePickup is null
                ? null : request => pickupCommands.Writer.TryWrite(request);
            Exception? pickupFault = null;
            Task pickupWorker = handlePickup is null
                ? Task.CompletedTask : ProcessPickupAsync();

            async Task ProcessPickupAsync()
            {
                try
                {
                    await foreach(var request in pickupCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handlePickup!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when (sessionCts.IsCancellationRequested) { }
                catch(Exception error)
                {
                    pickupFault = error;
                    sessionCts.Cancel();
                }
            }

            var recoveryCommands=Channel.CreateBounded<TrustedRecoveryUse>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedRecoveryUse,bool>? postRecovery=handleRecovery is null
                ? null : request=>recoveryCommands.Writer.TryWrite(request);
            Exception? recoveryFault=null;
            Task recoveryWorker=handleRecovery is null
                ? Task.CompletedTask : ProcessRecoveryAsync();

            async Task ProcessRecoveryAsync()
            {
                try
                {
                    await foreach(var request in recoveryCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleRecovery!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    recoveryFault=error;
                    sessionCts.Cancel();
                }
            }

            var discardCommands=Channel.CreateBounded<TrustedInventoryDiscard>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedInventoryDiscard,bool>? postDiscard=handleDiscard is null
                ? null : request=>discardCommands.Writer.TryWrite(request);
            Exception? discardFault=null;
            Task discardWorker=handleDiscard is null
                ? Task.CompletedTask : ProcessDiscardAsync();

            async Task ProcessDiscardAsync()
            {
                try
                {
                    await foreach(var request in discardCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleDiscard!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    discardFault=error;
                    sessionCts.Cancel();
                }
            }


            var shopCommands=Channel.CreateBounded<TrustedShopCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedShopCommand,bool>? postShop=handleShop is null
                ? null : request=>shopCommands.Writer.TryWrite(request);
            Exception? shopFault=null;
            Task shopWorker=handleShop is null
                ? Task.CompletedTask : ProcessShopAsync();

            async Task ProcessShopAsync()
            {
                try
                {
                    await foreach(var request in shopCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleShop!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    shopFault=error;
                    sessionCts.Cancel();
                }
            }

            var storageCommands=Channel.CreateBounded<TrustedStorageCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedStorageCommand,bool>? postStorage=handleStorage is null
                ? null:request=>storageCommands.Writer.TryWrite(request);
            Exception? storageFault=null;
            Task storageWorker=handleStorage is null
                ? Task.CompletedTask:ProcessStorageAsync();

            async Task ProcessStorageAsync()
            {
                try
                {
                    await foreach(var request in storageCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleStorage!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    storageFault=error;
                    sessionCts.Cancel();
                }
            }

            var equipmentCommands=Channel.CreateBounded<TrustedEquipmentCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedEquipmentCommand,bool>? postEquipment=handleEquipment is null
                ? null:request=>equipmentCommands.Writer.TryWrite(request);
            Exception? equipmentFault=null;
            Task equipmentWorker=handleEquipment is null
                ? Task.CompletedTask:ProcessEquipmentAsync();

            async Task ProcessEquipmentAsync()
            {
                try
                {
                    await foreach(var request in equipmentCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleEquipment!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    equipmentFault=error;
                    sessionCts.Cancel();
                }
            }

            var bankCommands=Channel.CreateBounded<TrustedBankCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedBankCommand,bool>? postBank=handleBank is null
                ? null:request=>bankCommands.Writer.TryWrite(request);
            Exception? bankFault=null;
            Task bankWorker=handleBank is null
                ? Task.CompletedTask:ProcessBankAsync();

            async Task ProcessBankAsync()
            {
                try
                {
                    await foreach(var request in bankCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleBank!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    bankFault=error;
                    sessionCts.Cancel();
                }
            }

            var repairCommands=Channel.CreateBounded<TrustedRepairCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedRepairCommand,bool>? postRepair=handleRepair is null
                ? null:request=>repairCommands.Writer.TryWrite(request);
            Exception? repairFault=null;
            Task repairWorker=handleRepair is null
                ? Task.CompletedTask:ProcessRepairAsync();

            async Task ProcessRepairAsync()
            {
                try
                {
                    await foreach(var request in repairCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleRepair!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    repairFault=error;
                    sessionCts.Cancel();
                }
            }

            var tradeCommands=Channel.CreateBounded<TrustedTradeCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedTradeCommand,bool>? postTrade=handleTrade is null
                ? null:request=>tradeCommands.Writer.TryWrite(request);
            Exception? tradeFault=null;
            Task tradeWorker=handleTrade is null
                ? Task.CompletedTask:ProcessTradeAsync();

            async Task ProcessTradeAsync()
            {
                try
                {
                    await foreach(var request in tradeCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleTrade!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    tradeFault=error;
                    sessionCts.Cancel();
                }
            }

            var questCommands=Channel.CreateBounded<TrustedQuestCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedQuestCommand,bool>? postQuest=handleQuest is null
                ? null:request=>questCommands.Writer.TryWrite(request);
            Exception? questFault=null;
            Task questWorker=handleQuest is null
                ? Task.CompletedTask:ProcessQuestAsync();

            async Task ProcessQuestAsync()
            {
                try
                {
                    await foreach(var request in questCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleQuest!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    questFault=error;
                    sessionCts.Cancel();
                }
            }

            var pvpCommands=Channel.CreateBounded<TrustedPvpModeCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedPvpModeCommand,bool>? postPvp=handlePvp is null
                ? null:request=>pvpCommands.Writer.TryWrite(request);
            Exception? pvpFault=null;
            Task pvpWorker=handlePvp is null ? Task.CompletedTask:ProcessPvpAsync();

            async Task ProcessPvpAsync()
            {
                try
                {
                    await foreach(var request in pvpCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handlePvp!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    pvpFault=error;
                    sessionCts.Cancel();
                }
            }

            var pvpAttackCommands=Channel.CreateBounded<TrustedPvpAttack>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedPvpAttack,bool>? postPvpAttack=handlePvpAttack is null
                ? null:request=>pvpAttackCommands.Writer.TryWrite(request);
            Exception? pvpAttackFault=null;
            Task pvpAttackWorker=handlePvpAttack is null
                ? Task.CompletedTask:ProcessPvpAttackAsync();

            async Task ProcessPvpAttackAsync()
            {
                try
                {
                    await foreach(var request in pvpAttackCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handlePvpAttack!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    pvpAttackFault=error;
                    sessionCts.Cancel();
                }
            }

            var pvpSkillCommands=Channel.CreateBounded<TrustedPvpSkill>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedPvpSkill,bool>? postPvpSkill=handlePvpSkill is null
                ? null:request=>pvpSkillCommands.Writer.TryWrite(request);
            Exception? pvpSkillFault=null;
            Task pvpSkillWorker=handlePvpSkill is null
                ? Task.CompletedTask:ProcessPvpSkillAsync();

            async Task ProcessPvpSkillAsync()
            {
                try
                {
                    await foreach(var request in pvpSkillCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handlePvpSkill!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    pvpSkillFault=error;
                    sessionCts.Cancel();
                }
            }

            var gmPvpNameCommands=Channel.CreateBounded<TrustedGmPvpNameCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedGmPvpNameCommand,bool>? postGmPvpName=
                handleGmPvpName is null
                    ? null:request=>gmPvpNameCommands.Writer.TryWrite(request);
            Exception? gmPvpNameFault=null;
            Task gmPvpNameWorker=handleGmPvpName is null
                ? Task.CompletedTask:ProcessGmPvpNameAsync();

            async Task ProcessGmPvpNameAsync()
            {
                try
                {
                    await foreach(var request in gmPvpNameCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleGmPvpName!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    gmPvpNameFault=error;
                    sessionCts.Cancel();
                }
            }

            var playerShopCommands=Channel.CreateBounded<TrustedPlayerShopCommand>(
                new BoundedChannelOptions(16)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedPlayerShopCommand,bool>? postPlayerShop=
                handlePlayerShop is null
                    ? null:request=>playerShopCommands.Writer.TryWrite(request);
            Exception? playerShopFault=null;
            Task playerShopWorker=handlePlayerShop is null
                ? Task.CompletedTask:ProcessPlayerShopAsync();

            async Task ProcessPlayerShopAsync()
            {
                try
                {
                    await foreach(var request in playerShopCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handlePlayerShop!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    playerShopFault=error;
                    sessionCts.Cancel();
                }
            }

            var chatCommands=Channel.CreateBounded<TrustedChatCommand>(
                new BoundedChannelOptions(32)
                {
                    SingleReader=true,SingleWriter=true,
                    FullMode=BoundedChannelFullMode.Wait
                });
            Func<TrustedChatCommand,bool>? postChat=handleChat is null
                ? null:request=>chatCommands.Writer.TryWrite(request);
            Exception? chatFault=null;
            Task chatWorker=handleChat is null
                ? Task.CompletedTask:ProcessChatAsync();

            async Task ProcessChatAsync()
            {
                try
                {
                    await foreach(var request in chatCommands.Reader
                        .ReadAllAsync(sessionCts.Token).ConfigureAwait(false))
                        await handleChat!(request).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(sessionCts.IsCancellationRequested){}
                catch(Exception error)
                {
                    chatFault=error;
                    sessionCts.Cancel();
                }
            }

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
            var reason = TlsWireEndReason.CleanEof;
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
                        reason = TlsWireEndReason.CleanEof;
                        break;
                    }

                    var status = gateway.FeedClientBytes(
                        rented.AsSpan(0, count), postMove, out int accepted,
                        postNpc, postResync, postAttack, postRespawn, postPickup,
                        postRecovery, postDiscard, postSkill, postShop, postStorage, postEquipment, postBank, postRepair, postTrade, postQuest, postPvp, postPvpAttack, postPvpSkill, postGmPvpName, postPlayerShop, postChat);
                    commandsAccepted += accepted;

                    if (status == WireFeedStatus.Backpressured)
                    {
                        long started = Stopwatch.GetTimestamp();
                        do
                        {
                            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >=
                                configured.MaxBackpressureMilliseconds)
                            {
                                reason = TlsWireEndReason.BackpressureTimeout;
                                goto finished;
                            }

                            await Task.Delay(
                                configured.BackpressureRetryMilliseconds, sessionCts.Token)
                                .ConfigureAwait(false);
                            status = gateway.ResumeClientIngress(
                                postMove, out accepted, postNpc, postResync,
                                postAttack, postRespawn, postPickup, postRecovery,
                                postDiscard, postSkill, postShop, postStorage, postEquipment, postBank, postRepair, postTrade, postQuest, postPvp, postPvpAttack, postPvpSkill, postGmPvpName, postPlayerShop, postChat);
                            commandsAccepted += accepted;
                        }
                        while (status == WireFeedStatus.Backpressured);
                    }

                    reason = status switch
                    {
                        WireFeedStatus.Accepted or WireFeedStatus.NeedMoreData =>
                            TlsWireEndReason.CleanEof,
                        WireFeedStatus.ProtocolViolation =>
                            TlsWireEndReason.ProtocolViolation,
                        WireFeedStatus.BufferOverflow =>
                            TlsWireEndReason.BufferOverflow,
                        WireFeedStatus.Closed =>
                            TlsWireEndReason.GatewayClosed,
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
                    ? TlsWireEndReason.Cancelled
                    : writerFault is not null
                        ? TlsWireEndReason.TransportError
                        : TlsWireEndReason.Cancelled;
            }
            catch (Exception error) when (error is IOException or AuthenticationException or ObjectDisposedException)
            {
                reason = TlsWireEndReason.TransportError;
            }

        finished:
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
            sessionCts.Cancel();
            npcCommands.Writer.TryComplete();
            respawnCommands.Writer.TryComplete();
            pickupCommands.Writer.TryComplete();
            recoveryCommands.Writer.TryComplete();
            discardCommands.Writer.TryComplete();
            skillCommands.Writer.TryComplete();
            shopCommands.Writer.TryComplete();
            storageCommands.Writer.TryComplete();
            equipmentCommands.Writer.TryComplete();
            bankCommands.Writer.TryComplete();
            repairCommands.Writer.TryComplete();
            tradeCommands.Writer.TryComplete();
            questCommands.Writer.TryComplete();
            pvpCommands.Writer.TryComplete();
            pvpAttackCommands.Writer.TryComplete();
            pvpSkillCommands.Writer.TryComplete();
            gmPvpNameCommands.Writer.TryComplete();
            playerShopCommands.Writer.TryComplete();
            chatCommands.Writer.TryComplete();
            // In-flight authorized NPC/respawn/skill work may finish even after the
            // socket is closed; await both before final trusted release.
            await respawnWorker.ConfigureAwait(false);
            // An in-flight authorized World transfer may finish even after the
            // socket is closed. Await that result BEFORE capturing final MapEpoch
            // for trusted disconnect; never leak the new World generation.
            await npcWorker.ConfigureAwait(false);
            await pickupWorker.ConfigureAwait(false);
            await recoveryWorker.ConfigureAwait(false);
            await discardWorker.ConfigureAwait(false);
            await skillWorker.ConfigureAwait(false);
            await shopWorker.ConfigureAwait(false);
            await storageWorker.ConfigureAwait(false);
            await equipmentWorker.ConfigureAwait(false);
            await bankWorker.ConfigureAwait(false);
            await repairWorker.ConfigureAwait(false);
            await tradeWorker.ConfigureAwait(false);
            await questWorker.ConfigureAwait(false);
            await pvpWorker.ConfigureAwait(false);
            await pvpAttackWorker.ConfigureAwait(false);
            await pvpSkillWorker.ConfigureAwait(false);
            await gmPvpNameWorker.ConfigureAwait(false);
            await playerShopWorker.ConfigureAwait(false);
            await chatWorker.ConfigureAwait(false);
            Exception? workerFault =
                npcFault ?? respawnFault ?? pickupFault ?? recoveryFault ??
                discardFault ?? skillFault ?? shopFault ?? storageFault ??
                equipmentFault ?? bankFault ?? repairFault ?? tradeFault ??
                questFault ?? pvpFault ?? pvpAttackFault ?? pvpSkillFault ??
                gmPvpNameFault ?? playerShopFault ?? chatFault;
            if (workerFault is not null)
                reason = TlsWireEndReason.GatewayClosed;
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

            return new(reason, tls.SslProtocol,
                commandsAccepted, Interlocked.Read(ref reliableSent),
                workerFault is null ? null :
                    workerFault.GetType().Name + ": " + workerFault.Message);
        }
    }

    internal static void Validate(TlsWireConnectionOptions options)
    {
        if (options.HandshakeTimeoutMilliseconds is < 100 or > 30000 ||
            options.AuthorizationTimeoutMilliseconds is < 1000 or > 600000 ||
            options.ReadBufferBytes is < 512 or > 65536 ||
            options.BackpressureRetryMilliseconds is < 1 or > 100 ||
            options.MaxBackpressureMilliseconds is < 10 or > 30000 ||
            options.MaxBackpressureMilliseconds < options.BackpressureRetryMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(options),
                "TLS connection options exceed bounded runtime limits");
    }

    internal static void ValidateCertificate(X509Certificate2 certificate)
    {
        var now = DateTime.UtcNow;
        if (!certificate.HasPrivateKey ||
            now < certificate.NotBefore.ToUniversalTime() ||
            now > certificate.NotAfter.ToUniversalTime())
            throw new ArgumentException(
                "TLS server certificate must contain a currently valid private key",
                nameof(certificate));
    }
}
