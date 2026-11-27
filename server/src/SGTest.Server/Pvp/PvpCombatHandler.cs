using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Skill;
using SGTest.Server.Visibility;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Pvp;

/// <summary>Per-socket PvP physical/skill attacks under the shared authenticated
/// gameplay gate, authoritative World geometry and combat sequence ownership.
/// </summary>
internal sealed class PvpCombatHandler
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PlayerCombatSettlement? _combat;
    private readonly long _pickupWorldGeneration;
    private readonly WorldResultRouter _router;
    private readonly SemaphoreSlim _gameplayGate;
    private readonly PvpAttackSequenceGate _attackSequenceGate = new();

    internal PvpCombatHandler(
        SqliteIdentityStore identity, ServerWorldHost world,
        PlayerCombatSettlement? combat, long pickupWorldGeneration,
        WorldResultRouter router, SemaphoreSlim gameplayGate)
    {
        _identity = identity;
        _world = world;
        _combat = combat;
        _pickupWorldGeneration = pickupWorldGeneration;
        _router = router;
        _gameplayGate = gameplayGate;
    }

    internal async Task HandleAttackAsync(TrustedPvpAttack request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var decision=_attackSequenceGate.Inspect(request);
            if(decision==PvpAttackSequenceDecision.ExactReplay)
            {
                var replay=_router.QueuePvpAttackResult(request,_attackSequenceGate.LastReceipt);
                if(replay!=GatewaySendStatus.Enqueued)
                    throw new IOException("Reliable PvP attack replay could not be queued: "+replay);
                return;
            }
            if(decision==PvpAttackSequenceDecision.Stale)
            {
                var stale=new PvpAttackResultWire(
                    request.RequestMapEpoch,request.AttackSequence,
                    PvpAttackStatus.StaleSequence,0,request.TargetActorId,0,false);
                var sent=_router.QueuePvpAttackResult(request,stale);
                if(sent!=GatewaySendStatus.Enqueued)
                    throw new IOException("Reliable stale PvP attack result could not be queued: "+sent);
                return;
            }

            PvpAttackResultWire result;
            if(request.Profile is not { } profile||_combat is null)
            {
                result=new(request.RequestMapEpoch,request.AttackSequence,
                    PvpAttackStatus.CombatUnavailable,0,request.TargetActorId,0,false);
            }
            else
            {
                var geometry=await _world.TrustedGetPvpAttackGeometryAsync(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                    request.TargetActorId,profile.AttackRange).ConfigureAwait(false);
                PvpAttackStatus? rejected=geometry.Status switch
                {
                    PvpAttackGeometryStatus.Ready=>null,
                    PvpAttackGeometryStatus.StaleGeneration=>PvpAttackStatus.StaleGeneration,
                    PvpAttackGeometryStatus.SelfTarget=>PvpAttackStatus.SelfTarget,
                    PvpAttackGeometryStatus.NonPvpMap=>PvpAttackStatus.NonPvpMap,
                    PvpAttackGeometryStatus.AttackerDead=>PvpAttackStatus.AttackerDead,
                    PvpAttackGeometryStatus.TargetDead=>PvpAttackStatus.TargetDead,
                    PvpAttackGeometryStatus.TooFar=>PvpAttackStatus.TooFar,
                    PvpAttackGeometryStatus.Blocked=>PvpAttackStatus.Blocked,
                    _=>PvpAttackStatus.TargetMissing
                };
                if(rejected is not null)
                    result=new(request.RequestMapEpoch,request.AttackSequence,
                        rejected.Value,geometry.ServerTick,request.TargetActorId,0,false);
                else
                {
                    var now=DateTimeOffset.UtcNow;
                    var attackerState=await _identity.GetCharacterPvpStateAsync(
                        request.ActorId).ConfigureAwait(false);
                    var targetState=await _identity.GetCharacterPvpStateAsync(
                        request.TargetActorId).ConfigureAwait(false);
                    if(!PvpCrimePolicy.IsCombatEligible(attackerState,now))
                        result=new(request.RequestMapEpoch,request.AttackSequence,
                            PvpAttackStatus.AttackerProtected,geometry.ServerTick,
                            request.TargetActorId,0,false);
                    else if(!PvpCrimePolicy.IsCombatEligible(targetState,now))
                        result=new(request.RequestMapEpoch,request.AttackSequence,
                            PvpAttackStatus.TargetProtected,geometry.ServerTick,
                            request.TargetActorId,0,false);
                    else if(_combat.CurrentHp(geometry.Attacker)<=0)
                        result=new(request.RequestMapEpoch,request.AttackSequence,
                            PvpAttackStatus.AttackerDead,geometry.ServerTick,
                            request.TargetActorId,0,false);
                    else if(_combat.CurrentHp(geometry.Target)<=0)
                        result=new(request.RequestMapEpoch,request.AttackSequence,
                            PvpAttackStatus.TargetDead,geometry.ServerTick,
                            request.TargetActorId,0,false);
                    else if(!_attackSequenceGate.CanApplyAt(
                        geometry.ServerTick,profile.AttackCooldownTicks))
                        result=new(request.RequestMapEpoch,request.AttackSequence,
                            PvpAttackStatus.Cooldown,geometry.ServerTick,
                            request.TargetActorId,0,false);
                    else
                    {
                        bool normalWhite=PvpCrimePolicy.IsNormalWhite(targetState,now);
                        var posted=_combat.TryPostPvpHit(
                            geometry.Target,geometry.Attacker,request.AttackSequence,
                            profile.Attack,geometry.ServerTick,normalWhite,out var completion);
                        if(posted==CombatPostStatus.Backpressured)
                            result=new(request.RequestMapEpoch,request.AttackSequence,
                                PvpAttackStatus.Backpressured,geometry.ServerTick,
                                request.TargetActorId,0,false);
                        else if(posted!=CombatPostStatus.Accepted||completion is null)
                            result=new(request.RequestMapEpoch,request.AttackSequence,
                                PvpAttackStatus.CombatUnavailable,geometry.ServerTick,
                                request.TargetActorId,0,false);
                        else
                        {
                            var settled=await completion.WaitAsync(
                                TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                            result=new(request.RequestMapEpoch,request.AttackSequence,
                                PvpAttackStatus.Applied,settled.ServerTick,
                                request.TargetActorId,settled.Damage,settled.Died);
                            _attackSequenceGate.Record(request,result,true);
                            var sent=_router.QueuePvpAttackResult(request,result);
                            if(sent!=GatewaySendStatus.Enqueued)
                                throw new IOException("Reliable applied PvP attack result could not be queued: "+sent);
                            return;
                        }
                    }
                }
            }
            _attackSequenceGate.Record(request,result,false);
            var queued=_router.QueuePvpAttackResult(request,result);
            if(queued!=GatewaySendStatus.Enqueued)
                throw new IOException("Reliable PvP attack result could not be queued: "+queued);
        }
        finally { _gameplayGate.Release(); }
    }

    internal async Task HandleSkillAsync(TrustedPvpSkill request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        ActorSnapshot? suspended=null;
        try
        {
            if(!_router.IsCurrentBinding(binding))return;

            var skillRequest=new TrustedSkillUse(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.SkillSequence,request.SkillId,request.TargetActorId,
                request.Profile,SkillTargetKind.SinglePlayer);
            var preflight=await _router.PrepareSkillAsync(skillRequest)
                .ConfigureAwait(false);
            if(!preflight.Ready)
            {
                var rejected=preflight.Rejected ??
                    throw new InvalidOperationException(
                        "PvP skill preflight rejection missing outcome");
                var frame=new PvpSkillResultWire(
                    request.RequestMapEpoch,request.SkillSequence,
                    rejected.Status,rejected.ServerTick,request.SkillId,
                    request.TargetActorId,0,false);
                var sent=_router.QueuePvpSkillResult(request,frame);
                if(sent!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable PvP skill rejection could not be queued: "+sent);
                return;
            }

            if(_combat is null)
            {
                var preparedMissing=preflight.Prepared!.Value;
                var cancelled=await _router.FinishPreparedPlayerSkillAsync(
                    preparedMissing,SkillUseStatus.CombatUnavailable)
                    .ConfigureAwait(false);
                var sent=_router.QueuePvpSkillResult(request,new(
                    request.RequestMapEpoch,request.SkillSequence,
                    cancelled.Status,cancelled.ServerTick,request.SkillId,
                    request.TargetActorId,0,false));
                if(sent!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable unavailable PvP skill result could not be queued: "+sent);
                return;
            }

            var prepared=preflight.Prepared!.Value;

            async Task<(SkillUseStatus? Status,PvpAttackGeometry Geometry,
                CharacterPvpState? TargetState)> ValidateTarget()
            {
                var geometry=await _world.TrustedGetPvpAttackGeometryAsync(
                    request.ActorId,request.ConnectionEpoch,
                    request.RequestMapEpoch,request.TargetActorId,
                    prepared.Rule.Range).ConfigureAwait(false);
                SkillUseStatus? status=geometry.Status switch
                {
                    PvpAttackGeometryStatus.Ready=>null,
                    PvpAttackGeometryStatus.StaleGeneration=>SkillUseStatus.OldMapEpoch,
                    PvpAttackGeometryStatus.SelfTarget=>SkillUseStatus.SelfTarget,
                    PvpAttackGeometryStatus.NonPvpMap=>SkillUseStatus.NonPvpMap,
                    PvpAttackGeometryStatus.AttackerDead=>SkillUseStatus.CombatUnavailable,
                    PvpAttackGeometryStatus.TargetDead=>SkillUseStatus.TargetDead,
                    PvpAttackGeometryStatus.TooFar=>SkillUseStatus.OutOfRange,
                    PvpAttackGeometryStatus.Blocked=>SkillUseStatus.Blocked,
                    _=>SkillUseStatus.TargetMissing
                };
                if(status is not null)return(status,geometry,null);

                var now=DateTimeOffset.UtcNow;
                var attackerState=await _identity.GetCharacterPvpStateAsync(
                    request.ActorId).ConfigureAwait(false);
                var targetState=await _identity.GetCharacterPvpStateAsync(
                    request.TargetActorId).ConfigureAwait(false);
                if(!PvpCrimePolicy.IsCombatEligible(attackerState,now))
                    return(SkillUseStatus.AttackerProtected,geometry,targetState);
                if(!PvpCrimePolicy.IsCombatEligible(targetState,now))
                    return(SkillUseStatus.TargetProtected,geometry,targetState);
                if(_combat.CurrentHp(geometry.Target)<=0)
                    return(SkillUseStatus.TargetDead,geometry,targetState);
                return(null,geometry,targetState);
            }

            var validation=await ValidateTarget().ConfigureAwait(false);
            if(validation.Status is { } initialReject)
            {
                var cancelled=await _router.FinishPreparedPlayerSkillAsync(
                    prepared,initialReject).ConfigureAwait(false);
                var sent=_router.QueuePvpSkillResult(request,new(
                    request.RequestMapEpoch,request.SkillSequence,
                    cancelled.Status,validation.Geometry.ServerTick,
                    request.SkillId,request.TargetActorId,0,false));
                if(sent!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable PvP skill validation rejection could not be queued: "+sent);
                return;
            }

            int suspendedHp=0;
            if(prepared.Rule.MpCost>0)
            {
                var snapshot=await _world.TrustedGetActorAsync(request.ActorId)
                    .ConfigureAwait(false);
                if(snapshot is not { } actor||
                   actor.ActorId!=request.ActorId||
                   actor.ConnectionEpoch!=request.ConnectionEpoch||
                   actor.MapEpoch!=request.RequestMapEpoch)
                {
                    var cancelled=await _router.FinishPreparedPlayerSkillAsync(
                        prepared,SkillUseStatus.OldMapEpoch).ConfigureAwait(false);
                    var sent=_router.QueuePvpSkillResult(request,new(
                        request.RequestMapEpoch,request.SkillSequence,
                        cancelled.Status,cancelled.ServerTick,
                        request.SkillId,request.TargetActorId,0,false));
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale PvP skill result could not be queued: "+sent);
                    return;
                }

                await _combat.SuspendAndDrainAsync(
                    actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch)
                    .ConfigureAwait(false);
                suspended=actor;
                if(!_combat.TryGetSuspendedCombatState(
                    actor,out suspendedHp,out int maxHp)||suspendedHp<=0)
                {
                    var cancelled=await _router.FinishPreparedPlayerSkillAsync(
                        prepared,SkillUseStatus.CombatUnavailable)
                        .ConfigureAwait(false);
                    var sent=_router.QueuePvpSkillResult(request,new(
                        request.RequestMapEpoch,request.SkillSequence,
                        cancelled.Status,cancelled.ServerTick,
                        request.SkillId,request.TargetActorId,0,false));
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable dead PvP skill result could not be queued: "+sent);
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
                SkillUseStatus? mpReject=mp.Status switch
                {
                    SkillMpConsumeStatus.Consumed or
                    SkillMpConsumeStatus.AlreadyConsumed=>null,
                    SkillMpConsumeStatus.InsufficientMp=>
                        SkillUseStatus.InsufficientMp,
                    SkillMpConsumeStatus.StaleSequence=>
                        SkillUseStatus.ReplayedSequence,
                    _=>SkillUseStatus.CombatUnavailable
                };
                if(mpReject is { } rejected)
                {
                    var cancelled=await _router.FinishPreparedPlayerSkillAsync(
                        prepared,rejected).ConfigureAwait(false);
                    var sent=_router.QueuePvpSkillResult(request,new(
                        request.RequestMapEpoch,request.SkillSequence,
                        cancelled.Status,cancelled.ServerTick,
                        request.SkillId,request.TargetActorId,0,false));
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable MP-rejected PvP skill result could not be queued: "+sent);
                    return;
                }
                var profile=await _identity.GetCharacterCombatProfileAsync(actor.ActorId)
                    .ConfigureAwait(false)
                    ?? throw new IOException("Committed PvP skill MP profile disappeared before HUD refresh");
                var baseline=_router.QueuePlayerCombatState(
                    binding,suspendedHp,maxHp,mp.CurrentMp,mp.MaxMp,
                    profile.Data.CurrentWp,profile.Data.MaxWp);
                if(baseline!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Committed PvP skill MP baseline could not be queued: "+baseline);

                // Target may move or change PvP state while SQLite MP is
                // settling. Revalidate before reserving durable damage.
                validation=await ValidateTarget().ConfigureAwait(false);
                if(validation.Status is { } afterMpReject)
                {
                    var cancelled=await _router.FinishPreparedPlayerSkillAsync(
                        prepared,afterMpReject).ConfigureAwait(false);
                    var sent=_router.QueuePvpSkillResult(request,new(
                        request.RequestMapEpoch,request.SkillSequence,
                        cancelled.Status,validation.Geometry.ServerTick,
                        request.SkillId,request.TargetActorId,0,false));
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable post-MP PvP skill rejection could not be queued: "+sent);
                    return;
                }
            }

            var geometryReady=validation.Geometry;
            var targetStateReady=validation.TargetState ??
                throw new InvalidOperationException(
                    "PvP skill ready target state missing");
            bool normalWhite=PvpCrimePolicy.IsNormalWhite(
                targetStateReady,DateTimeOffset.UtcNow);
            var posted=_combat.TryPostPvpSkillHit(
                geometryReady.Target,geometryReady.Attacker,
                request.SkillSequence,request.SkillId,
                prepared.Rule.FixedDamage,geometryReady.ServerTick,
                normalWhite,out var completion);
            if(posted!=CombatPostStatus.Accepted||completion is null)
            {
                var status=posted==CombatPostStatus.Backpressured
                    ? SkillUseStatus.Backpressured:SkillUseStatus.TargetDead;
                var cancelled=await _router.FinishPreparedPlayerSkillAsync(
                    prepared,status).ConfigureAwait(false);
                var sent=_router.QueuePvpSkillResult(request,new(
                    request.RequestMapEpoch,request.SkillSequence,
                    cancelled.Status,geometryReady.ServerTick,
                    request.SkillId,request.TargetActorId,0,false));
                if(sent!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable PvP skill combat rejection could not be queued: "+sent);
                return;
            }

            var finalized=await _router.FinishPreparedPlayerSkillAsync(prepared)
                .ConfigureAwait(false);
            if(finalized.Status!=SkillUseStatus.Applied)
                throw new InvalidOperationException(
                    "Accepted PvP skill damage lost shared skill commit token");
            var settled=await completion.WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            var applied=_router.QueuePvpSkillResult(request,new(
                request.RequestMapEpoch,request.SkillSequence,
                SkillUseStatus.Applied,settled.ServerTick,
                request.SkillId,request.TargetActorId,
                settled.Damage,settled.Died));
            if(applied!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable applied PvP skill result could not be queued: "+applied);
        }
        finally
        {
            if(suspended is { } actor&&
               _router.IsCurrentBinding(binding)&&
               !_combat!.TryResume(actor))
                throw new InvalidOperationException(
                    "PvP skill Combat generation could not resume");
            _gameplayGate.Release();
        }
    }
}
