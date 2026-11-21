using SGTest.Server.Visibility;
using SGTest.Server.Trade;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.PlayerShop;
using SGTest.Server.Party;
using SGTest.Server.Npc;
using SGTest.Server.Combat;
using SGTest.Server.Chat;
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SGTest.Server.Identity;
using SGTest.Server.Inventory;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public sealed record CharacterCreationPolicy(CharacterSpawn Spawn);

/// <summary>
/// TLS-only registration/login/character-selection boundary. Public account
/// registration is opt-in and rate-limited; even a newly created account must
/// still pass the ordinary AuthRequest before receiving CharacterList. It never
/// trusts a client-supplied ActorId: CharacterChoose is matched against the
/// authenticated account, then persistent character_id enters trusted World lifecycle.
/// </summary>
public sealed partial class IdentityTlsAuthorization
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PortalCatalog? _portals;
    private readonly CharacterPositionCheckpoint? _positions;
    private readonly PlayerCombatSettlement? _combat;
    private readonly long _pickupWorldGeneration;
    private readonly IReadOnlyDictionary<int,RecoveryItemRule>? _recoveryRules;
    private readonly CharacterCreationPolicy? _characterCreation;
    private readonly NpcShopCatalog? _shops;
    private readonly NpcStorageCatalog? _storages;
    private readonly IReadOnlyDictionary<int,EquipmentRule>? _equipmentRules;
    private readonly NpcBankCatalog? _banks;
    private readonly NpcRepairCatalog? _repairs;
    private readonly IReadOnlyDictionary<int,RepairItemRule>? _repairItemRules;
    private readonly NpcEnhancementCatalog? _enhancers;
    private readonly IReadOnlyDictionary<int,EquipmentEnhancementItemRule>?
        _enhancementItemRules;
    private readonly NpcQuestCatalog? _quests;
    private readonly PlayerTradeCoordinator? _trades;
    private readonly PlayerShopCoordinator? _playerShops;
    private readonly PartyCoordinator? _parties;
    private readonly AuthenticationRateLimiter? _rateLimiter;
    private readonly AccountRegistrationRateLimiter? _registrationRateLimiter;
    private readonly HashSet<string> _gmAccounts;

    public IdentityTlsAuthorization(SqliteIdentityStore identity, ServerWorldHost world,
                                    PortalCatalog? portals = null,
                                    CharacterPositionCheckpoint? positions = null,
                                    PlayerCombatSettlement? combat = null,
                                     long pickupWorldGeneration = 0,
                                     IReadOnlyDictionary<int,RecoveryItemRule>? recoveryRules = null,
                                     CharacterCreationPolicy? characterCreation = null,
                                     NpcShopCatalog? shops = null,
                                     NpcStorageCatalog? storages = null,
                                     IReadOnlyDictionary<int,EquipmentRule>? equipmentRules = null,
                                     NpcBankCatalog? banks = null,
                                     NpcRepairCatalog? repairs = null,
                                     IReadOnlyDictionary<int,RepairItemRule>? repairItemRules = null,
                                     NpcEnhancementCatalog? enhancers = null,
                                     IReadOnlyDictionary<int,EquipmentEnhancementItemRule>?
                                         enhancementItemRules = null,
                                     NpcQuestCatalog? quests = null,
                                     PlayerTradeCoordinator? trades = null,
                                     PlayerShopCoordinator? playerShops = null,
                                     PartyCoordinator? parties = null,
                                     AuthenticationRateLimiter? rateLimiter = null,
                                     AccountRegistrationRateLimiter? registrationRateLimiter = null,
                                     IReadOnlySet<string>? gmAccounts = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _portals = portals;
        _positions = positions;
        _combat = combat;
        if (pickupWorldGeneration is < 0 or > 0x3fffffff)
            throw new ArgumentOutOfRangeException(nameof(pickupWorldGeneration));
        _pickupWorldGeneration = pickupWorldGeneration;
        _recoveryRules = recoveryRules;
        if (characterCreation is not null)
        {
            var spawn = characterCreation.Spawn;
            if (spawn.MapId <= 0 || !double.IsFinite(spawn.X) ||
                !double.IsFinite(spawn.Y))
                throw new ArgumentOutOfRangeException(nameof(characterCreation));
        }
        _characterCreation = characterCreation;
        _shops = shops;
        _storages = storages;
        _equipmentRules = equipmentRules;
        _banks = banks;
        _repairs = repairs;
        _repairItemRules = repairItemRules;
        _enhancers = enhancers;
        _enhancementItemRules = enhancementItemRules;
        _quests = quests;
        _trades = trades;
        _playerShops=playerShops;
        _parties=parties;
        _rateLimiter=rateLimiter;
        _registrationRateLimiter=registrationRateLimiter;
        if(_playerShops is not null&&_trades is null)
            throw new ArgumentException(
                "Player shop requires the authenticated gameplay-gate registry",
                nameof(playerShops));
        _gmAccounts=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(gmAccounts is not null)
            foreach(string account in gmAccounts)
            {
                if(!ValidGmAccountName(account))
                    throw new ArgumentException(
                        "GM account allowlist entries must be valid account usernames",
                        nameof(gmAccounts));
                _gmAccounts.Add(account);
            }
    }

    /// <summary>
    /// The official authenticated socket lifecycle for a future production
    /// listener: binds the selected character, registers the receipt route,
    /// and releases BOTH the route and the World actor after the socket ends.
    /// No listener, credentials or plaintext fallback are provided here.
    /// </summary>
    public async Task<TlsWireConnectionResult> RunAuthenticatedSocketAsync(
        TcpClient client, X509Certificate2 certificate, WorldResultRouter router,
        TlsWireConnectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(router);
        var bindingGate = new object();
        bool sessionFinished = false;
        GatewayConnection? boundConnection = null;
        using var gameplayGate = new SemaphoreSlim(1,1);
        using var tradeIngress = _trades is null ? null : new TradeCommandIngress(_trades, router);
        var playerShopGate=new PlayerShopSequenceGate();

        IPAddress remoteAddress=((IPEndPoint?)client.Client.RemoteEndPoint)?.Address
            ??IPAddress.None;
        if(remoteAddress.IsIPv4MappedToIPv6)remoteAddress=remoteAddress.MapToIPv4();

        async ValueTask<GatewayConnection?> Authorize(SslStream stream, CancellationToken token)
        {
            var connection = await AuthorizeAndBindAsync(
                stream, token, remoteAddress).ConfigureAwait(false);
            if (connection is null) return null;
            var binding = connection.Binding;
            bool attached;
            lock (bindingGate)
            {
                // The TLS outer authorization timeout may win its WaitAsync race
                // just before this callback returns. Never attach after the
                // socket runner's finally has already released the session.
                attached = !sessionFinished && !token.IsCancellationRequested &&
                    router.TryAttach(connection);
                if (attached && _combat is not null &&
                    !_combat.ResumeIfAttached(
                        binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch))
                {
                    router.TryDetach(connection);
                    attached = false;
                }
                if (attached && _trades is not null &&
                    !_trades.TryRegisterGameplayGate(binding,gameplayGate))
                {
                    router.TryDetach(connection);
                    attached = false;
                }
                if (attached) boundConnection = connection;
            }
            if(attached&&_playerShops is not null)
            {
                try
                {
                    await _playerShops.AttachAsync(binding).ConfigureAwait(false);
                }
                catch
                {
                    lock(bindingGate)
                    {
                        if(ReferenceEquals(boundConnection,connection))
                            boundConnection=null;
                    }
                    router.TryDetach(connection);
                    _trades?.UnregisterGameplayGate(binding,gameplayGate);
                    attached=false;
                }
            }
            if (!attached)
            {
                if (_combat is not null)
                    await _combat.CloseAndDrainAsync(
                        binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
                connection.Close();
                await _world.TrustedDisconnectAsync(
                    binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                    .ConfigureAwait(false);
                return null;
            }
            return connection;
        }

        async Task Release(ConnectionBinding binding)
        {
            // No socket release may detach/advance a participant while its
            // confirmed SQLite trade is inside the two-party commit barrier.
            await gameplayGate.WaitAsync().ConfigureAwait(false);
            try
            {
            GatewayConnection? connection;
            lock (bindingGate)
            {
                connection = boundConnection;
                boundConnection = null;
            }
            // Drain committed combat BEFORE detaching the Gateway. The settlement
            // worker queues each reliable damage/death frame only after SQLite
            // commits, so reconnect cannot observe old HP and the live socket
            // cannot lose a committed result merely because release won a race.
            if (_combat is not null)
                await _combat.CloseAndDrainAsync(
                    binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
            // Online player shops cannot outlive the authenticated session.
            // The same gameplay gate excludes a concurrent buyer commit here.
            if(_playerShops is not null)
                await _playerShops.CloseForBindingAsync(binding,router)
                    .ConfigureAwait(false);
            // A pending trade is session-scoped. Disconnect before detaching the
            // route so the peer can receive one authoritative terminal event.
            _trades?.Disconnect(binding,router);
            _parties?.Disconnect(binding,router);
            if (connection is not null) router.TryDetach(connection);

            // Keep the character registered while its final authoritative
            // coordinates are committed. This prevents a reconnect from reading
            // the old SQLite spawn in the small DB-write window.
            ActorSnapshot? final = await _world.TrustedPrepareDisconnectAsync(
                binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                .ConfigureAwait(false);
            try
            {
                if (final is { } snapshot)
                {
                    if (_positions is not null)
                        await _positions.FinalizeAsync(snapshot).ConfigureAwait(false);
                    else
                        await _identity.UpdateCharacterSpawnAsync(snapshot.ActorId,
                            new CharacterSpawn(snapshot.MapId, snapshot.X, snapshot.Y))
                            .ConfigureAwait(false);
                }
            }
            finally
            {
                var release = final ?? new ActorSnapshot(
                    binding.ActorId, 0, binding.ConnectionEpoch, binding.MapEpoch,
                    0, 0, 0, 0, 0, false);
                await _world.TrustedDisconnectAsync(
                    release.ActorId, release.ConnectionEpoch, release.MapEpoch)
                    .ConfigureAwait(false);
            }
            }
            finally
            {
                _trades?.UnregisterGameplayGate(binding,gameplayGate);
                gameplayGate.Release();
            }
        }

        try
        {
            // Map transfers and pickup settlement for this authenticated
            // socket must not race one another across separate async workers.
            Func<TrustedNpcInteraction, Task<NpcPortalOutcome>>? npcHandler =
                _portals is null ? null : async request =>
                {
                    await gameplayGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                    if (_combat is not null)
                        await _combat.SuspendAndDrainAsync(
                            request.ActorId, request.ConnectionEpoch).ConfigureAwait(false);

                    var oldBinding=new ConnectionBinding(
                        request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                    var outcome = await router.TrustedNpcInteractAsync(
                        _world, _portals!,oldBinding,
                        request.InteractionSequence, request.NpcSpawnId).ConfigureAwait(false);

                    // Trade cannot span a MapEpoch. The old binding stored by the
                    // trade coordinator is intentionally used to cancel the
                    // pre-transfer session after a successful authoritative portal.
                    if(outcome.Status==NpcPortalStatus.Accepted)
                    {
                        if(_playerShops is not null&&outcome.TransferredActor is { } shopAfter)
                            await _playerShops.CloseForBindingAsync(
                                ConnectionBinding.FromTrustedSnapshot(shopAfter),router)
                                .ConfigureAwait(false);
                        _trades?.Disconnect(oldBinding,router);
                        if(_trades is not null&&
                           (outcome.TransferredActor is not { } after||
                            !_trades.TryAdvanceGameplayGate(
                                oldBinding,after,gameplayGate)))
                            throw new InvalidOperationException(
                                "Trade gameplay lock did not advance with trusted NPC map generation");
                    }

                    if (_combat is not null)
                    {
                        long nextEpoch = outcome.TransferredActor?.MapEpoch
                            ?? request.RequestMapEpoch;
                        if (!_combat.ResumeIfAttached(
                                request.ActorId, request.ConnectionEpoch, nextEpoch))
                            throw new InvalidOperationException(
                                "Combat generation could not resume after NPC interaction");
                    }
                    return outcome;
                    }
                    finally { gameplayGate.Release(); }
                };
            async Task PlayerShopHandler(TrustedPlayerShopCommand request)
            {
                if(_playerShops is null)return;
                var decision=playerShopGate.Inspect(request);
                if(decision==PlayerShopSequenceDecision.ExactReplay)
                {
                    var replay=router.QueuePlayerShopResult(
                        request,playerShopGate.LastReceipt);
                    if(replay!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable player-shop replay could not be queued: "+replay);
                    return;
                }
                if(decision==PlayerShopSequenceDecision.Stale)
                {
                    var stale=new PlayerShopResultWire(
                        request.RequestMapEpoch,request.ShopSequence,request.Action,
                        PlayerShopResultStatus.StaleSequence,
                        request.SellerActorId,request.ListingId,0,0,0,0,-1,0,0,0);
                    var sent=router.QueuePlayerShopResult(request,stale);
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale player-shop result could not be queued: "+sent);
                    return;
                }
                var receipt=await _playerShops.HandleNewAsync(request,router)
                    .ConfigureAwait(false);
                playerShopGate.Record(request,receipt);
            }

            Task PartyHandler(TrustedPartyCommand request)
            {
                if(_parties is null)return Task.CompletedTask;
                return _parties.HandleAsync(request,router);
            }

            // Repair and enhancement share this authenticated session's gameplay
            // gate with other commands; no new session or command queue is created.
            var npcItems = new NpcItemModificationHandler(
                _identity, _world, _combat, _pickupWorldGeneration,
                _repairs, _repairItemRules, _enhancers, _enhancementItemRules,
                router, gameplayGate);
            // Shop, storage and bank share the same authenticated gameplay
            // gate and result router as equipment and item modification.
            var npcEconomy = new NpcEconomyHandler(
                _identity, _world, _combat, _pickupWorldGeneration,
                _shops, _storages, _banks, router, gameplayGate);
            var npcQuests = new NpcQuestHandler(
                _identity, _world, _combat, _quests, _pickupWorldGeneration,
                router, gameplayGate);
            var pvpStates = new PvpStateHandler(
                _identity, _world, _combat, router, gameplayGate);
            var pvpCombat = new PvpCombatHandler(
                _identity, _world, _combat, _pickupWorldGeneration,
                router, gameplayGate);
            // Inventory acquisition, equipment transfer and discard share the
            // authenticated socket's existing gameplay lock and DB authority.
            var inventory = new InventoryCommandHandler(
                _identity, _world, _combat, _equipmentRules,
                _pickupWorldGeneration, router, gameplayGate);
            var recovery = new PlayerRecoveryHandler(
                _identity, _world, _combat, _recoveryRules,
                _pickupWorldGeneration, router, gameplayGate);
            var skills = new SkillCommandHandler(
                _identity, _world, _combat, _pickupWorldGeneration,
                router, gameplayGate);
            return await TlsWireConnection.RunServerAsync(client, certificate,
                Authorize, _world.TryPostMove, options, cancellationToken, Release,
                npcHandler, router.TryRequestResync,
                router.TryRequestNormalAttack, recovery.HandleRespawnAsync,
                inventory.HandlePickupAsync, recovery.HandleRecoveryItemAsync,
                inventory.HandleDiscardAsync,
                handleSkill:skills.HandleSkillAsync,handleShop:npcEconomy.HandleShopAsync,
                handleStorage:npcEconomy.HandleStorageAsync,
                handleEquipment:inventory.HandleEquipmentAsync,
                handleBank:npcEconomy.HandleBankAsync,
                handleRepair:npcItems.HandleRepairAsync,
                handleEnhancement:npcItems.HandleEnhancementAsync,
                handleTrade:tradeIngress is null ? null : tradeIngress.HandleAsync,
                handleQuest:npcQuests.HandleQuestAsync,
                handlePvp:pvpStates.HandleModeAsync,
                handlePvpAttack:pvpCombat.HandleAttackAsync,
                handlePvpSkill:pvpCombat.HandleSkillAsync,
                handleGmPvpName:pvpStates.HandleGmNameAsync,
                handlePlayerShop:_playerShops is null?null:PlayerShopHandler,
                handleChat:request=>ChatRouting.RouteAsync(request,router,_parties),
                handleParty:_parties is null?null:PartyHandler)
                .ConfigureAwait(false);
        }
        finally
        {
            // Also covers a rare outer WaitAsync timeout while the auth callback
            // has already attached a Gateway but has not yet returned it.
            GatewayConnection? abandoned;
            lock (bindingGate)
            {
                sessionFinished = true;
                abandoned = boundConnection;
                boundConnection = null;
            }
            if (abandoned is not null)
            {
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                var binding = abandoned.Binding;
                if (_combat is not null)
                    await _combat.CloseAndDrainAsync(
                        binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
                if(_playerShops is not null)
                    await _playerShops.CloseForBindingAsync(binding,router)
                        .ConfigureAwait(false);
                _trades?.Disconnect(binding,router);
                _parties?.Disconnect(binding,router);
                router.TryDetach(abandoned);
                await _world.TrustedDisconnectAsync(
                    binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                    .ConfigureAwait(false);
                }
                finally
                {
                    _trades?.UnregisterGameplayGate(abandoned.Binding,gameplayGate);
                    gameplayGate.Release();
                }
            }
        }
    }


    private static bool ValidGmAccountName(string value)
    {
        if(value.Length is <3 or >32)return false;
        foreach(char c in value)
            if(!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or
                >= '0' and <= '9' or '_'))
                return false;
        return true;
    }

    private static async Task<byte[]?> ReadFrameAsync(SslStream tls, CancellationToken cancellationToken)
    {
        byte[] header = new byte[WireProtocol.HeaderBytes];
        try { await tls.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false); }
        catch (EndOfStreamException) { return null; }
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        if (length < WireProtocol.HeaderBytes || length > WireProtocol.MaximumFrameBytes) return null;
        byte[] frame = new byte[length];
        header.CopyTo(frame, 0);
        try
        {
            if (length > header.Length)
                await tls.ReadExactlyAsync(frame.AsMemory(header.Length), cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException) { return null; }
        return WireProtocol.TryReadHeader(frame, out _) ? frame : null;
    }

    private static async Task WriteFrameAsync(SslStream tls, byte[] frame, CancellationToken cancellationToken)
    {
        await tls.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
