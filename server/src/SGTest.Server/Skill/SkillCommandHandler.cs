using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Skill;

/// <summary>
/// Trusted skill command settlement. Skill rules and preflight remain on
/// the original router; MP settlement and combat suspension use the same
/// authenticated World/SQLite owners and serialized gameplay gate.
/// </summary>
internal sealed class SkillCommandHandler
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PlayerCombatSettlement? _combat;
    private readonly long _pickupWorldGeneration;
    private readonly WorldResultRouter _router;
    private readonly SemaphoreSlim _gameplayGate;

    internal SkillCommandHandler(
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

    internal async Task HandleSkillAsync(TrustedSkillUse request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        ActorSnapshot? suspended=null;
        try
        {
            if(!_router.IsCurrentBinding(binding))return;

            var preflight=await _router.PrepareSkillAsync(request)
                .ConfigureAwait(false);
            if(!preflight.Ready)
            {
                var rejected=preflight.Rejected ??
                    throw new InvalidOperationException(
                        "Skill preflight rejection missing outcome");
                var sent=_router.QueueSkillUseResult(request,rejected);
                if(sent!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable skill preflight rejection could not be queued: "+sent);
                return;
            }

            var prepared=preflight.Prepared!.Value;
            if(prepared.Rule.MpCost==0)
            {
                await _router.FinishPreparedSkillAsync(prepared)
                    .ConfigureAwait(false);
                return;
            }

            if(_combat is null)
            {
                await _router.FinishPreparedSkillAsync(
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
                await _router.FinishPreparedSkillAsync(
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
                await _router.FinishPreparedSkillAsync(
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
                var profile=await _identity.GetCharacterCombatProfileAsync(actor.ActorId)
                    .ConfigureAwait(false)
                    ?? throw new IOException("Committed skill MP profile disappeared before HUD refresh");
                var baseline=_router.QueuePlayerCombatState(
                    binding,hp,maxHp,mp.CurrentMp,mp.MaxMp,
                    profile.Data.CurrentWp,profile.Data.MaxWp);
                if(baseline!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Committed skill MP baseline could not be queued: "+baseline);
            }

            await _router.FinishPreparedSkillAsync(prepared,cancel)
                .ConfigureAwait(false);
        }
        finally
        {
            if(suspended is { } actor &&
                _router.IsCurrentBinding(binding) &&
                !_combat!.TryResume(actor))
                throw new InvalidOperationException(
                    "Skill Combat generation could not resume");
            _gameplayGate.Release();
        }
    }
}
