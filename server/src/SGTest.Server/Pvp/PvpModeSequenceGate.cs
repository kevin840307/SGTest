using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Pvp;

/// <summary>
/// One authenticated TLS session's PvP command admission. A map transfer
/// starts a new MapEpoch sequence. Exact retries replay the original reliable
/// receipt; reordered/conflicting bytes must not mutate the durable mode.
/// The single PvP command worker owns this state (no locking or DB I/O).
/// </summary>
public enum PvpModeSequenceDecision : byte { New, ExactReplay, Stale }

public sealed class PvpModeSequenceGate
{
    private long _mapEpoch;
    private uint _lastSequence;
    private PvpModeCommandAction _lastAction;
    private PvpModeResultWire _lastReceipt;

    public PvpModeResultWire LastReceipt => _lastReceipt;

    public PvpModeSequenceDecision Inspect(TrustedPvpModeCommand request)
    {
        if (request.RequestMapEpoch <= 0 || request.PvpSequence == 0 ||
            (byte)request.Action > 2)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.RequestMapEpoch < _mapEpoch)
            return PvpModeSequenceDecision.Stale;
        if (request.RequestMapEpoch > _mapEpoch)
        {
            _mapEpoch = request.RequestMapEpoch;
            _lastSequence = 0;
            _lastReceipt = default;
        }
        if (request.PvpSequence > _lastSequence)
            return PvpModeSequenceDecision.New;
        return request.PvpSequence == _lastSequence && request.Action == _lastAction
            ? PvpModeSequenceDecision.ExactReplay
            : PvpModeSequenceDecision.Stale;
    }

    public void Record(TrustedPvpModeCommand request,
                       PvpModeResultStatus status,PvpMode mode,bool mapAllowsPvp)
    {
        if (Inspect(request) != PvpModeSequenceDecision.New)
            throw new InvalidOperationException("PvP receipt would overwrite an existing command");
        _lastSequence = request.PvpSequence;
        _lastAction = request.Action;
        _lastReceipt = new PvpModeResultWire(
            request.RequestMapEpoch,request.PvpSequence,status,mode,mapAllowsPvp);
    }
}
