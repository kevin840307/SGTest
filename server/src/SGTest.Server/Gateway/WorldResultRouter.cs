using System.Threading.Channels;
using SGTest.Server.World;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Gateway;

public sealed record WorldResultRouterOptions(
    int ChangedBatchCapacity = 128,
    int MaxSessions = 10000,
    int ResyncRequestCapacity = 256,
    int AttackRequestCapacity = 256);

/// <summary>
/// Raw authoritative changed actors, NOT yet filtered by AOI.
/// Each batch retains its server Tick and is delivered exactly once through a
/// bounded queue; a future AOI/WorldReady consumer MUST drain this queue.
/// </summary>
public sealed record WorldChangedBatch(
    long Tick, IReadOnlyList<ActorSnapshot> Changed,
    IReadOnlyList<ActorLifecycleEvent>? Lifecycle = null);

public readonly record struct PlayerRespawnVisualRequest(
    ActorSnapshot Actor,uint RespawnSequence);
public readonly record struct PlayerIdentityUpdate(
    long ActorId,long ConnectionEpoch);

public sealed record SkillPreflightRequest(
    TrustedSkillUse Request,
    TaskCompletionSource<MonsterWorldCoordinator.SkillPrepareResult> Completion);

public sealed record PreparedSkillAction(
    MonsterWorldCoordinator.PreparedSkillUse Prepared,
    SkillUseStatus? CancelStatus,
    bool DeliverMonsterResult,
    TaskCompletionSource<SkillUseOutcome> Completion);

public sealed record WorldResultRouterStatistics(
    long ReceiptsQueued,
    long StaleReceiptsSkipped,
    long ChangedBatchesQueued,
    long BackpressureFailures,
    int ActiveSessions);

public sealed class WorldResultRoutingException : Exception
{
    public WorldResultRoutingException(string message) : base(message) { }
}

/// <summary>
/// Single consumer of ServerWorldHost.Results. Routes reliable MoveReceipts to
/// a trusted GatewayConnection by ActorId/ConnectionEpoch, and preserves
/// authoritative Changed batches for later AOI processing instead of dropping
/// them. All queues are bounded. No database, socket or render work occurs on
/// the World single-writer thread.
/// </summary>
public sealed class WorldResultRouter
{
    private readonly object _gate = new();
    private readonly Dictionary<long, GatewayConnection> _connections = new();
    private readonly Channel<WorldChangedBatch> _changed;
    private readonly Channel<TrustedWorldResyncRequest> _resync;
    private readonly Channel<TrustedNormalAttack> _attacks;
    private readonly Channel<TrustedSkillUse> _skills;
    private readonly Channel<SkillPreflightRequest> _skillPreflights;
    private readonly Channel<PreparedSkillAction> _preparedSkills;
    private readonly Channel<long> _claimedGroundDrops;
    private readonly Channel<PlayerDamageSettlement> _playerDamageVisuals;
    private readonly Channel<PlayerRespawnVisualRequest> _playerRespawnVisuals;
    private readonly Channel<PlayerIdentityUpdate> _playerIdentityUpdates;
    private readonly Channel<PlayerDamageSettlement> _playerPvpCombatVisuals;
    private readonly Channel<PlayerDamageSettlement> _playerPvpSkillVisuals;
    private readonly WorldResultRouterOptions _options;
    private readonly Action<WorldChangedBatch>? _changedObserver;
    private long _queued, _stale, _changedQueued, _backpressure;
    private int _running;

    public WorldResultRouter(WorldResultRouterOptions? options = null,
                             Action<WorldChangedBatch>? changedObserver = null)
    {
        _options = options ?? new WorldResultRouterOptions();
        _changedObserver = changedObserver;
        if (_options.ChangedBatchCapacity is < 1 or > 65536 ||
            _options.MaxSessions is < 1 or > 100000 ||
            _options.ResyncRequestCapacity is < 1 or > 65536 ||
            _options.AttackRequestCapacity is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(options));
        _changed = Channel.CreateBounded<WorldChangedBatch>(
            new BoundedChannelOptions(_options.ChangedBatchCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _resync = Channel.CreateBounded<TrustedWorldResyncRequest>(
            new BoundedChannelOptions(_options.ResyncRequestCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _attacks = Channel.CreateBounded<TrustedNormalAttack>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _skills=Channel.CreateBounded<TrustedSkillUse>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader=true,
                SingleWriter=false,
                FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
            });
        _skillPreflights=Channel.CreateBounded<SkillPreflightRequest>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader=true,
                SingleWriter=false,
                FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
            });
        _preparedSkills=Channel.CreateBounded<PreparedSkillAction>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader=true,
                SingleWriter=false,
                FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
            });
        _claimedGroundDrops = Channel.CreateBounded<long>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _playerDamageVisuals = Channel.CreateBounded<PlayerDamageSettlement>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _playerRespawnVisuals = Channel.CreateBounded<PlayerRespawnVisualRequest>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _playerIdentityUpdates = Channel.CreateBounded<PlayerIdentityUpdate>(
            new BoundedChannelOptions(_options.MaxSessions)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _playerPvpCombatVisuals = Channel.CreateBounded<PlayerDamageSettlement>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader=true,
                SingleWriter=false,
                FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
            });
        _playerPvpSkillVisuals = Channel.CreateBounded<PlayerDamageSettlement>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader=true,
                SingleWriter=false,
                FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
            });
    }

    public ChannelReader<WorldChangedBatch> ChangedBatches => _changed.Reader;
    public ChannelReader<TrustedWorldResyncRequest> ResyncRequests => _resync.Reader;
    public ChannelReader<TrustedNormalAttack> AttackRequests => _attacks.Reader;
    public ChannelReader<TrustedSkillUse> SkillRequests => _skills.Reader;
    public ChannelReader<SkillPreflightRequest> SkillPreflightRequests =>
        _skillPreflights.Reader;
    public ChannelReader<PreparedSkillAction> PreparedSkillActions =>
        _preparedSkills.Reader;
    public ChannelReader<long> ClaimedGroundDrops => _claimedGroundDrops.Reader;
    public ChannelReader<PlayerDamageSettlement> PlayerDamageVisuals =>
        _playerDamageVisuals.Reader;
    public ChannelReader<PlayerRespawnVisualRequest> PlayerRespawnVisuals =>
        _playerRespawnVisuals.Reader;
    public ChannelReader<PlayerIdentityUpdate> PlayerIdentityUpdates =>
        _playerIdentityUpdates.Reader;
    public ChannelReader<PlayerDamageSettlement> PlayerPvpCombatVisuals =>
        _playerPvpCombatVisuals.Reader;
    public ChannelReader<PlayerDamageSettlement> PlayerPvpSkillVisuals =>
        _playerPvpSkillVisuals.Reader;

    public WorldResultRouterStatistics Statistics
    {
        get
        {
            lock (_gate)
                return new(Interlocked.Read(ref _queued), Interlocked.Read(ref _stale),
                    Interlocked.Read(ref _changedQueued), Interlocked.Read(ref _backpressure),
                    _connections.Count);
        }
    }

    /// <summary>
    /// Called ONLY after TLS Auth + CharacterChoose returned a trusted World
    /// registration. Reconnect may replace an older ConnectionEpoch, but an
    /// older/duplicate session cannot replace a newer binding.
    /// </summary>
    public bool TryAttach(GatewayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_gate)
        {
            if (connection.IsClosed) return false;
            var next = connection.Binding;
            if (_connections.TryGetValue(next.ActorId, out var existing))
            {
                if (ReferenceEquals(existing, connection)) return true;
                if (existing.Binding.ConnectionEpoch >= next.ConnectionEpoch) return false;
                existing.Close(); // stale socket cannot receive later authoritative receipts
            }
            else if (_connections.Count >= _options.MaxSessions)
                return false;
            _connections[next.ActorId] = connection;
            if(connection.TryGetPlayerIdentity(out _) &&
               !_playerIdentityUpdates.Writer.TryWrite(
                   new(next.ActorId,next.ConnectionEpoch)))
            {
                _connections.Remove(next.ActorId);
                connection.Close();
                return false;
            }
            return true;
        }
    }

    /// <summary>Stale disconnects cannot detach a newer authenticated session.</summary>
    public bool TryDetach(GatewayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_gate)
        {
            var bound = connection.Binding;
            if (!_connections.TryGetValue(bound.ActorId, out var existing) ||
                !ReferenceEquals(existing, connection))
                return false;
            _connections.Remove(bound.ActorId);
            connection.Close();
            return true;
        }
    }

    /// <summary>
    /// Only a trusted transfer can advance the current Gateway's MapEpoch.
    /// Future-map receipts without that binding update fail closed.
    /// </summary>
    public bool TryAdvanceTrustedMap(GatewayConnection connection, ActorSnapshot actor)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_gate)
        {
            if (!_connections.TryGetValue(actor.ActorId, out var existing) ||
                !ReferenceEquals(existing, connection))
                return false;
            return connection.TryUpdateTrustedBinding(actor);
        }
    }

    /// <summary>
    /// Server-authorized map transfer for an ALREADY authenticated actor.
    /// The caller must validate the relevant NPC/portal and its destination:
    /// this is not an exposed client RPC or generic teleport permission.
    /// Host World generation changes before the trusted Gateway changes.
    /// If the TLS session disconnects during that hand-off, release the new
    /// World generation so a stale old-epoch disconnect cannot leave a ghost.
    /// No socket, database, or AOI work occurs on the World Tick owner thread.
    /// </summary>
    public async Task<ActorSnapshot?> TrustedTransferAsync(
        ServerWorldHost world, long actorId, int destinationMapId,
        double destinationX, double destinationY)
    {
        ArgumentNullException.ThrowIfNull(world);
        GatewayConnection gateway;
        ConnectionBinding before;
        lock (_gate)
        {
            if (!_connections.TryGetValue(actorId, out gateway!) ||
                gateway.IsClosed)
                return null;
            before = gateway.Binding;
        }

        ActorSnapshot? result = await world.TrustedTransferAsync(
            before.ActorId, before.ConnectionEpoch, before.MapEpoch,
            destinationMapId, destinationX, destinationY).ConfigureAwait(false);
        if (result is not { } after)
            return null; // no map/position mutation occurred

        // A closed/changed session cannot be advanced. Clean the newly
        // transferred generation even if its old socket has already exited.
        if (TryAdvanceTrustedMap(gateway, after))
            return after;

        TryDetach(gateway);
        await world.TrustedDisconnectAsync(
            after.ActorId, after.ConnectionEpoch, after.MapEpoch).ConfigureAwait(false);
        throw new InvalidOperationException(
            "Map transfer succeeded in World but trusted Gateway binding was lost; actor released");
    }

    /// <summary>
    /// Trusted RPC boundary used by the authenticated TLS NPC action handler.
    /// The connection supplies only an NPC SpawnId and request sequence; the
    /// World owner validates current map, distance and audited portal content.
    /// On success, advance the same Gateway MapEpoch, or release the World
    /// generation if the TLS socket disappeared during the async hand-off.
    /// </summary>
    public async Task<NpcPortalOutcome> TrustedNpcInteractAsync(
        ServerWorldHost world, PortalCatalog portals, ConnectionBinding requester,
        uint sequence, int npcSpawnId)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(portals);
        GatewayConnection gateway;
        lock (_gate)
        {
            if (!_connections.TryGetValue(requester.ActorId, out gateway!) ||
                gateway.IsClosed || gateway.Binding != requester)
                return new(NpcPortalStatus.StaleGeneration, null);
        }

        var outcome = await world.TrustedNpcInteractAsync(
            requester.ActorId, requester.ConnectionEpoch, requester.MapEpoch,
            sequence, npcSpawnId, portals).ConfigureAwait(false);

        if (outcome.TransferredActor is not { } after)
            return outcome;

        if (outcome.Status != NpcPortalStatus.Accepted ||
            !TryAdvanceTrustedMap(gateway, after))
        {
            TryDetach(gateway);
            await world.TrustedDisconnectAsync(
                after.ActorId, after.ConnectionEpoch, after.MapEpoch).ConfigureAwait(false);
            throw new InvalidOperationException(
                "NPC portal advanced World but gateway session disappeared; trusted actor released");
        }
        return outcome;
    }

    /// <summary>
    /// Short immutable snapshot for the non-World AOI pump; it never exposes
    /// Client-supplied identities. A late disconnect may invalidate a binding
    /// before QueueAoi; that case is rejected without sending stale frames.
    /// </summary>
    public bool IsCurrentBinding(ConnectionBinding binding)
    {
        lock (_gate)
            return _connections.TryGetValue(binding.ActorId, out var connection) &&
                !connection.IsClosed && connection.Binding == binding;
    }

    public bool IsVisibleActor(ConnectionBinding viewer,long targetActorId)
    {
        if(targetActorId<=0||targetActorId==viewer.ActorId)return false;
        lock(_gate)
            return _connections.TryGetValue(viewer.ActorId,out var connection)&&
                !connection.IsClosed&&connection.Binding==viewer&&
                connection.IsVisibleActor(targetActorId);
    }

    public bool TryGetPlayerIdentity(
        long actorId,long connectionEpoch,out PlayerIdentityPresentation identity)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(actorId,out var connection)||
               connection.IsClosed||
               connection.Binding.ConnectionEpoch!=connectionEpoch)
            {
                identity=default;
                return false;
            }
            return connection.TryGetPlayerIdentity(out identity);
        }
    }

    public GatewaySendStatus PublishPlayerPvpState(
        long actorId,long connectionEpoch,CharacterPvpState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock(_gate)
        {
            if(!_connections.TryGetValue(actorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            if(connection.Binding.ConnectionEpoch!=connectionEpoch||
               !connection.TryUpdatePlayerPvpState(state))
                return GatewaySendStatus.GenerationMismatch;
            return _playerIdentityUpdates.Writer.TryWrite(
                new(actorId,connectionEpoch))
                ? GatewaySendStatus.Enqueued
                : GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePlayerRespawnResult(
        TrustedPlayerRespawn request, PlayerRespawnOutcome outcome)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(request.ActorId, out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerRespawnResult(request, outcome);
        }
    }

    public ConnectionBinding[] SnapshotActiveBindings()
    {
        lock (_gate)
            return _connections.Values.Where(c => !c.IsClosed)
                .Select(c => c.Binding).ToArray();
    }

    public GatewaySendStatus QueueAoi(ConnectionBinding viewer, AoiEvent evt)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(viewer.ActorId, out var connection) ||
                connection.Binding != viewer)
                return GatewaySendStatus.GenerationMismatch;
            return connection.QueueAoi(evt);
        }
    }

    public bool TryRequestNormalAttack(TrustedNormalAttack request)
    {
        if (request.ActorId <= 0 || request.ConnectionEpoch <= 0 ||
            request.RequestMapEpoch <= 0 || request.AttackSequence == 0 ||
            request.TargetEntityId <= 0)
            return false;
        lock (_gate)
        {
            if (!_connections.TryGetValue(request.ActorId, out var connection) ||
                connection.IsClosed ||
                connection.Binding.ActorId != request.ActorId ||
                connection.Binding.ConnectionEpoch != request.ConnectionEpoch)
                return false;
        }
        return _attacks.Writer.TryWrite(request);
    }

    public bool TryRequestSkill(TrustedSkillUse request)
    {
        if(request.ActorId<=0 || request.ConnectionEpoch<=0 ||
            request.RequestMapEpoch<=0 || request.SkillSequence==0 ||
            request.SkillId<=0 || request.TargetEntityId<0)
            return false;
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed ||
                connection.Binding.ActorId!=request.ActorId ||
                connection.Binding.ConnectionEpoch!=request.ConnectionEpoch)
                return false;
        }
        return _skills.Writer.TryWrite(request);
    }

    public async Task<MonsterWorldCoordinator.SkillPrepareResult> PrepareSkillAsync(
        TrustedSkillUse request,CancellationToken cancellationToken=default)
    {
        if(request.ActorId<=0 || request.ConnectionEpoch<=0 ||
            request.RequestMapEpoch<=0 || request.SkillSequence==0 ||
            request.SkillId<=0 || request.TargetEntityId<0)
            throw new ArgumentOutOfRangeException(nameof(request));

        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed ||
                connection.Binding!=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch))
                return new(null,new SkillUseOutcome(
                    SkillUseStatus.OldMapEpoch,0,request.SkillId,
                    request.TargetEntityId,0,0,0,0));
        }

        var completion=new TaskCompletionSource<MonsterWorldCoordinator.SkillPrepareResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await _skillPreflights.Writer.WriteAsync(
            new(request,completion),cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SkillUseOutcome> FinishPreparedSkillAsync(
        MonsterWorldCoordinator.PreparedSkillUse prepared,
        SkillUseStatus? cancelStatus=null,
        CancellationToken cancellationToken=default)
    {
        if(cancelStatus is SkillUseStatus.Applied or SkillUseStatus.Miss)
            throw new ArgumentOutOfRangeException(nameof(cancelStatus));
        var completion=new TaskCompletionSource<SkillUseOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await _preparedSkills.Writer.WriteAsync(
            new(prepared,cancelStatus,true,completion),cancellationToken)
            .ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SkillUseOutcome> FinishPreparedPlayerSkillAsync(
        MonsterWorldCoordinator.PreparedSkillUse prepared,
        SkillUseStatus? cancelStatus=null,
        CancellationToken cancellationToken=default)
    {
        if(cancelStatus is SkillUseStatus.Applied or SkillUseStatus.Miss)
            throw new ArgumentOutOfRangeException(nameof(cancelStatus));
        var completion=new TaskCompletionSource<SkillUseOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await _preparedSkills.Writer.WriteAsync(
            new(prepared,cancelStatus,false,completion),cancellationToken)
            .ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryPostPlayerDamageVisual(PlayerDamageSettlement result)
    {
        if(result.ActorId<=0 || result.ConnectionEpoch<=0 || result.MapEpoch<=0 ||
            result.DamageSequence==0 || result.Damage<=0)
            return false;
        return _playerDamageVisuals.Writer.TryWrite(result);
    }

    public bool TryPostPlayerPvpCombatVisual(PlayerDamageSettlement result)
    {
        if(result.SourceKind!=PlayerDamageSourceKind.PlayerPvp||
           result.ActorId<=0||result.ConnectionEpoch<=0||result.MapEpoch<=0||
           result.DamageSequence==0||result.AttackerActorId<=0||
           result.AttackerConnectionEpoch<=0||result.AttackerAttackSequence==0||
           result.Damage<=0||result.AttackerFacing is <1 or >8||
           result.VictimFacing is <1 or >8)
            return false;
        return _playerPvpCombatVisuals.Writer.TryWrite(result);
    }

    public bool TryPostPlayerPvpSkillVisual(PlayerDamageSettlement result)
    {
        if(result.SourceKind!=PlayerDamageSourceKind.PlayerSkill||
           result.ActorId<=0||result.ConnectionEpoch<=0||result.MapEpoch<=0||
           result.DamageSequence==0||result.AttackerActorId<=0||
           result.AttackerConnectionEpoch<=0||result.AttackerSkillSequence==0||
           result.AttackerSkillId is <1 or >100_000_000||
           result.Damage<=0||result.AttackerFacing is <1 or >8||
           result.VictimFacing is <1 or >8)
            return false;
        return _playerPvpSkillVisuals.Writer.TryWrite(result);
    }

    public bool TryPostPlayerRespawnVisual(
        ActorSnapshot actor,uint respawnSequence)
    {
        if(actor.ActorId<=0 || actor.ConnectionEpoch<=0 || actor.MapEpoch<=0 ||
            respawnSequence==0)
            return false;
        return _playerRespawnVisuals.Writer.TryWrite(
            new(actor,respawnSequence));
    }

    public GatewaySendStatus QueuePlayerDamage(PlayerDamageSettlement result)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(result.ActorId, out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerDamage(result);
        }
    }

    public bool TryPostGroundDropClaimed(long dropId) =>
        dropId>0 && _claimedGroundDrops.Writer.TryWrite(dropId);


    public GatewaySendStatus QueuePvpAttackResult(
        TrustedPvpAttack request,PvpAttackResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePvpAttackResult(request,result);
        }
    }

    public GatewaySendStatus QueuePvpSkillResult(
        TrustedPvpSkill request,PvpSkillResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePvpSkillResult(request,result);
        }
    }

    public GatewaySendStatus QueueGmPvpNameResult(
        TrustedGmPvpNameCommand request,GmPvpNameResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueGmPvpNameResult(request,result);
        }
    }

    public GatewaySendStatus QueuePlayerShopResult(
        TrustedPlayerShopCommand request,PlayerShopResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerShopResult(request,result);
        }
    }

    public GatewaySendStatus QueueShopSnapshot(
        TrustedShopCommand request,ShopOpenStatus status,long money,
        IReadOnlyList<NpcShopCatalog.Item> items)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueShopSnapshot(request,status,money,items);
        }
    }

    public GatewaySendStatus QueueShopTransactionResult(
        TrustedShopCommand request,ShopTransactionResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueShopTransactionResult(request,result);
        }
    }

    public GatewaySendStatus QueueStorageSnapshot(
        TrustedStorageCommand request,StorageOpenStatus status,
        IReadOnlyList<StoredItem> items)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueStorageSnapshot(request,status,items);
        }
    }

    public GatewaySendStatus QueueStorageTransferResult(
        TrustedStorageCommand request,StorageTransferResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueStorageTransferResult(request,result);
        }
    }

    public GatewaySendStatus QueueEquipmentTransferResult(
        TrustedEquipmentCommand request,EquipmentTransferResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueEquipmentTransferResult(request,result);
        }
    }

    public GatewaySendStatus QueueBankSnapshot(
        TrustedBankCommand request,BankOpenStatus status,BankBalanceSnapshot? snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueBankSnapshot(request,status,snapshot);
        }
    }

    public GatewaySendStatus QueueBankTransactionResult(
        TrustedBankCommand request,BankTransactionResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueBankTransactionResult(request,result);
        }
    }

    public GatewaySendStatus QueuePvpModeResult(
        TrustedPvpModeCommand request,PvpModeResultStatus status,
        PvpMode mode,bool mapAllowsPlayerPvp)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePvpModeResult(
                request,status,mode,mapAllowsPlayerPvp);
        }
    }

    public GatewaySendStatus QueueQuestView(
        TrustedQuestCommand request,QuestViewWireStatus status,QuestView? view)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueQuestView(request,status,view);
        }
    }

    public GatewaySendStatus QueueRepairSnapshot(
        TrustedRepairCommand request,RepairOpenStatus status,RepairPreview? preview)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueRepairSnapshot(request,status,preview);
        }
    }

    public GatewaySendStatus QueueRepairResult(
        TrustedRepairCommand request,RepairResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueRepairResult(request,result);
        }
    }

    public GatewaySendStatus QueueTradeSnapshot(
        ConnectionBinding expected,TradeSnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(expected.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueTradeSnapshot(expected,snapshot);
        }
    }

    public GatewaySendStatus QueueTradeTerminal(
        ConnectionBinding expected,TradeTerminalWire terminal)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(expected.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueTradeTerminal(expected,terminal);
        }
    }

    public GatewaySendStatus QueueRecoveryUseResult(
        TrustedRecoveryUse request,RecoveryUseResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueRecoveryUseResult(request,result);
        }
    }

    public GatewaySendStatus QueueInventoryDiscardResult(
        TrustedInventoryDiscard request,InventoryDiscardResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueInventoryDiscardResult(request,result);
        }
    }

    public GatewaySendStatus QueueGroundPickupResult(
        TrustedGroundPickup request,PickupResult result)
    {
        lock(_gate)
        {
            if (!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueGroundPickupResult(request,result);
        }
    }

    public GatewaySendStatus QueuePlayerCombatState(
        ConnectionBinding generation,int currentHp,int maxHp,
        int currentMp,int maxMp)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(generation.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerCombatState(
                generation,currentHp,maxHp,currentMp,maxMp);
        }
    }

    public GatewaySendStatus QueueSkillUseResult(
        TrustedSkillUse request,SkillUseOutcome outcome)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueSkillUseResult(request,outcome);
        }
    }

    public GatewaySendStatus QueueNormalAttackResult(
        TrustedNormalAttack request, NormalAttackOutcome outcome)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(request.ActorId, out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueNormalAttackResult(request, outcome);
        }
    }

    public bool TryRequestResync(TrustedWorldResyncRequest request)
    {
        if (request.ActorId <= 0 || request.ConnectionEpoch <= 0 || request.MapEpoch <= 0)
            return false;
        lock (_gate)
        {
            if (!_connections.TryGetValue(request.ActorId, out var connection) ||
                connection.IsClosed ||
                connection.Binding != new ConnectionBinding(
                    request.ActorId, request.ConnectionEpoch, request.MapEpoch))
                return false;
        }
        return _resync.Writer.TryWrite(request);
    }

    /// <summary>
    /// Synchronous bounded fanout, called by exactly one result-consumer.
    /// Every authoritative World Tick is retained first: overflow fails before any receipt
    /// in this Tick is enqueued. Receipt queue overflow also fails closed.
    /// A failure is terminal for the caller's result pump; never retry a
    /// partially routed Tick (which could duplicate reliable events).
    /// </summary>
    public void Route(WorldTickResult tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (tick.Tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));

        // Preserve EVERY authoritative World Tick on the single AOI/AI timeline.
        // An empty player Changed/Lifecycle batch is still the clock source for
        // map-local monster AI. It produces no player AOI traffic by itself.
        // This avoids introducing a second independent AI timer/scheduler.
        var batch = new WorldChangedBatch(tick.Tick, tick.Changed, tick.Lifecycle);
        if (!_changed.Writer.TryWrite(batch))
            FailBackpressure("Authoritative World Tick output queue full; AOI/AI consumer is too slow");
        Interlocked.Increment(ref _changedQueued);
        // Observer contract is memory-only/nonblocking. Position checkpointing
        // ignores empty actor batches; no SQLite work occurs in this call.
        _changedObserver?.Invoke(batch);

        lock (_gate)
        {
            foreach (var receipt in tick.Receipts)
            {
                if (receipt.ActorId <= 0 || receipt.ConnectionEpoch <= 0 ||
                    receipt.MapEpoch <= 0 || receipt.MovementSequence == 0)
                    throw new WorldResultRoutingException("Invalid authoritative receipt identity");

                if (!_connections.TryGetValue(receipt.ActorId, out var connection) ||
                    connection.IsClosed)
                {
                    Interlocked.Increment(ref _stale);
                    continue; // no reachable socket after disconnect
                }
                var bound = connection.Binding;
                if (receipt.ConnectionEpoch < bound.ConnectionEpoch)
                {
                    Interlocked.Increment(ref _stale);
                    continue; // old in-flight World result after reconnect
                }
                if (receipt.ConnectionEpoch > bound.ConnectionEpoch ||
                    receipt.MapEpoch > bound.MapEpoch)
                    throw new WorldResultRoutingException(
                        "World receipt belongs to an unbound future connection/map generation");

                var status = connection.QueueReceipt(receipt);
                switch (status)
                {
                    case GatewaySendStatus.Enqueued:
                        Interlocked.Increment(ref _queued);
                        break;
                    case GatewaySendStatus.Closed:
                        Interlocked.Increment(ref _stale);
                        break;
                    case GatewaySendStatus.Backpressured:
                        FailBackpressure("Reliable receipt queue full; cannot drop or skip an ACK");
                        break;
                    default:
                        throw new WorldResultRoutingException(
                            "Trusted routing and Gateway generation disagree");
                }
            }
        }
    }

    /// <summary>Consumes every host Tick exactly once, independently of render cadence.</summary>
    public async Task RunAsync(ChannelReader<WorldTickResult> source,
                               CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Interlocked.Exchange(ref _running, 1) != 0)
            throw new InvalidOperationException("World results must have exactly one router consumer");

        Exception? fault = null;
        try
        {
            await foreach (var tick in source.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                Route(tick);
        }
        catch (Exception error)
        {
            fault = error;
            throw;
        }
        finally
        {
            _changed.Writer.TryComplete(fault);
            _resync.Writer.TryComplete(fault);
            _attacks.Writer.TryComplete(fault);
            _skills.Writer.TryComplete(fault);
            _skillPreflights.Writer.TryComplete(fault);
            _preparedSkills.Writer.TryComplete(fault);
            _claimedGroundDrops.Writer.TryComplete(fault);
            _playerDamageVisuals.Writer.TryComplete(fault);
            _playerRespawnVisuals.Writer.TryComplete(fault);
        }
    }

    private void FailBackpressure(string message)
    {
        Interlocked.Increment(ref _backpressure);
        throw new WorldResultRoutingException(message);
    }
}
