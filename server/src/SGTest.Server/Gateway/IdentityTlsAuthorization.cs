using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public sealed record CharacterCreationPolicy(CharacterSpawn Spawn);

/// <summary>
/// TLS-only login/character selection boundary. It never creates accounts and it
/// never trusts a client-supplied ActorId: CharacterChoose is matched against the
/// authenticated account, then the persistent character_id is passed to the
/// trusted World lifecycle API.
/// </summary>
public sealed class IdentityTlsAuthorization
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
    private readonly NpcQuestCatalog? _quests;
    private readonly PlayerTradeCoordinator? _trades;

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
                                     NpcQuestCatalog? quests = null,
                                     PlayerTradeCoordinator? trades = null)
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
        _quests = quests;
        _trades = trades;
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

        async ValueTask<GatewayConnection?> Authorize(SslStream stream, CancellationToken token)
        {
            var connection = await AuthorizeAndBindAsync(stream, token).ConfigureAwait(false);
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
                if (attached) boundConnection = connection;
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
            // A pending trade is session-scoped. Disconnect before detaching the
            // route so the peer can receive one authoritative terminal event.
            _trades?.Disconnect(binding,router);
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

        try
        {
            // Map transfers and pickup settlement for this authenticated
            // socket must not race one another across separate async workers.
            using var gameplayGate = new SemaphoreSlim(1,1);
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
                        _trades?.Disconnect(oldBinding,router);

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
            async Task RespawnHandler(TrustedPlayerRespawn request)
            {
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                var binding = new ConnectionBinding(
                    request.ActorId, request.ConnectionEpoch, request.RequestMapEpoch);
                if (!router.IsCurrentBinding(binding))
                    return;

                PlayerRespawnOutcome outcome;
                ActorSnapshot? revived = null;
                if (_combat is null)
                {
                    outcome = new(PlayerRespawnStatus.CombatUnavailable, 0, 0);
                }
                else
                {
                    outcome = await _combat.RespawnFullAsync(
                        request.ActorId, request.ConnectionEpoch,
                        request.RequestMapEpoch, request.RespawnSequence)
                        .ConfigureAwait(false);

                    if (outcome.Status == PlayerRespawnStatus.Accepted)
                    {
                        revived = await _world.TrustedSetCombatDeadAsync(
                            request.ActorId, request.ConnectionEpoch,
                            request.RequestMapEpoch, false).ConfigureAwait(false);
                        if (revived is null)
                            outcome = new(PlayerRespawnStatus.StaleGeneration,
                                outcome.CurrentHp, outcome.MaxHp);
                    }
                }

                var sent = router.QueuePlayerRespawnResult(request, outcome);
                if (sent != GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable player respawn result could not be queued: " + sent);

                if (outcome.Status == PlayerRespawnStatus.Accepted &&
                    revived is { } visualActor &&
                    !router.TryPostPlayerRespawnVisual(
                        visualActor,request.RespawnSequence))
                    throw new AoiBackpressureException(
                        "Committed respawn visual queue full; event cannot be dropped");

                // Queue the authoritative full-HP baseline BEFORE reopening AI
                // targetability, so later PlayerDamage cannot overtake RespawnResult.
                if (outcome.Status == PlayerRespawnStatus.Accepted &&
                    revived is { } actor &&
                    (_combat is null || !_combat.TryResume(actor)))
                    throw new InvalidOperationException(
                        "Combat generation could not resume after player respawn");
                }
                finally { gameplayGate.Release(); }
            }

            async Task PickupHandler(TrustedGroundPickup request)
            {
                var binding = new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!router.IsCurrentBinding(binding))
                        return;
                    ActorSnapshot? snapshot = await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    PickupResult result;
                    if (snapshot is not { } actor ||
                        actor.ConnectionEpoch != request.ConnectionEpoch ||
                        actor.MapEpoch != request.RequestMapEpoch ||
                        actor.ActorId != request.ActorId)
                        result = new(PickupStatus.StaleGeneration,request.DropId);
                    else if (_combat is not null && _combat.CurrentHp(actor) <= 0)
                        result = new(PickupStatus.PlayerDead,request.DropId);
                    else
                        result = await _identity.ClaimGroundDropAsync(
                            new TrustedPickupContext(actor.ActorId,
                                _pickupWorldGeneration == 0
                                    ? actor.ConnectionEpoch // isolated fixture only
                                    : SqliteIdentityStore.ComposePickupConnectionEpoch(
                                        _pickupWorldGeneration,actor.ConnectionEpoch),
                                actor.MapEpoch,
                                request.PickupSequence,actor.MapId,actor.X,actor.Y),
                            request.DropId).ConfigureAwait(false);

                    // SQLite commits first. Never remove on rejection or
                    // client intent alone. A failed notification is terminal,
                    // not a fabricated successful response.
                    if (result.Status == PickupStatus.Granted &&
                        !router.TryPostGroundDropClaimed(result.DropId))
                        throw new IOException(
                            "Committed drop claimed but shared AOI notification was full");

                    var sent = router.QueueGroundPickupResult(request,result);
                    if (sent != GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable pickup result could not be queued: " + sent);
                }
                finally { gameplayGate.Release(); }
            }

            async Task RecoveryHandler(TrustedRecoveryUse request)
            {
                // The World/AOI owner and existing auth binding supply ALL
                // identity fields. A client can only choose its inventory UID.
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if(!router.IsCurrentBinding(binding))return;
                    var snapshot=await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    RecoveryUseResult result;
                    ActorSnapshot? suspendedActor=null;
                    if(snapshot is not { } actor ||
                        actor.ActorId!=request.ActorId ||
                        actor.ConnectionEpoch!=request.ConnectionEpoch ||
                        actor.MapEpoch!=request.RequestMapEpoch)
                    {
                        result=new(RecoveryUseStatus.CombatUnavailable,request.ItemUid);
                    }
                    else if(_combat is null)
                        result=new(RecoveryUseStatus.CombatUnavailable,request.ItemUid);
                    else if(_recoveryRules is null)
                        result=new(RecoveryUseStatus.NoRule,request.ItemUid);
                    else
                    {
                        // AI reservations must be blocked and all outstanding
                        // SQLite damage committed before the HP + item TX.
                        await _combat.SuspendAndDrainAsync(
                            actor.ActorId,actor.ConnectionEpoch,
                            actor.MapEpoch).ConfigureAwait(false);
                        suspendedActor=actor;
                        if(!_combat.TryGetSuspendedHp(actor,out int hp))
                            result=new(RecoveryUseStatus.CombatUnavailable,request.ItemUid);
                        else if(hp<=0)
                            result=new(RecoveryUseStatus.Dead,request.ItemUid);
                        else
                        {
                            long durableConnection=_pickupWorldGeneration==0
                                ? actor.ConnectionEpoch // isolated test only
                                : SqliteIdentityStore.ComposePickupConnectionEpoch(
                                    _pickupWorldGeneration,actor.ConnectionEpoch);
                            result=await _identity.UseVerifiedRecoveryItemAsync(
                                new TrustedRecoveryUseContext(
                                    actor.ActorId,durableConnection,
                                    actor.MapEpoch,request.UseSequence,hp),
                                request.ItemUid,_recoveryRules).ConfigureAwait(false);
                            if(result.Status==RecoveryUseStatus.Granted &&
                                !_combat.TryApplyCommittedRecovery(actor,result))
                                throw new InvalidOperationException(
                                    "Committed recovery diverged from reserved Combat state");
                        }
                    }
                    var sent=router.QueueRecoveryUseResult(request,result);
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable recovery receipt could not be queued: "+sent);
                    // Resume AI only AFTER reliable result is in the same FIFO,
                    // ensuring later PlayerDamage cannot overtake HP restoration.
                    if(suspendedActor is { } resumed &&
                        !_combat!.TryResume(resumed))
                        throw new InvalidOperationException(
                            "Recovery Combat generation could not resume");
                }
                finally { gameplayGate.Release(); }
            }

            async Task SkillHandler(TrustedSkillUse request)
            {
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                ActorSnapshot? suspended=null;
                try
                {
                    if(!router.IsCurrentBinding(binding))return;

                    var preflight=await router.PrepareSkillAsync(request)
                        .ConfigureAwait(false);
                    if(!preflight.Ready)
                    {
                        var rejected=preflight.Rejected ??
                            throw new InvalidOperationException(
                                "Skill preflight rejection missing outcome");
                        var sent=router.QueueSkillUseResult(request,rejected);
                        if(sent!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable skill preflight rejection could not be queued: "+sent);
                        return;
                    }

                    var prepared=preflight.Prepared!.Value;
                    if(prepared.Rule.MpCost==0)
                    {
                        await router.FinishPreparedSkillAsync(prepared)
                            .ConfigureAwait(false);
                        return;
                    }

                    if(_combat is null)
                    {
                        await router.FinishPreparedSkillAsync(
                            prepared,SkillUseStatus.CombatUnavailable)
                            .ConfigureAwait(false);
                        return;
                    }

                    var snapshot=await _world.TrustedGetActorAsync(request.ActorId)
                        .ConfigureAwait(false);
                    if(snapshot is not { } actor ||
                        actor.ActorId!=request.ActorId ||
                        actor.ConnectionEpoch!=request.ConnectionEpoch ||
                        actor.MapEpoch!=request.RequestMapEpoch)
                    {
                        await router.FinishPreparedSkillAsync(
                            prepared,SkillUseStatus.OldMapEpoch)
                            .ConfigureAwait(false);
                        return;
                    }

                    await _combat.SuspendAndDrainAsync(
                        actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch)
                        .ConfigureAwait(false);
                    suspended=actor;
                    if(!_combat.TryGetSuspendedCombatState(
                        actor,out int hp,out int maxHp) || hp<=0)
                    {
                        await router.FinishPreparedSkillAsync(
                            prepared,SkillUseStatus.CombatUnavailable)
                            .ConfigureAwait(false);
                        return;
                    }

                    long durableConnection=_pickupWorldGeneration==0
                        ? actor.ConnectionEpoch
                        : SqliteIdentityStore.ComposePickupConnectionEpoch(
                            _pickupWorldGeneration,actor.ConnectionEpoch);
                    var mp=await _identity.ConsumeSkillMpAsync(
                        new TrustedSkillMpContext(
                            actor.ActorId,durableConnection,actor.MapEpoch,
                            request.SkillSequence,request.SkillId),
                        prepared.Rule.MpCost).ConfigureAwait(false);

                    SkillUseStatus? cancel=mp.Status switch
                    {
                        SkillMpConsumeStatus.Consumed or
                        SkillMpConsumeStatus.AlreadyConsumed => null,
                        SkillMpConsumeStatus.InsufficientMp =>
                            SkillUseStatus.InsufficientMp,
                        SkillMpConsumeStatus.StaleSequence =>
                            SkillUseStatus.ReplayedSequence,
                        _ => SkillUseStatus.CombatUnavailable
                    };

                    if(cancel is null)
                    {
                        var baseline=router.QueuePlayerCombatState(
                            binding,hp,maxHp,mp.CurrentMp,mp.MaxMp);
                        if(baseline!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Committed skill MP baseline could not be queued: "+baseline);
                    }

                    await router.FinishPreparedSkillAsync(prepared,cancel)
                        .ConfigureAwait(false);
                }
                finally
                {
                    if(suspended is { } actor &&
                        router.IsCurrentBinding(binding) &&
                        !_combat!.TryResume(actor))
                        throw new InvalidOperationException(
                            "Skill Combat generation could not resume");
                    gameplayGate.Release();
                }
            }


            async Task ShopHandler(TrustedShopCommand request)
            {
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if(!router.IsCurrentBinding(binding))return;
                    var snapshot=await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    if(snapshot is not { } actor||
                       actor.ActorId!=request.ActorId||
                       actor.ConnectionEpoch!=request.ConnectionEpoch||
                       actor.MapEpoch!=request.RequestMapEpoch)
                    {
                        if(request.Kind==TrustedShopCommandKind.Open)
                        {
                            var stale=router.QueueShopSnapshot(
                                request,ShopOpenStatus.StaleGeneration,0,
                                Array.Empty<NpcShopCatalog.Item>());
                            if(stale!=GatewaySendStatus.Enqueued)
                                throw new IOException(
                                    "Reliable stale shop snapshot could not be queued: "+stale);
                        }
                        else
                        {
                            var kind=request.Kind==TrustedShopCommandKind.Buy
                                ? ShopTransactionKind.Buy:ShopTransactionKind.Sell;
                            var staleResult=new ShopTransactionResult(
                                ShopTransactionStatus.StaleGeneration,kind,
                                request.NpcSpawnId,Math.Max(0,request.ItemUid),
                                request.ItemDefinitionId,request.Quantity,0,0);
                            var stale=router.QueueShopTransactionResult(request,staleResult);
                            if(stale!=GatewaySendStatus.Enqueued)
                                throw new IOException(
                                    "Reliable stale shop result could not be queued: "+stale);
                        }
                        return;
                    }

                    bool dead=_combat is not null&&_combat.CurrentHp(actor)<=0;
                    NpcShopCatalog.Shop? shop=null;
                    ShopAccessStatus access;
                    if(dead)access=ShopAccessStatus.PlayerDead;
                    else if(_shops is null)
                        access=ShopAccessStatus.UnknownShop;
                    else
                    {
                        access=_shops.Access(actor,request.NpcSpawnId);
                        if(access==ShopAccessStatus.Allowed)
                            _shops.TryGet(actor.MapId,request.NpcSpawnId,out shop!);
                    }

                    if(request.Kind==TrustedShopCommandKind.Open)
                    {
                        ShopOpenStatus status=access switch
                        {
                            ShopAccessStatus.Allowed=>ShopOpenStatus.Opened,
                            ShopAccessStatus.UnknownShop=>ShopOpenStatus.UnknownShop,
                            ShopAccessStatus.TooFar=>ShopOpenStatus.TooFar,
                            ShopAccessStatus.PlayerDead=>ShopOpenStatus.PlayerDead,
                            _=>ShopOpenStatus.StaleGeneration
                        };
                        long money=status==ShopOpenStatus.Opened
                            ? await _identity.GetCharacterMoneyAsync(actor.ActorId)
                                .ConfigureAwait(false):0;
                        var items=status==ShopOpenStatus.Opened
                            ? shop!.Items.Values.ToArray()
                            : Array.Empty<NpcShopCatalog.Item>();
                        var sent=router.QueueShopSnapshot(request,status,money,items);
                        if(sent!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable shop snapshot could not be queued: "+sent);
                        return;
                    }

                    int itemDefinitionId=request.ItemDefinitionId;
                    TrustedShopPrice price;
                    if(shop is not null&&_shops is not null)
                        price=_shops.Price(shop,itemDefinitionId);
                    else
                        price=new TrustedShopPrice(
                            request.NpcSpawnId,itemDefinitionId,0,0,
                            "shop-unavailable");

                    long durableConnection=_pickupWorldGeneration==0
                        ? actor.ConnectionEpoch
                        : SqliteIdentityStore.ComposePickupConnectionEpoch(
                            _pickupWorldGeneration,actor.ConnectionEpoch);
                    var context=new TrustedShopContext(
                        actor.ActorId,durableConnection,actor.MapEpoch,
                        request.ShopSequence);
                    ShopTransactionResult result=request.Kind switch
                    {
                        TrustedShopCommandKind.Buy=>
                            await _identity.BuyFromShopAsync(
                                context,access,price,request.Quantity)
                                .ConfigureAwait(false),
                        TrustedShopCommandKind.Sell=>
                            await _identity.SellToShopAsync(
                                context,access,price,request.ItemUid,
                                request.Quantity).ConfigureAwait(false),
                        _=>throw new InvalidOperationException(
                            "Unhandled trusted shop command")
                    };
                    var queued=router.QueueShopTransactionResult(request,result);
                    if(queued!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable shop transaction result could not be queued: "+queued);
                }
                finally { gameplayGate.Release(); }
            }

            async Task StorageHandler(TrustedStorageCommand request)
            {
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if(!router.IsCurrentBinding(binding))return;
                    var snapshot=await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    if(snapshot is not { } actor||
                       actor.ActorId!=request.ActorId||
                       actor.ConnectionEpoch!=request.ConnectionEpoch||
                       actor.MapEpoch!=request.RequestMapEpoch)
                    {
                        if(request.Kind==TrustedStorageCommandKind.Open)
                        {
                            var stale=router.QueueStorageSnapshot(
                                request,StorageOpenStatus.StaleGeneration,
                                Array.Empty<StoredItem>());
                            if(stale!=GatewaySendStatus.Enqueued)
                                throw new IOException(
                                    "Reliable stale storage snapshot could not be queued: "+stale);
                        }
                        else
                        {
                            var kind=request.Kind==TrustedStorageCommandKind.Deposit
                                ? StorageTransferKind.Deposit:StorageTransferKind.Withdraw;
                            var staleResult=new StorageTransferResult(
                                StorageTransferStatus.StaleGeneration,kind,
                                request.ItemUid,0,0,0);
                            var stale=router.QueueStorageTransferResult(request,staleResult);
                            if(stale!=GatewaySendStatus.Enqueued)
                                throw new IOException(
                                    "Reliable stale storage result could not be queued: "+stale);
                        }
                        return;
                    }

                    bool dead=_combat is not null&&_combat.CurrentHp(actor)<=0;
                    StorageAccessStatus access=dead
                        ? StorageAccessStatus.PlayerDead
                        : _storages is null
                            ? StorageAccessStatus.UnknownStorage
                            : _storages.Access(actor,request.NpcSpawnId);

                    if(request.Kind==TrustedStorageCommandKind.Open)
                    {
                        StorageOpenStatus status=access switch
                        {
                            StorageAccessStatus.Allowed=>StorageOpenStatus.Opened,
                            StorageAccessStatus.UnknownStorage=>StorageOpenStatus.UnknownStorage,
                            StorageAccessStatus.TooFar=>StorageOpenStatus.TooFar,
                            StorageAccessStatus.PlayerDead=>StorageOpenStatus.PlayerDead,
                            _=>StorageOpenStatus.StaleGeneration
                        };
                        IReadOnlyList<StoredItem> items=Array.Empty<StoredItem>();
                        if(status==StorageOpenStatus.Opened)
                        {
                            items=await _identity.ListStorageItemsAsync(
                                actor.ActorId,WireProtocol.StorageSnapshotMaxItems+1)
                                .ConfigureAwait(false);
                            if(items.Count>WireProtocol.StorageSnapshotMaxItems)
                                throw new InvalidOperationException(
                                    "Storage exceeds bounded V1 snapshot capacity");
                        }
                        var queued=router.QueueStorageSnapshot(request,status,items);
                        if(queued!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable storage snapshot could not be queued: "+queued);
                        return;
                    }

                    var transferKind=request.Kind==TrustedStorageCommandKind.Deposit
                        ? StorageTransferKind.Deposit:StorageTransferKind.Withdraw;
                    StorageTransferResult result;
                    if(access!=StorageAccessStatus.Allowed)
                    {
                        var rejected=access switch
                        {
                            StorageAccessStatus.UnknownStorage=>StorageTransferStatus.UnknownStorage,
                            StorageAccessStatus.TooFar=>StorageTransferStatus.TooFar,
                            StorageAccessStatus.PlayerDead=>StorageTransferStatus.PlayerDead,
                            _=>StorageTransferStatus.StaleGeneration
                        };
                        result=new(rejected,transferKind,request.ItemUid,0,0,0);
                    }
                    else
                    {
                        long durableConnection=_pickupWorldGeneration==0
                            ? actor.ConnectionEpoch
                            : SqliteIdentityStore.ComposePickupConnectionEpoch(
                                _pickupWorldGeneration,actor.ConnectionEpoch);
                        var context=new TrustedStorageContext(
                            actor.ActorId,durableConnection,actor.MapEpoch,
                            request.StorageSequence);
                        result=request.Kind switch
                        {
                            TrustedStorageCommandKind.Deposit=>
                                await _identity.DepositInventoryItemAsync(
                                    context,request.ItemUid).ConfigureAwait(false),
                            TrustedStorageCommandKind.Withdraw=>
                                await _identity.WithdrawStorageItemAsync(
                                    context,request.ItemUid).ConfigureAwait(false),
                            _=>throw new InvalidOperationException(
                                "Unhandled trusted storage command")
                        };
                    }
                    var sent=router.QueueStorageTransferResult(request,result);
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable storage transfer result could not be queued: "+sent);
                }
                finally { gameplayGate.Release(); }
            }

            async Task EquipmentHandler(TrustedEquipmentCommand request)
            {
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if(!router.IsCurrentBinding(binding))return;
                    var snapshot=await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    var transferKind=request.Kind==TrustedEquipmentCommandKind.Equip
                        ? EquipmentTransferKind.Equip:EquipmentTransferKind.Unequip;
                    EquipmentTransferResult result;
                    if(snapshot is not { } actor||
                       actor.ActorId!=request.ActorId||
                       actor.ConnectionEpoch!=request.ConnectionEpoch||
                       actor.MapEpoch!=request.RequestMapEpoch)
                    {
                        result=new(EquipmentTransferStatus.StaleGeneration,
                            transferKind,request.ItemUid,0,0,0);
                    }
                    else
                    {
                        long durableConnection=_pickupWorldGeneration==0
                            ? actor.ConnectionEpoch
                            : SqliteIdentityStore.ComposePickupConnectionEpoch(
                                _pickupWorldGeneration,actor.ConnectionEpoch);
                        var context=new TrustedEquipmentContext(
                            actor.ActorId,durableConnection,actor.MapEpoch,
                            request.EquipmentSequence);
                        if(request.Kind==TrustedEquipmentCommandKind.Equip)
                        {
                            result=_equipmentRules is null
                                ? new(EquipmentTransferStatus.NoRules,
                                    EquipmentTransferKind.Equip,request.ItemUid,0,0,0)
                                : await _identity.EquipInventoryItemAsync(
                                    context,request.ItemUid,_equipmentRules)
                                    .ConfigureAwait(false);
                        }
                        else
                        {
                            result=await _identity.UnequipItemAsync(
                                context,request.ItemUid).ConfigureAwait(false);
                        }
                    }
                    var sent=router.QueueEquipmentTransferResult(request,result);
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable equipment transfer result could not be queued: "+sent);
                }
                finally { gameplayGate.Release(); }
            }

            async Task BankHandler(TrustedBankCommand request)
            {
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if(!router.IsCurrentBinding(binding))return;
                    var snapshot=await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    if(snapshot is not { } actor||
                       actor.ActorId!=request.ActorId||
                       actor.ConnectionEpoch!=request.ConnectionEpoch||
                       actor.MapEpoch!=request.RequestMapEpoch)
                    {
                        if(request.Kind==TrustedBankCommandKind.Open)
                        {
                            var stale=router.QueueBankSnapshot(
                                request,BankOpenStatus.StaleGeneration,null);
                            if(stale!=GatewaySendStatus.Enqueued)
                                throw new IOException(
                                    "Reliable stale bank snapshot could not be queued: "+stale);
                        }
                        else
                        {
                            var kind=request.Kind==TrustedBankCommandKind.Deposit
                                ? BankTransactionKind.Deposit:BankTransactionKind.Withdraw;
                            var staleResult=new BankTransactionResult(
                                BankTransactionStatus.StaleGeneration,kind,
                                request.NpcSpawnId,BankBalanceScope.None,
                                request.Amount,0,0);
                            var stale=router.QueueBankTransactionResult(request,staleResult);
                            if(stale!=GatewaySendStatus.Enqueued)
                                throw new IOException(
                                    "Reliable stale bank result could not be queued: "+stale);
                        }
                        return;
                    }

                    bool dead=_combat is not null&&_combat.CurrentHp(actor)<=0;
                    BankNpcAccessStatus access=dead
                        ? BankNpcAccessStatus.PlayerDead
                        : _banks is null
                            ? BankNpcAccessStatus.UnknownBank
                            : _banks.Access(actor,request.NpcSpawnId);

                    if(request.Kind==TrustedBankCommandKind.Open)
                    {
                        BankOpenStatus status=access switch
                        {
                            BankNpcAccessStatus.Allowed=>BankOpenStatus.Opened,
                            BankNpcAccessStatus.UnknownBank=>BankOpenStatus.UnknownBank,
                            BankNpcAccessStatus.TooFar=>BankOpenStatus.TooFar,
                            BankNpcAccessStatus.PlayerDead=>BankOpenStatus.PlayerDead,
                            _=>BankOpenStatus.StaleGeneration
                        };
                        BankBalanceSnapshot? balance=null;
                        if(status==BankOpenStatus.Opened)
                        {
                            if(_banks is null||
                               !_banks.TryGet(actor.MapId,request.NpcSpawnId,out var point))
                                throw new InvalidOperationException("Bank rule vanished after access check");
                            var scope=point.Scope==BankScopePolicy.Account
                                ? BankBalanceScope.Account:BankBalanceScope.Character;
                            balance=await _identity.GetBankBalanceAsync(
                                actor.ActorId,scope).ConfigureAwait(false);
                        }
                        var queued=router.QueueBankSnapshot(request,status,balance);
                        if(queued!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable bank snapshot could not be queued: "+queued);
                        return;
                    }

                    var transferKind=request.Kind==TrustedBankCommandKind.Deposit
                        ? BankTransactionKind.Deposit:BankTransactionKind.Withdraw;
                    BankTransactionResult result;
                    if(access!=BankNpcAccessStatus.Allowed)
                    {
                        var rejected=access switch
                        {
                            BankNpcAccessStatus.UnknownBank=>BankTransactionStatus.UnknownBank,
                            BankNpcAccessStatus.TooFar=>BankTransactionStatus.TooFar,
                            BankNpcAccessStatus.PlayerDead=>BankTransactionStatus.PlayerDead,
                            _=>BankTransactionStatus.StaleGeneration
                        };
                        result=new(rejected,transferKind,request.NpcSpawnId,
                            BankBalanceScope.None,request.Amount,0,0);
                    }
                    else
                    {
                        if(_banks is null||
                           !_banks.TryGet(actor.MapId,request.NpcSpawnId,out var point))
                            throw new InvalidOperationException("Bank rule vanished after access check");
                        var scope=point.Scope==BankScopePolicy.Account
                            ? BankBalanceScope.Account:BankBalanceScope.Character;
                        long durableConnection=_pickupWorldGeneration==0
                            ? actor.ConnectionEpoch
                            : SqliteIdentityStore.ComposePickupConnectionEpoch(
                                _pickupWorldGeneration,actor.ConnectionEpoch);
                        var context=new TrustedBankContext(
                            actor.ActorId,durableConnection,actor.MapEpoch,
                            request.BankSequence);
                        var rule=new TrustedBankRule(
                            point.NpcSpawnId,scope,point.SourceReference);
                        result=request.Kind switch
                        {
                            TrustedBankCommandKind.Deposit=>
                                await _identity.DepositBankAsync(
                                    context,BankAccessStatus.Allowed,rule,
                                    request.Amount).ConfigureAwait(false),
                            TrustedBankCommandKind.Withdraw=>
                                await _identity.WithdrawBankAsync(
                                    context,BankAccessStatus.Allowed,rule,
                                    request.Amount).ConfigureAwait(false),
                            _=>throw new InvalidOperationException(
                                "Unhandled trusted bank command")
                        };
                    }
                    var sent=router.QueueBankTransactionResult(request,result);
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable bank transaction result could not be queued: "+sent);
                }
                finally { gameplayGate.Release(); }
            }

            async Task RepairHandler(TrustedRepairCommand request)
            {
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if(!router.IsCurrentBinding(binding))return;
                    var snapshot=await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    if(snapshot is not { } actor||
                       actor.ActorId!=request.ActorId||
                       actor.ConnectionEpoch!=request.ConnectionEpoch||
                       actor.MapEpoch!=request.RequestMapEpoch)
                    {
                        if(request.Kind==TrustedRepairCommandKind.Open)
                        {
                            var stale=router.QueueRepairSnapshot(
                                request,RepairOpenStatus.StaleGeneration,null);
                            if(stale!=GatewaySendStatus.Enqueued)
                                throw new IOException(
                                    "Reliable stale repair snapshot could not be queued: "+stale);
                        }
                        else
                        {
                            var staleResult=new RepairResult(
                                RepairStatus.StaleGeneration,request.NpcSpawnId,
                                request.ItemUid,0,RepairItemLocation.None,0,0,0,0);
                            var stale=router.QueueRepairResult(request,staleResult);
                            if(stale!=GatewaySendStatus.Enqueued)
                                throw new IOException(
                                    "Reliable stale repair result could not be queued: "+stale);
                        }
                        return;
                    }

                    bool dead=_combat is not null&&_combat.CurrentHp(actor)<=0;
                    RepairNpcAccessStatus access=dead
                        ? RepairNpcAccessStatus.PlayerDead
                        : _repairs is null||_repairItemRules is null
                            ? RepairNpcAccessStatus.UnknownRepair
                            : _repairs.Access(actor,request.NpcSpawnId);

                    if(request.Kind==TrustedRepairCommandKind.Open)
                    {
                        RepairOpenStatus status=access switch
                        {
                            RepairNpcAccessStatus.Allowed=>RepairOpenStatus.Opened,
                            RepairNpcAccessStatus.UnknownRepair=>RepairOpenStatus.UnknownRepair,
                            RepairNpcAccessStatus.TooFar=>RepairOpenStatus.TooFar,
                            RepairNpcAccessStatus.PlayerDead=>RepairOpenStatus.PlayerDead,
                            _=>RepairOpenStatus.StaleGeneration
                        };
                        RepairPreview? preview=null;
                        if(status==RepairOpenStatus.Opened)
                        {
                            if(_repairs is null||_repairItemRules is null||
                               !_repairs.TryGet(actor.MapId,request.NpcSpawnId,out var point))
                                throw new InvalidOperationException(
                                    "Repair rule vanished after access check");
                            var rule=new TrustedRepairNpcRule(
                                point.NpcSpawnId,point.RepairRate,point.SourceReference);
                            preview=await _identity.ListRepairPreviewAsync(
                                actor.ActorId,rule,_repairItemRules).ConfigureAwait(false);
                        }
                        var queued=router.QueueRepairSnapshot(request,status,preview);
                        if(queued!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable repair snapshot could not be queued: "+queued);
                        return;
                    }

                    RepairResult result;
                    if(access!=RepairNpcAccessStatus.Allowed)
                    {
                        var rejected=access switch
                        {
                            RepairNpcAccessStatus.UnknownRepair=>RepairStatus.UnknownRepair,
                            RepairNpcAccessStatus.TooFar=>RepairStatus.TooFar,
                            RepairNpcAccessStatus.PlayerDead=>RepairStatus.PlayerDead,
                            _=>RepairStatus.StaleGeneration
                        };
                        result=new(rejected,request.NpcSpawnId,request.ItemUid,
                            0,RepairItemLocation.None,0,0,0,0);
                    }
                    else
                    {
                        if(_repairs is null||_repairItemRules is null||
                           !_repairs.TryGet(actor.MapId,request.NpcSpawnId,out var point))
                            throw new InvalidOperationException(
                                "Repair rule vanished after access check");
                        long durableConnection=_pickupWorldGeneration==0
                            ? actor.ConnectionEpoch
                            : SqliteIdentityStore.ComposePickupConnectionEpoch(
                                _pickupWorldGeneration,actor.ConnectionEpoch);
                        var context=new TrustedRepairContext(
                            actor.ActorId,durableConnection,actor.MapEpoch,
                            request.RepairSequence);
                        var rule=new TrustedRepairNpcRule(
                            point.NpcSpawnId,point.RepairRate,point.SourceReference);
                        result=await _identity.RepairItemAsync(
                            context,RepairAccessStatus.Allowed,rule,
                            _repairItemRules,request.ItemUid).ConfigureAwait(false);
                    }
                    var sent=router.QueueRepairResult(request,result);
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable repair result could not be queued: "+sent);
                }
                finally { gameplayGate.Release(); }
            }

            async Task QuestHandler(TrustedQuestCommand request)
            {
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if(!router.IsCurrentBinding(binding))return;
                    var snapshot=await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    if(snapshot is not { } actor||
                       actor.ActorId!=request.ActorId||
                       actor.ConnectionEpoch!=request.ConnectionEpoch||
                       actor.MapEpoch!=request.RequestMapEpoch)
                    {
                        var stale=router.QueueQuestView(
                            request,QuestViewWireStatus.StaleGeneration,null);
                        if(stale!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable stale quest view could not be queued: "+stale);
                        return;
                    }

                    bool dead=_combat is not null&&_combat.CurrentHp(actor)<=0;
                    QuestNpcAccessStatus access=dead
                        ? QuestNpcAccessStatus.PlayerDead
                        : _quests is null
                            ? QuestNpcAccessStatus.UnknownQuest
                            : _quests.Access(actor,request.NpcSpawnId);
                    if(access!=QuestNpcAccessStatus.Allowed)
                    {
                        var status=access switch
                        {
                            QuestNpcAccessStatus.UnknownQuest=>QuestViewWireStatus.UnknownQuest,
                            QuestNpcAccessStatus.TooFar=>QuestViewWireStatus.TooFar,
                            QuestNpcAccessStatus.PlayerDead=>QuestViewWireStatus.PlayerDead,
                            _=>QuestViewWireStatus.StaleGeneration
                        };
                        var rejected=router.QueueQuestView(request,status,null);
                        if(rejected!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable rejected quest view could not be queued: "+rejected);
                        return;
                    }

                    if(_quests is null||
                       !_quests.TryGet(actor.MapId,request.NpcSpawnId,out var point))
                        throw new InvalidOperationException(
                            "Quest rule vanished after access check");

                    if(request.Kind==TrustedQuestCommandKind.Open)
                    {
                        var current=await _identity.GetQuestViewAsync(
                            actor.ActorId,point.Rule).ConfigureAwait(false);
                        var status=current.Status==QuestViewStatus.Ready
                            ? QuestViewWireStatus.Ready
                            : QuestViewWireStatus.RuleRevisionMismatch;
                        var queued=router.QueueQuestView(request,status,current.View);
                        if(queued!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable quest open view could not be queued: "+queued);
                        return;
                    }

                    if(request.QuestId!=point.Rule.QuestId)
                    {
                        var unknown=router.QueueQuestView(
                            request,QuestViewWireStatus.UnknownQuest,null);
                        if(unknown!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable quest identity rejection could not be queued: "+unknown);
                        return;
                    }

                    long durableConnection=_pickupWorldGeneration==0
                        ? actor.ConnectionEpoch
                        : SqliteIdentityStore.ComposePickupConnectionEpoch(
                            _pickupWorldGeneration,actor.ConnectionEpoch);
                    var context=new TrustedQuestContext(
                        actor.ActorId,durableConnection,actor.MapEpoch,
                        request.QuestSequence);
                    var result=await _identity.ApplyQuestChoiceAsync(
                        context,QuestAccessStatus.Allowed,point.NpcSpawnId,
                        point.Rule,request.ChoiceId).ConfigureAwait(false);
                    var wireStatus=result.Status switch
                    {
                        QuestChoiceStatus.Applied=>QuestViewWireStatus.Applied,
                        QuestChoiceStatus.AlreadyApplied=>QuestViewWireStatus.AlreadyApplied,
                        QuestChoiceStatus.UnknownQuest=>QuestViewWireStatus.UnknownQuest,
                        QuestChoiceStatus.TooFar=>QuestViewWireStatus.TooFar,
                        QuestChoiceStatus.InvalidChoice=>QuestViewWireStatus.InvalidChoice,
                        QuestChoiceStatus.AlreadyCompleted=>QuestViewWireStatus.AlreadyCompleted,
                        QuestChoiceStatus.StaleSequence=>QuestViewWireStatus.StaleSequence,
                        QuestChoiceStatus.StaleGeneration=>QuestViewWireStatus.StaleGeneration,
                        QuestChoiceStatus.PlayerDead=>QuestViewWireStatus.PlayerDead,
                        QuestChoiceStatus.RuleRevisionMismatch=>
                            QuestViewWireStatus.RuleRevisionMismatch,
                        _=>throw new InvalidOperationException(
                            "Unhandled quest choice status")
                    };
                    QuestView? view=null;
                    if(wireStatus is QuestViewWireStatus.Applied or
                        QuestViewWireStatus.AlreadyApplied or
                        QuestViewWireStatus.InvalidChoice or
                        QuestViewWireStatus.AlreadyCompleted)
                    {
                        var current=await _identity.GetQuestViewAsync(
                            actor.ActorId,point.Rule).ConfigureAwait(false);
                        if(current.Status==QuestViewStatus.Ready)
                            view=current.View;
                        else
                            wireStatus=QuestViewWireStatus.RuleRevisionMismatch;
                    }
                    var sent=router.QueueQuestView(request,wireStatus,view);
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable quest choice view could not be queued: "+sent);
                }
                finally { gameplayGate.Release(); }
            }

            async Task TradeHandler(TrustedTradeCommand request)
            {
                if(_trades is null)return;
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if(!router.IsCurrentBinding(binding))return;
                    await _trades.HandleAsync(request,router).ConfigureAwait(false);
                }
                finally { gameplayGate.Release(); }
            }
            Func<TrustedTradeCommand,Task>? tradeHandler=
                _trades is null ? null : TradeHandler;

            async Task DiscardHandler(TrustedInventoryDiscard request)
            {
                var binding=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if(!router.IsCurrentBinding(binding))return;
                    var snapshot=await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    InventoryDiscardResult result;
                    if(snapshot is not { } actor ||
                        actor.ActorId!=request.ActorId ||
                        actor.ConnectionEpoch!=request.ConnectionEpoch ||
                        actor.MapEpoch!=request.RequestMapEpoch)
                        result=new(InventoryDiscardStatus.StaleGeneration,request.ItemUid);
                    else
                    {
                        long durableConnection=_pickupWorldGeneration==0
                            ? actor.ConnectionEpoch
                            : SqliteIdentityStore.ComposePickupConnectionEpoch(
                                _pickupWorldGeneration,actor.ConnectionEpoch);
                        result=await _identity.DiscardInventoryItemAsync(
                            new TrustedInventoryDiscardContext(
                                actor.ActorId,durableConnection,actor.MapEpoch,
                                request.DiscardSequence),
                            request.ItemUid,request.Quantity).ConfigureAwait(false);
                    }
                    var sent=router.QueueInventoryDiscardResult(request,result);
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable inventory discard receipt could not be queued: "+sent);
                }
                finally { gameplayGate.Release(); }
            }

            return await TlsWireConnection.RunServerAsync(client, certificate,
                Authorize, _world.TryPostMove, options, cancellationToken, Release,
                npcHandler, router.TryRequestResync,
                router.TryRequestNormalAttack, RespawnHandler,
                PickupHandler, RecoveryHandler, DiscardHandler,
                handleSkill:SkillHandler,handleShop:ShopHandler,
                handleStorage:StorageHandler,
                handleEquipment:EquipmentHandler,
                handleBank:BankHandler,
                handleRepair:RepairHandler,
                handleTrade:tradeHandler,
                handleQuest:QuestHandler).ConfigureAwait(false);
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
                var binding = abandoned.Binding;
                if (_combat is not null)
                    await _combat.CloseAndDrainAsync(
                        binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
                _trades?.Disconnect(binding,router);
                router.TryDetach(abandoned);
                await _world.TrustedDisconnectAsync(
                    binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                    .ConfigureAwait(false);
            }
        }
    }

    public async ValueTask<GatewayConnection?> AuthorizeAndBindAsync(
        SslStream tls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tls);
        byte[]? authFrame = await ReadFrameAsync(tls, cancellationToken).ConfigureAwait(false);
        if (authFrame is null || !WireProtocol.TryDecodeAuthRequest(authFrame, out var request)) return null;

        var authentication = await _identity.AuthenticateAsync(
            request.Username, request.Password, cancellationToken).ConfigureAwait(false);
        AuthWireStatus authStatus = authentication.Status switch
        {
            AuthenticationStatus.Success => AuthWireStatus.Success,
            AuthenticationStatus.Disabled => AuthWireStatus.Disabled,
            _ => AuthWireStatus.InvalidCredentials
        };
        await WriteFrameAsync(tls, WireProtocol.EncodeAuthResult(new(authStatus)), cancellationToken)
            .ConfigureAwait(false);
        if (authentication.Status != AuthenticationStatus.Success || authentication.Account is null)
            return null;

        var account = authentication.Account;
        var characters = account.Characters.ToList();

        async Task SendCharacterListAsync()
        {
            var summaries = characters
                .Select(c => new CharacterSummaryWire(
                    c.CharacterId, checked((byte)c.Slot), c.Name))
                .ToArray();
            await WriteFrameAsync(tls,
                WireProtocol.EncodeCharacterList(new(summaries)),
                cancellationToken).ConfigureAwait(false);
        }

        await SendCharacterListAsync().ConfigureAwait(false);

        CharacterRecord? character = null;
        while (character is null)
        {
            byte[]? selectionFrame = await ReadFrameAsync(
                tls, cancellationToken).ConfigureAwait(false);
            if (selectionFrame is null) return null;

            if (WireProtocol.TryDecodeCharacterCreate(
                    selectionFrame, out var create))
            {
                CharacterCreateWireStatus wireStatus;
                CharacterRecord? createdCharacter = null;
                if (_characterCreation is null)
                {
                    wireStatus = CharacterCreateWireStatus.Unavailable;
                }
                else
                {
                    try
                    {
                        var result = await _identity.CreateCharacterAsync(
                            account.AccountId, create.Slot, create.Name,
                            _characterCreation.Spawn, cancellationToken)
                            .ConfigureAwait(false);
                        wireStatus = result.Status switch
                        {
                            CharacterCreateStatus.Created =>
                                CharacterCreateWireStatus.Success,
                            CharacterCreateStatus.SlotOccupied =>
                                CharacterCreateWireStatus.SlotOccupied,
                            CharacterCreateStatus.NameTaken =>
                                CharacterCreateWireStatus.NameTaken,
                            CharacterCreateStatus.LimitReached =>
                                CharacterCreateWireStatus.LimitReached,
                            _ => CharacterCreateWireStatus.Unavailable
                        };
                        createdCharacter = result.Character;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        wireStatus = CharacterCreateWireStatus.InvalidSlot;
                    }
                    catch (ArgumentException)
                    {
                        wireStatus = CharacterCreateWireStatus.InvalidName;
                    }
                }

                if (wireStatus == CharacterCreateWireStatus.Success &&
                    createdCharacter is not null)
                {
                    characters = (await _identity.ListCharactersAsync(
                        account.AccountId, cancellationToken)
                        .ConfigureAwait(false)).ToList();
                    await WriteFrameAsync(tls,
                        WireProtocol.EncodeCharacterCreateResult(new(
                            wireStatus, createdCharacter.CharacterId,
                            checked((byte)createdCharacter.Slot),
                            createdCharacter.Name)),
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WriteFrameAsync(tls,
                        WireProtocol.EncodeCharacterCreateResult(new(
                            wireStatus, 0, create.Slot, string.Empty)),
                        cancellationToken).ConfigureAwait(false);
                }

                // Always refresh after a create attempt. This lets the Client
                // recover from a concurrent slot/name conflict without
                // reconnecting and keeps the authoritative list server-owned.
                await SendCharacterListAsync().ConfigureAwait(false);
                continue;
            }

            if (!WireProtocol.TryDecodeCharacterChoose(
                    selectionFrame, out var choose))
                return null;
            character = characters.FirstOrDefault(
                c => c.CharacterId == choose.CharacterId);
            if (character is null)
            {
                await WriteFrameAsync(tls,
                    WireProtocol.EncodeCharacterChooseResult(new(
                        CharacterChooseWireStatus.NotOwned,
                        0, 0, 0, 0, 0)), cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }
        }

        CharacterCombatProfile? combat = await _identity.GetCharacterCombatProfileAsync(
            character.CharacterId, cancellationToken).ConfigureAwait(false);
        TrustedCombatProfile? trustedCombat = combat is null ? null : new(
            combat.Data.Attack, combat.Data.Defense, combat.Data.Hit, combat.Data.Dodge,
            combat.Data.AttackRange, combat.Data.AttackCooldownTicks);

        // Do not cancel ONLY the waiter: the trusted World queue may finish
        // registration after the TLS authorization timeout, leaking a character.
        ActorSnapshot? actor;
        try
        {
            actor = await _world.TrustedRegisterAsync(character.CharacterId,
                character.MapId, character.X, character.Y).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            actor = null;
        }
        if (actor is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                CharacterChooseWireStatus.WorldRejected, 0, 0, 0, 0, 0)), cancellationToken).ConfigureAwait(false);
            return null;
        }

        var snapshot = actor.Value;
        bool combatAttached = false;
        bool completed = false;
        try
        {
            if (combat is not null && _combat is not null)
            {
                combatAttached = _combat.TryAttach(snapshot, combat.Data);
                if (!combatAttached)
                    throw new InvalidOperationException(
                        "Combat settlement rejected authenticated character generation");
                if (combat.Data.CurrentHp == 0)
                {
                    var dead = await _world.TrustedSetCombatDeadAsync(
                        snapshot.ActorId, snapshot.ConnectionEpoch, snapshot.MapEpoch, true)
                        .ConfigureAwait(false);
                    if (dead is null)
                        throw new InvalidOperationException(
                            "Stored dead combat state could not gate authoritative World actor");
                }
            }

            // Bounded durable inventory baseline is read on the TLS async
            // authorization path, before the Gateway begins AOI egress.
            // Do not emit a partial/truncated snapshot. Item instances are
            // owned by the selected authenticated character only.
            var inventory = await _identity.ListInventoryItemsAsync(
                snapshot.ActorId,WireProtocol.InventorySnapshotMaxItems+1,
                cancellationToken).ConfigureAwait(false);
            if(inventory.Count>WireProtocol.InventorySnapshotMaxItems)
                throw new InvalidOperationException(
                    "Character inventory exceeds bounded V1 baseline capacity");

            var equipment = await _identity.ListEquippedItemsAsync(
                snapshot.ActorId,cancellationToken).ConfigureAwait(false);
            if(equipment.Count>WireProtocol.EquipmentSnapshotMaxItems)
                throw new InvalidOperationException(
                    "Character equipment exceeds bounded V1 baseline capacity");

            cancellationToken.ThrowIfCancellationRequested();
            await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                CharacterChooseWireStatus.Success, snapshot.ActorId, snapshot.MapId,
                snapshot.X, snapshot.Y, snapshot.MapEpoch)), cancellationToken).ConfigureAwait(false);

            if (combat is not null)
            {
                await WriteFrameAsync(tls, WireProtocol.EncodePlayerCombatState(new(
                    snapshot.MapEpoch,
                    combat.Data.CurrentHp, combat.Data.MaxHp,
                    combat.Data.CurrentMp, combat.Data.MaxMp)),
                    cancellationToken).ConfigureAwait(false);
            }

            // Write a final packet even for an empty inventory; client must
            // never confuse an empty but complete bag with incomplete data.
            const int chunkSize=WireProtocol.InventorySnapshotMaxItemsPerFrame;
            for(int start=0; start<inventory.Count; start+=chunkSize)
            {
                var group=inventory.Skip(start).Take(chunkSize)
                    .Select(item=>new InventoryItemWire(item.ItemUid,
                        item.ItemDefinitionId,item.Quantity,item.SourceDropId))
                    .ToArray();
                bool final=start+group.Length==inventory.Count;
                await WriteFrameAsync(tls,WireProtocol.EncodeInventorySnapshot(
                    new(snapshot.ActorId,start,final,group)),
                    cancellationToken).ConfigureAwait(false);
            }
            if(inventory.Count==0)
                await WriteFrameAsync(tls,WireProtocol.EncodeInventorySnapshot(
                    new(snapshot.ActorId,0,true,Array.Empty<InventoryItemWire>())),
                    cancellationToken).ConfigureAwait(false);

            // Preserve the legacy login frame sequence if equipment is entirely disabled.
            // Persisted equipped rows still force a baseline so a character can recover.
            if(_equipmentRules is not null || equipment.Count>0)
            {
                const int equipmentChunk=WireProtocol.EquipmentSnapshotMaxItemsPerFrame;
                for(int start=0;start<equipment.Count;start+=equipmentChunk)
                {
                    var group=equipment.Skip(start).Take(equipmentChunk)
                        .Select(item=>new EquipmentItemWire(
                            item.ItemUid,item.SlotId,item.ItemDefinitionId,item.SourceDropId))
                        .ToArray();
                    bool final=start+group.Length==equipment.Count;
                    await WriteFrameAsync(tls,WireProtocol.EncodeEquipmentSnapshot(
                        new(snapshot.ActorId,start,final,group)),
                        cancellationToken).ConfigureAwait(false);
                }
                if(equipment.Count==0)
                    await WriteFrameAsync(tls,WireProtocol.EncodeEquipmentSnapshot(
                        new(snapshot.ActorId,0,true,Array.Empty<EquipmentItemWire>())),
                        cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var gateway = new GatewayConnection(
                snapshot, combatProfile: trustedCombat);
            completed = true;
            return gateway;
        }
        finally
        {
            if (!completed)
            {
                if (combatAttached && _combat is not null)
                    await _combat.CloseAndDrainAsync(
                        snapshot.ActorId, snapshot.ConnectionEpoch).ConfigureAwait(false);
                await _world.TrustedDisconnectAsync(snapshot.ActorId,
                    snapshot.ConnectionEpoch, snapshot.MapEpoch).ConfigureAwait(false);
            }
        }
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
