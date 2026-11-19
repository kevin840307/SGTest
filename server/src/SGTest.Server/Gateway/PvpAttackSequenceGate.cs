using SGTest.Server.Wire;

namespace SGTest.Server.Gateway;

public enum PvpAttackSequenceDecision : byte { New, ExactReplay, Stale }

public sealed class PvpAttackSequenceGate
{
    private long _mapEpoch;
    private uint _lastSequence;
    private long _lastTarget;
    private long _lastAppliedTick=-1;
    private PvpAttackResultWire _lastReceipt;
    public PvpAttackResultWire LastReceipt=>_lastReceipt;

    public PvpAttackSequenceDecision Inspect(TrustedPvpAttack request)
    {
        if(request.RequestMapEpoch<=0||request.AttackSequence==0||request.TargetActorId<=0)
            throw new ArgumentOutOfRangeException(nameof(request));
        if(request.RequestMapEpoch<_mapEpoch)return PvpAttackSequenceDecision.Stale;
        if(request.RequestMapEpoch>_mapEpoch)
        {
            _mapEpoch=request.RequestMapEpoch; _lastSequence=0; _lastTarget=0;
            _lastAppliedTick=-1; _lastReceipt=default;
        }
        if(request.AttackSequence>_lastSequence)return PvpAttackSequenceDecision.New;
        return request.AttackSequence==_lastSequence&&request.TargetActorId==_lastTarget
            ? PvpAttackSequenceDecision.ExactReplay:PvpAttackSequenceDecision.Stale;
    }

    public bool CanApplyAt(long serverTick,int cooldownTicks)
    {
        if(serverTick<0||cooldownTicks<1)throw new ArgumentOutOfRangeException(nameof(serverTick));
        return _lastAppliedTick<0||serverTick-_lastAppliedTick>=cooldownTicks;
    }

    public void Record(TrustedPvpAttack request,PvpAttackResultWire receipt,bool applied)
    {
        if(Inspect(request)!=PvpAttackSequenceDecision.New||
           receipt.MapEpoch!=request.RequestMapEpoch||
           receipt.AttackSequence!=request.AttackSequence||
           receipt.TargetActorId!=request.TargetActorId||
           applied!=(receipt.Status==PvpAttackStatus.Applied))
            throw new InvalidOperationException("PvP attack receipt does not match admission");
        _lastSequence=request.AttackSequence; _lastTarget=request.TargetActorId;
        _lastReceipt=receipt; if(applied)_lastAppliedTick=receipt.ServerTick;
    }
}
