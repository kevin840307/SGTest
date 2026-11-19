using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class QuestWireChecks
{
    public static void Verify()
    {
        Checks.Run("quest wire carries only server-owned bounded dialogue view", () =>
        {
            var open=new QuestOpenWire(9,7,88);
            var openFrame=WireProtocol.EncodeQuestOpen(open);
            Checks.Require(openFrame.Length==WireProtocol.QuestOpenFrameBytes&&
                WireProtocol.TryDecodeQuestOpen(openFrame,out var openBack)&&
                openBack==open,"quest open roundtrip");

            var choose=new QuestChooseWire(9,8,88,91001,2);
            var chooseFrame=WireProtocol.EncodeQuestChoose(choose);
            Checks.Require(chooseFrame.Length==WireProtocol.QuestChooseFrameBytes&&
                WireProtocol.TryDecodeQuestChoose(chooseFrame,out var chooseBack)&&
                chooseBack==choose,"quest choice roundtrip");

            var view=new QuestViewWire(
                9,8,QuestViewWireStatus.Applied,88,91001,3,
                QuestProgressState.Active,1,4,
                "任務標題","請選擇下一步。",
                [new QuestViewChoiceWire(2,"完成"),
                 new QuestViewChoiceWire(3,"稍後再說")]);
            var frame=WireProtocol.EncodeQuestView(view);
            Checks.Require(frame.Length<WireProtocol.QuestViewMaximumFrameBytes&&
                WireProtocol.TryDecodeQuestView(frame,out var decoded)&&
                decoded is not null&&decoded.MapEpoch==9&&
                decoded.QuestId==91001&&decoded.RuleRevision==3&&
                decoded.State==QuestProgressState.Active&&decoded.Stage==1&&
                decoded.ProgressRevision==4&&decoded.Title=="任務標題"&&
                decoded.Text=="請選擇下一步。"&&
                decoded.Choices.Select(x=>x.ChoiceId).SequenceEqual([2,3]),
                "quest UTF8 view roundtrip");

            var rejected=new QuestViewWire(
                9,9,QuestViewWireStatus.TooFar,88,0,0,
                QuestProgressState.NotStarted,0,0,"","",
                Array.Empty<QuestViewChoiceWire>());
            Checks.Require(WireProtocol.TryDecodeQuestView(
                WireProtocol.EncodeQuestView(rejected),out var rejectedBack)&&
                rejectedBack is not null&&rejectedBack.Status==QuestViewWireStatus.TooFar&&
                rejectedBack.QuestId==0&&rejectedBack.Choices.Count==0,
                "quest no-view rejection roundtrip");
        });

        Checks.Run("quest wire rejects forged stage state and unbounded text", () =>
        {
            bool badCompleted=false,badError=false,badText=false;
            try
            {
                WireProtocol.EncodeQuestView(new(
                    9,1,QuestViewWireStatus.Ready,88,91001,1,
                    QuestProgressState.Completed,2,2,"Q","Done",
                    [new QuestViewChoiceWire(1,"illegal")]));
            }
            catch(ArgumentOutOfRangeException){badCompleted=true;}
            try
            {
                WireProtocol.EncodeQuestView(new(
                    9,1,QuestViewWireStatus.TooFar,88,91001,1,
                    QuestProgressState.Active,1,1,"Q","leak",
                    Array.Empty<QuestViewChoiceWire>()));
            }
            catch(ArgumentOutOfRangeException){badError=true;}
            try
            {
                WireProtocol.EncodeQuestView(new(
                    9,1,QuestViewWireStatus.Ready,88,91001,1,
                    QuestProgressState.NotStarted,0,0,"Q",
                    new string('x',WireProtocol.QuestViewMaxTextBytes+1),
                    Array.Empty<QuestViewChoiceWire>()));
            }
            catch(ArgumentOutOfRangeException){badText=true;}
            Checks.Require(badCompleted&&badError&&badText,
                "quest wire admitted forged/unbounded authority state");
        });
    }
}
