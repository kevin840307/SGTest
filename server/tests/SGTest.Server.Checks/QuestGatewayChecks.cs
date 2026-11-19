using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class QuestGatewayChecks
{
    public static void Verify()
    {
        Checks.Run("quest gateway binds identity and blocks forged server dialogue", Gateway);
    }

    private static void Gateway()
    {
        var actor=new ActorSnapshot(11,15,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);

        TrustedQuestCommand open=default;
        var feed=gateway.FeedClientBytes(
            WireProtocol.EncodeQuestOpen(new(9,7,88)),_=>true,out var accepted,
            postQuest:value=>{open=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            open.ActorId==11&&open.ConnectionEpoch==19&&
            open.RequestMapEpoch==9&&open.QuestSequence==7&&
            open.NpcSpawnId==88&&open.Kind==TrustedQuestCommandKind.Open&&
            open.QuestId==0&&open.ChoiceId==0,
            "quest open did not bind authenticated identity");

        var rule=QuestChecks.Rule(1,"ci:quest:v1");
        var initial=new QuestView(
            rule.QuestId,rule.RuleRevision,QuestProgressState.NotStarted,
            0,0,rule.Title,rule.Stages[0].Text,
            rule.Stages[0].Choices.Values.OrderBy(x=>x.ChoiceId).ToArray());
        Checks.Require(
            gateway.QueueQuestView(open,QuestViewWireStatus.Ready,initial)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var viewFrame)&&
            WireProtocol.TryDecodeQuestView(viewFrame,out var view)&&
            view is not null&&view.QuestId==91001&&view.Stage==0&&
            view.Choices.Select(x=>x.ChoiceId).SequenceEqual([1,3]),
            "quest view did not enter reliable FIFO");

        TrustedQuestCommand choose=default;
        feed=gateway.FeedClientBytes(
            WireProtocol.EncodeQuestChoose(new(9,8,88,91001,1)),
            _=>true,out accepted,
            postQuest:value=>{choose=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            choose.ActorId==11&&choose.ConnectionEpoch==19&&
            choose.Kind==TrustedQuestCommandKind.Choose&&
            choose.QuestId==91001&&choose.ChoiceId==1,
            "quest choice trusted context mismatch");

        var active=new QuestView(
            rule.QuestId,rule.RuleRevision,QuestProgressState.Active,
            1,1,rule.Title,rule.Stages[1].Text,
            rule.Stages[1].Choices.Values.ToArray());
        Checks.Require(
            gateway.QueueQuestView(choose,QuestViewWireStatus.Applied,active)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var resultFrame)&&
            WireProtocol.TryDecodeQuestView(resultFrame,out var result)&&
            result is not null&&result.Status==QuestViewWireStatus.Applied&&
            result.State==QuestProgressState.Active&&result.Stage==1,
            "quest applied view did not enter reliable FIFO");

        var forged=new GatewayConnection(actor);
        Checks.Require(
            forged.FeedClientBytes(
                WireProtocol.EncodeQuestView(new(
                    9,1,QuestViewWireStatus.Ready,88,91001,1,
                    QuestProgressState.NotStarted,0,0,"Q","text",
                    Array.Empty<QuestViewChoiceWire>())),
                _=>true,out _)==WireFeedStatus.ProtocolViolation,
            "client forged server-only quest view");

        Checks.Require(
            gateway.QueueQuestView(
                choose with{ConnectionEpoch=20},QuestViewWireStatus.Applied,active)==
                GatewaySendStatus.GenerationMismatch,
            "stale quest generation received dialogue");
    }
}
