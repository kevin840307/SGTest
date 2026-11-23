using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Pet;
using SGTest.Server.Mecha;
using SGTest.Server.Combat;
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
public sealed partial class WorldResultRouter
{
    private readonly object _gate = new();
    private readonly Dictionary<long, GatewayConnection> _connections = new();
    private readonly Channel<WorldChangedBatch> _changed;
    private readonly Channel<TrustedWorldResyncRequest> _resync;
    private readonly Channel<TrustedNormalAttack> _attacks;
    private readonly Channel<TrustedPetAttack> _petAttacks;
    private readonly Channel<TrustedSkillUse> _skills;
    private readonly Channel<SkillPreflightRequest> _skillPreflights;
    private readonly Channel<PreparedSkillAction> _preparedSkills;
    private readonly Channel<long> _claimedGroundDrops;
    private readonly Channel<PlayerDamageSettlement> _playerDamageVisuals;
    private readonly Channel<PlayerRespawnVisualRequest> _playerRespawnVisuals;
    private readonly Channel<PlayerIdentityUpdate> _playerIdentityUpdates;
    private readonly Channel<PlayerDamageSettlement> _playerPvpCombatVisuals;
    private readonly Channel<PlayerDamageSettlement> _playerPvpSkillVisuals;
    private readonly Channel<PetWorldStateUpdate> _petWorldUpdates;
    private readonly Channel<MechaWorldStateUpdate> _mechaWorldUpdates;
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
        _petAttacks=Channel.CreateBounded<TrustedPetAttack>(
            new BoundedChannelOptions(_options.AttackRequestCapacity)
            {
                SingleReader=true,
                SingleWriter=false,
                FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
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
        _petWorldUpdates = Channel.CreateBounded<PetWorldStateUpdate>(
            new BoundedChannelOptions(_options.MaxSessions)
            {
                SingleReader=true,
                SingleWriter=false,
                FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
            });
        _mechaWorldUpdates = Channel.CreateBounded<MechaWorldStateUpdate>(
            new BoundedChannelOptions(_options.MaxSessions)
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
    public ChannelReader<TrustedPetAttack> PetAttackRequests => _petAttacks.Reader;
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
    public ChannelReader<PetWorldStateUpdate> PetWorldUpdates =>
        _petWorldUpdates.Reader;
    public ChannelReader<MechaWorldStateUpdate> MechaWorldUpdates =>
        _mechaWorldUpdates.Reader;

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
    /// Called ONLY after authenticated transport Auth + CharacterChoose returned a trusted World
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
    /// If the authenticated transport session disconnects during that hand-off, release the new
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
    /// Trusted RPC boundary used by the authenticated authenticated transport NPC action handler.
    /// The connection supplies only an NPC SpawnId and request sequence; the
    /// World owner validates current map, distance and audited portal content.
    /// On success, advance the same Gateway MapEpoch, or release the World
    /// generation if the authenticated transport socket disappeared during the async hand-off.
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

    public bool IsVisibleActor(ConnectionBinding viewer,ConnectionBinding target)
    {
        if(target.ActorId<=0||target.ConnectionEpoch<=0||target.MapEpoch<=0||
           target.ActorId==viewer.ActorId)
            return false;
        lock(_gate)
            return _connections.TryGetValue(viewer.ActorId,out var connection)&&
                !connection.IsClosed&&connection.Binding==viewer&&
                _connections.TryGetValue(target.ActorId,out var targetConnection)&&
                !targetConnection.IsClosed&&targetConnection.Binding==target&&
                connection.IsVisibleActor(target);
    }

    public bool TryGetCurrentBinding(long actorId,out ConnectionBinding binding)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(actorId,out var connection)||
               connection.IsClosed)
            {
                binding=default;
                return false;
            }
            binding=connection.Binding;
            return true;
        }
    }

    public bool CloseCurrentBinding(ConnectionBinding expected)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(expected.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=expected)
                return false;
            connection.Close();
            return true;
        }
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

    public bool TryResolveOnlinePlayerByName(
        string? displayName,out ConnectionBinding binding,
        out PlayerIdentityPresentation identity)
    {
        binding=default;
        identity=default;
        if(string.IsNullOrWhiteSpace(displayName))return false;
        string normalized=displayName.Normalize(
            System.Text.NormalizationForm.FormC).Trim();
        if(normalized.Length is <1 or >24||normalized.Any(char.IsControl))
            return false;
        lock(_gate)
        {
            bool found=false;
            foreach(var connection in _connections.Values)
            {
                if(connection.IsClosed||
                   !connection.TryGetPlayerIdentity(out var candidate)||
                   !string.Equals(candidate.DisplayName,normalized,
                       StringComparison.Ordinal))
                    continue;
                if(found)
                {
                    binding=default;
                    identity=default;
                    return false;
                }
                binding=connection.Binding;
                identity=candidate;
                found=true;
            }
            return found;
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

    public ConnectionBinding[] SnapshotActiveBindings()
    {
        lock (_gate)
            return _connections.Values.Where(c => !c.IsClosed)
                .Select(c => c.Binding).ToArray();
    }


}
