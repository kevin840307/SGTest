using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Skill;
using SGTest.Server.Visibility;
using SGTest.Server.Wire;
using SGTest.Server.World;
using System.Security.Cryptography;
using System.Text;

namespace SGTest.Server.Pvp;

/// <summary>Per-socket PvP mode and authenticated GM name-state commands.
/// State changes are SQLite-authoritative and broadcast via the trusted router.
/// </summary>
internal sealed class PvpStateHandler
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PlayerCombatSettlement? _combat;
    private readonly WorldResultRouter _router;
    private readonly SemaphoreSlim _gameplayGate;
    private readonly GmPvpNameSequenceGate _gmNameSequenceGate = new();
    private readonly PvpModeSequenceGate _modeSequenceGate = new();

    internal PvpStateHandler(
        SqliteIdentityStore identity, ServerWorldHost world,
        PlayerCombatSettlement? combat,
        WorldResultRouter router, SemaphoreSlim gameplayGate)
    {
        _identity = identity;
        _world = world;
        _combat = combat;
        _router = router;
        _gameplayGate = gameplayGate;
    }

    internal async Task HandleModeAsync(TrustedPvpModeCommand request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            PvpModeResultStatus status;
            PvpMode mode;
            bool mapAllows=PvpMapPolicy.AllowsPlayerPvp(request.RequestMapEpoch>0
                && snapshot is { } a ? a.MapId : 0);
            if(snapshot is not { } actor||
               actor.ActorId!=request.ActorId||
               actor.ConnectionEpoch!=request.ConnectionEpoch||
               actor.MapEpoch!=request.RequestMapEpoch)
            {
                mode=PvpMode.ProtectedBlue;
                status=PvpModeResultStatus.StaleGeneration;
            }
            else
            {
                var decision=_modeSequenceGate.Inspect(request);
                mapAllows=PvpMapPolicy.AllowsPlayerPvp(actor.MapId);
                if(decision==PvpModeSequenceDecision.ExactReplay)
                {
                    // A lost receipt is replayed without a second DB write.
                    var prior=_modeSequenceGate.LastReceipt;
                    status=prior.Status;
                    mode=prior.Mode;
                    mapAllows=prior.MapAllowsPlayerPvp;
                }
                else
                {
                    var current=await _identity.GetCharacterPvpModeAsync(
                        actor.ActorId).ConfigureAwait(false);
                    mode=current.Mode;
                    if(decision==PvpModeSequenceDecision.Stale)
                        status=PvpModeResultStatus.StaleSequence;
                    else
                    {
                        bool dead=_combat is not null&&_combat.CurrentHp(actor)<=0;
                        if(request.Action==PvpModeCommandAction.Query)
                            status=PvpModeResultStatus.Ready;
                        else if(dead)
                            status=PvpModeResultStatus.PlayerDead;
                        else if(request.Action==PvpModeCommandAction.SetWhite&&!mapAllows)
                            status=PvpModeResultStatus.NonPvpMap;
                        else
                        {
                            var desired=request.Action==PvpModeCommandAction.SetWhite
                                ? PvpMode.EnabledWhite:PvpMode.ProtectedBlue;
                            if(mode==desired)
                                status=PvpModeResultStatus.Unchanged;
                            else
                            {
                                var persisted=await _identity.SetCharacterPvpModeAsync(
                                    actor.ActorId,desired).ConfigureAwait(false);
                                mode=persisted.Mode;
                                status=PvpModeResultStatus.Applied;
                                var fullState=await _identity.GetCharacterPvpStateAsync(
                                    actor.ActorId).ConfigureAwait(false);
                                var published=_router.PublishPlayerPvpState(
                                    actor.ActorId,actor.ConnectionEpoch,fullState);
                                if(published==GatewaySendStatus.Backpressured)
                                    throw new AoiBackpressureException(
                                        "PvP identity update queue full; committed mode cannot be hidden");
                            }
                        }
                        // Even a denied mutation must not turn valid on retry.
                        _modeSequenceGate.Record(request,status,mode,mapAllows);
                    }
                }
            }
            var sent=_router.QueuePvpModeResult(
                request,status,mode,mapAllows);
            if(sent!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable PvP mode result could not be queued: "+sent);
        }
        finally { _gameplayGate.Release(); }
    }

    internal async Task HandleGmNameAsync(TrustedGmPvpNameCommand request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var decision=_gmNameSequenceGate.Inspect(request);
            if(decision==GmPvpNameSequenceDecision.ExactReplay)
            {
                var replay=_router.QueueGmPvpNameResult(
                    request,_gmNameSequenceGate.LastReceipt);
                if(replay!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable GM PvP-name replay could not be queued: "+replay);
                return;
            }
            if(decision==GmPvpNameSequenceDecision.Stale)
            {
                var stale=new GmPvpNameResultWire(
                    request.RequestMapEpoch,request.GmSequence,
                    GmPvpNameResultStatus.StaleSequence,
                    request.TargetActorId,request.State);
                var sent=_router.QueueGmPvpNameResult(request,stale);
                if(sent!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable stale GM PvP-name result could not be queued: "+sent);
                return;
            }

            GmPvpNameResultWire result;
            if(request.OperatorId is null)
                result=new(
                    request.RequestMapEpoch,request.GmSequence,
                    GmPvpNameResultStatus.Unauthorized,
                    request.TargetActorId,request.State);
            else
            {
                var target=_router.SnapshotActiveBindings()
                    .FirstOrDefault(x=>x.ActorId==request.TargetActorId);
                if(target.ActorId<=0)
                    result=new(
                        request.RequestMapEpoch,request.GmSequence,
                        GmPvpNameResultStatus.TargetOffline,
                        request.TargetActorId,request.State);
                else
                {
                    string eventId=OnlineGmEventId(request);
                    var changed=await _identity.ApplyPvpNameAdminAsync(
                        request.TargetActorId,request.State,
                        request.OperatorId,
                        "online authenticated GM PvP-name command",
                        eventId,DateTimeOffset.UtcNow).ConfigureAwait(false);
                    var published=_router.PublishPlayerPvpState(
                        request.TargetActorId,target.ConnectionEpoch,
                        changed.State);
                    if(published==GatewaySendStatus.Backpressured)
                        throw new IOException(
                            "Committed GM PvP-name identity update queue full");
                    result=new(
                        request.RequestMapEpoch,request.GmSequence,
                        changed.Replayed
                            ? GmPvpNameResultStatus.Replayed
                            : GmPvpNameResultStatus.Applied,
                        request.TargetActorId,request.State);
                }
            }
            _gmNameSequenceGate.Record(request,result);
            var queued=_router.QueueGmPvpNameResult(request,result);
            if(queued!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable GM PvP-name result could not be queued: "+queued);
        }
        finally { _gameplayGate.Release(); }
    }
    private static string OnlineGmEventId(TrustedGmPvpNameCommand request)
    {
        string raw=$"{request.OperatorId}|{request.ActorId}|{request.ConnectionEpoch}|" +
            $"{request.RequestMapEpoch}|{request.GmSequence}";
        return "online-"+Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(raw))).Substring(0,32);
    }
}
