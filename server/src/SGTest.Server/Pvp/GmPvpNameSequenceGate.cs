using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Pvp;

public enum GmPvpNameSequenceDecision : byte { New, ExactReplay, Stale }

/// <summary>
/// One authenticated socket's GM PvP-name command cursor. The capability itself
/// is server-owned on GatewayConnection; this gate only prevents a client from
/// reusing an accepted sequence for a different target/state.
/// </summary>
public sealed class GmPvpNameSequenceGate
{
    private long _mapEpoch;
    private uint _lastSequence;
    private long _lastTargetActorId;
    private PvpNameState _lastState;
    private GmPvpNameResultWire _lastReceipt;

    public GmPvpNameResultWire LastReceipt=>_lastReceipt;

    public GmPvpNameSequenceDecision Inspect(TrustedGmPvpNameCommand request)
    {
        if(request.RequestMapEpoch<=0||request.GmSequence==0||
           request.TargetActorId<=0||(byte)request.State>4)
            throw new ArgumentOutOfRangeException(nameof(request));
        if(request.RequestMapEpoch<_mapEpoch)
            return GmPvpNameSequenceDecision.Stale;
        if(request.RequestMapEpoch>_mapEpoch)
        {
            _mapEpoch=request.RequestMapEpoch;
            _lastSequence=0;
            _lastTargetActorId=0;
            _lastReceipt=default;
        }
        if(request.GmSequence>_lastSequence)
            return GmPvpNameSequenceDecision.New;
        return request.GmSequence==_lastSequence&&
               request.TargetActorId==_lastTargetActorId&&
               request.State==_lastState
            ? GmPvpNameSequenceDecision.ExactReplay
            : GmPvpNameSequenceDecision.Stale;
    }

    public void Record(
        TrustedGmPvpNameCommand request,GmPvpNameResultWire receipt)
    {
        if(Inspect(request)!=GmPvpNameSequenceDecision.New||
           receipt.MapEpoch!=request.RequestMapEpoch||
           receipt.GmSequence!=request.GmSequence||
           receipt.TargetActorId!=request.TargetActorId||
           receipt.State!=request.State)
            throw new InvalidOperationException(
                "GM PvP-name receipt does not match admission");
        _lastSequence=request.GmSequence;
        _lastTargetActorId=request.TargetActorId;
        _lastState=request.State;
        _lastReceipt=receipt;
    }
}
