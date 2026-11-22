using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;
using SGTest.Server.Wire;

namespace SGTest.Server.Npc;

/// <summary>
/// Trusted NPC quest open and choice handling. The quest catalog and SQLite
/// store retain rule and progress ownership; authenticated transport authorization, map generation,
/// reliable delivery, and command serialization remain on the calling session.
/// </summary>
internal sealed class NpcQuestHandler
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PlayerCombatSettlement? _combat;
    private readonly NpcQuestCatalog? _quests;
    private readonly long _pickupWorldGeneration;
    private readonly WorldResultRouter _router;
    private readonly SemaphoreSlim _gameplayGate;

    internal NpcQuestHandler(
        SqliteIdentityStore identity, ServerWorldHost world,
        PlayerCombatSettlement? combat, NpcQuestCatalog? quests,
        long pickupWorldGeneration,
        WorldResultRouter router, SemaphoreSlim gameplayGate)
    {
        _identity = identity;
        _world = world;
        _combat = combat;
        _quests = quests;
        _pickupWorldGeneration = pickupWorldGeneration;
        _router = router;
        _gameplayGate = gameplayGate;
    }

    internal async Task HandleQuestAsync(TrustedQuestCommand request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            if(snapshot is not { } actor||
               actor.ActorId!=request.ActorId||
               actor.ConnectionEpoch!=request.ConnectionEpoch||
               actor.MapEpoch!=request.RequestMapEpoch)
            {
                var stale=_router.QueueQuestView(
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
                var rejected=_router.QueueQuestView(request,status,null);
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
                var queued=_router.QueueQuestView(request,status,current.View);
                if(queued!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable quest open view could not be queued: "+queued);
                return;
            }

            if(request.QuestId!=point.Rule.QuestId)
            {
                var unknown=_router.QueueQuestView(
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
            var sent=_router.QueueQuestView(request,wireStatus,view);
            if(sent!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable quest choice view could not be queued: "+sent);
        }
        finally { _gameplayGate.Release(); }
    }
}
