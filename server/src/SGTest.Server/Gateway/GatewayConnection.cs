using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Combat;
using System.Text;
using SGTest.Server.Wire;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Gateway;

/// <summary>
/// Transport-independent state for one authenticated connection.
/// Authentication/authenticated transport/socket lifetime live above this class; raw client bytes can
/// never choose ActorId or ConnectionEpoch. Reconnect creates a NEW instance.
/// </summary>
public sealed partial class GatewayConnection
{
    private sealed class ReliableFrameQueue
    {
        private readonly object _gate = new();
        private readonly Queue<byte[]> _frames = new();
        private readonly int _maxFrames;
        private readonly int _maxBytes;
        private readonly SemaphoreSlim _available = new(0);
        private int _bytes;

        public ReliableFrameQueue(int maxFrames, int maxBytes)
        {
            _maxFrames = maxFrames;
            _maxBytes = maxBytes;
        }

        public int Count { get { lock (_gate) return _frames.Count; } }
        public int Bytes { get { lock (_gate) return _bytes; } }

        public bool TryEnqueue(byte[] frame)
        {
            lock (_gate)
            {
                if (_frames.Count >= _maxFrames || frame.Length > _maxBytes - _bytes)
                    return false;
                _frames.Enqueue(frame);
                _bytes += frame.Length;
            }
            _available.Release();
            return true;
        }

        public bool TryDequeue(out byte[] frame)
        {
            if (!_available.Wait(0))
            {
                frame = Array.Empty<byte>();
                return false;
            }
            lock (_gate)
            {
                if (!_frames.TryDequeue(out frame!))
                    throw new InvalidOperationException("Reliable queue signal/state diverged");
                _bytes -= frame.Length;
                return true;
            }
        }

        public async ValueTask<byte[]?> DequeueAsync(CancellationToken cancellationToken)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_frames.TryDequeue(out var frame))
                    return null; // Close may clear after the signal was acquired.
                _bytes -= frame.Length;
                return frame;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _frames.Clear();
                _bytes = 0;
                while (_available.Wait(0)) { }
            }
        }
    }

    private readonly object _bindingGate = new();
    private readonly object _receiveGate = new();
    private readonly WireStreamParser _parser;
    private readonly ReliableFrameQueue _reliable;
    private ConnectionBinding _binding;
    private bool _closed;
    private long _worldReadySentMapEpoch;
    private long _worldReadySentTick = -1;
    private long _worldReadyAckedMapEpoch;
    private long _worldReadyAckedTick = -1;
    private bool _resyncPending;
    private readonly TrustedCombatProfile? _combatProfile;
    private uint _lastChatSequence;
    private uint _lastPartySequence;
    private uint _lastClanSequence;
    private uint _lastFriendSequence;
    private uint _lastHotkeySequence;
    private uint _lastPetSequence;
    private uint _lastMechaSequence;
    private uint _lastPetAttackSequence;
    private uint _lastPetSkillSequence;
    private uint _lastAttributeSequence;
    private readonly string? _displayName;
    private readonly CharacterAppearance? _appearance;
    private CharacterPvpState? _pvpState;
    private readonly string? _gmOperatorId;
    // Exact AOI identity. MapEpoch is scoped to the target actor's own
    // trusted map lifecycle and may differ numerically from the viewer's.
    private readonly Dictionary<long,ConnectionBinding> _visibleActors = new();

    public GatewayConnection(ActorSnapshot trustedActor,
                             GatewayConnectionOptions? options = null,
                             TrustedCombatProfile? combatProfile = null,
                             string? displayName = null,
                             CharacterPvpState? pvpState = null,
                             string? gmOperatorId = null,
                             CharacterAppearance? appearance = null)
    {
        var configured = options ?? new GatewayConnectionOptions();
        if (configured.MaxBufferedInputBytes < WireProtocol.MaximumFrameBytes ||
            configured.MaxBufferedInputBytes > WireProtocol.MaximumFrameBytes * 4 ||
            configured.MaxReliableFrames is < 1 or > 4096 ||
            configured.MaxReliableBytes < WireProtocol.MaximumFrameBytes ||
            configured.MaxReliableBytes > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid bounded connection limits");

        _binding = ConnectionBinding.FromTrustedSnapshot(trustedActor);
        if (combatProfile is { } profile &&
            (profile.Attack < 0 || profile.Defense < 0 || profile.Hit < 0 ||
             profile.Dodge < 0 || !double.IsFinite(profile.AttackRange) ||
             profile.AttackRange <= 0 || profile.AttackRange > 500 ||
             profile.AttackCooldownTicks is < 1 or > 3600))
            throw new ArgumentOutOfRangeException(nameof(combatProfile));
        _combatProfile = combatProfile;
        if((displayName is null)!=(pvpState is null)||
           (pvpState is not null&&pvpState.CharacterId!=trustedActor.ActorId))
            throw new ArgumentException(
                "Display name and PvP state must match the trusted actor");
        if(displayName is not null)
        {
            int nameBytes=Encoding.UTF8.GetByteCount(displayName);
            if(string.IsNullOrWhiteSpace(displayName)||
               nameBytes is <1 or >WireProtocol.PlayerIdentityMaxNameBytes)
                throw new ArgumentOutOfRangeException(nameof(displayName));
        }
        _displayName=displayName;
        _appearance=appearance;
        _pvpState=pvpState;
        if(gmOperatorId is not null&&
           (string.IsNullOrWhiteSpace(gmOperatorId)||gmOperatorId.Length>64||
            gmOperatorId.Any(char.IsControl)))
            throw new ArgumentOutOfRangeException(nameof(gmOperatorId));
        _gmOperatorId=gmOperatorId;
        _parser = new WireStreamParser(configured.MaxBufferedInputBytes);
        _reliable = new ReliableFrameQueue(configured.MaxReliableFrames, configured.MaxReliableBytes);
    }

    public ConnectionBinding Binding
    {
        get { lock (_bindingGate) return _binding; }
    }

    public bool TryGetPlayerIdentity(out PlayerIdentityPresentation identity)
    {
        lock(_bindingGate)
        {
            if(_closed||_displayName is null||_pvpState is null)
            {
                identity=default;
                return false;
            }
            identity=new(
                _binding.ActorId,_binding.ConnectionEpoch,_binding.MapEpoch,
                _displayName,_pvpState.Mode,_pvpState.RedMan,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                _pvpState.PinkUntilUnixMilliseconds,
                _pvpState.UpdatedUnixMilliseconds,_appearance);
            return true;
        }
    }

    public bool TryUpdatePlayerPvpState(CharacterPvpState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock(_bindingGate)
        {
            if(_closed||_displayName is null||
               state.CharacterId!=_binding.ActorId)
                return false;
            _pvpState=state;
            return true;
        }
    }

    public bool IsClosed
    {
        get { lock (_bindingGate) return _closed; }
    }

    public int BufferedInputBytes => _parser.BufferedBytes;
    public int ReliableFrames => _reliable.Count;
    public int ReliableBytes => _reliable.Bytes;
    public bool GmAuthorized => _gmOperatorId is not null;
    public bool IsVisibleActor(long actorId)
    {
        lock(_bindingGate)
            return !_closed&&actorId>0&&actorId!=_binding.ActorId&&
                _visibleActors.ContainsKey(actorId);
    }

    public bool IsVisibleActor(ConnectionBinding actor)
    {
        lock(_bindingGate)
            return !_closed&&actor.ActorId>0&&actor.ActorId!=_binding.ActorId&&
                _visibleActors.TryGetValue(actor.ActorId,out var visible)&&
                visible==actor;
    }
    public bool WorldReadyAcknowledged
    {
        get
        {
            lock (_bindingGate)
                return !_resyncPending &&
                    _worldReadySentMapEpoch == _binding.MapEpoch &&
                    _worldReadyAckedMapEpoch == _binding.MapEpoch &&
                    _worldReadyAckedTick == _worldReadySentTick;
        }
    }

    /// <summary>
    /// Same physical/authenticated connection may advance MapEpoch after a trusted
    /// transfer. A new ConnectionEpoch MUST create a new GatewayConnection.
    /// </summary>
    public bool TryUpdateTrustedBinding(ActorSnapshot trustedActor)
    {
        ConnectionBinding next;
        try { next = ConnectionBinding.FromTrustedSnapshot(trustedActor); }
        catch (ArgumentOutOfRangeException) { return false; }

        lock (_bindingGate)
        {
            if (_closed ||
                next.ActorId != _binding.ActorId ||
                next.ConnectionEpoch != _binding.ConnectionEpoch ||
                next.MapEpoch < _binding.MapEpoch)
                return false;
            if (next.MapEpoch > _binding.MapEpoch)
            {
                _worldReadySentMapEpoch = 0;
                _worldReadySentTick = -1;
                _worldReadyAckedMapEpoch = 0;
                _worldReadyAckedTick = -1;
                _resyncPending = false;
                _visibleActors.Clear();
            }
            _binding = next;
            return true;
        }
    }

    public bool TryDequeueReliable(out byte[] frame)
    {
        if (IsClosed)
        {
            frame = Array.Empty<byte>();
            return false;
        }
        return _reliable.TryDequeue(out frame);
    }

    public async ValueTask<byte[]?> DequeueReliableAsync(CancellationToken cancellationToken)
    {
        if (IsClosed) return null;
        return await _reliable.DequeueAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Close()
    {
        lock (_bindingGate)
        {
            if (_closed) return;
            _closed = true;
            _visibleActors.Clear();
        }
        _reliable.Clear();
    }
}
