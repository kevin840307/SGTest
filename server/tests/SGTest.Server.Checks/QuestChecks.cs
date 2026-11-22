using SGTest.Server.Identity;

internal static class QuestChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified quest progress is durable replay-safe and revision-bound",
            () => Lifecycle(root).GetAwaiter().GetResult());
    }

    private static async Task Lifecycle(string root)
    {
        string dir=Path.Combine(root,"quest_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long firstCharacter;
        long secondCharacter;

        var rule=Rule(1,"ci:quest:v1");
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            Checks.Require(SqliteIdentityStore.SchemaVersion==25,
                "identity schema version is not v22");
            long account=(await store.CreateAccountAsync(
                "quest_user","Quest-Password-123!")).AccountId;
            firstCharacter=(await store.CreateCharacterAsync(
                account,0,"QuestOne",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId??throw new Exception("quest character missing");
            secondCharacter=(await store.CreateCharacterAsync(
                account,1,"QuestTwo",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId??throw new Exception("quest sibling missing");

            var initial=await store.GetQuestViewAsync(firstCharacter,rule);
            Checks.Require(initial.Status==QuestViewStatus.Ready&&
                initial.View is { State:QuestProgressState.NotStarted,Stage:0,
                    ProgressRevision:0 }&&
                initial.View.Choices.Select(x=>x.ChoiceId).SequenceEqual([1,3]),
                "quest initial view mismatch");

            var denied=await store.ApplyQuestChoiceAsync(
                new TrustedQuestContext(firstCharacter,501,1,1),
                QuestAccessStatus.TooFar,88,rule,1);
            Checks.Require(denied.Status==QuestChoiceStatus.TooFar,
                "quest access denial mismatch");
            initial=await store.GetQuestViewAsync(firstCharacter,rule);
            Checks.Require(initial.View?.State==QuestProgressState.NotStarted,
                "quest access denial mutated progress");

            var firstContext=new TrustedQuestContext(firstCharacter,501,1,1);
            var first=await store.ApplyQuestChoiceAsync(
                firstContext,QuestAccessStatus.Allowed,88,rule,1);
            var replay=await store.ApplyQuestChoiceAsync(
                firstContext,QuestAccessStatus.Allowed,88,rule,1);
            Checks.Require(first.Status==QuestChoiceStatus.Applied&&
                first.State==QuestProgressState.Active&&first.Stage==1&&
                first.ProgressRevision==1&&
                replay.Status==QuestChoiceStatus.AlreadyApplied&&
                replay.State==QuestProgressState.Active&&replay.Stage==1&&
                replay.ProgressRevision==1,
                "quest first transition/replay mismatch");

            var conflict=await store.ApplyQuestChoiceAsync(
                firstContext,QuestAccessStatus.Allowed,88,rule,3);
            Checks.Require(conflict.Status==QuestChoiceStatus.StaleSequence,
                "quest conflicting replay was not stale");

            var invalid=await store.ApplyQuestChoiceAsync(
                new TrustedQuestContext(firstCharacter,501,1,2),
                QuestAccessStatus.Allowed,88,rule,99);
            Checks.Require(invalid.Status==QuestChoiceStatus.InvalidChoice,
                "quest invalid choice mismatch");

            var completed=await store.ApplyQuestChoiceAsync(
                new TrustedQuestContext(firstCharacter,501,1,2),
                QuestAccessStatus.Allowed,88,rule,2);
            Checks.Require(completed.Status==QuestChoiceStatus.Applied&&
                completed.State==QuestProgressState.Completed&&
                completed.Stage==2&&completed.ProgressRevision==2,
                "quest completion mismatch");

            var final=await store.GetQuestViewAsync(firstCharacter,rule);
            Checks.Require(final.Status==QuestViewStatus.Ready&&
                final.View is { State:QuestProgressState.Completed,Stage:2,
                    ProgressRevision:2 }&&final.View.Choices.Count==0,
                "quest completed view mismatch");

            var afterComplete=await store.ApplyQuestChoiceAsync(
                new TrustedQuestContext(firstCharacter,501,1,3),
                QuestAccessStatus.Allowed,88,rule,2);
            Checks.Require(afterComplete.Status==QuestChoiceStatus.AlreadyCompleted&&
                afterComplete.State==QuestProgressState.Completed&&
                afterComplete.ProgressRevision==2,
                "completed quest admitted another mutation");

            var sibling=await store.GetQuestViewAsync(secondCharacter,rule);
            Checks.Require(sibling.View is
                { State:QuestProgressState.NotStarted,Stage:0,ProgressRevision:0 },
                "quest progress leaked to sibling character");

            var revised=Rule(2,"ci:quest:v2");
            var mismatch=await store.GetQuestViewAsync(firstCharacter,revised);
            Checks.Require(mismatch.Status==QuestViewStatus.RuleRevisionMismatch&&
                mismatch.View is null,
                "quest rule revision change silently reinterpreted progress");
            var mismatchChoice=await store.ApplyQuestChoiceAsync(
                new TrustedQuestContext(firstCharacter,502,1,1),
                QuestAccessStatus.Allowed,88,revised,1);
            Checks.Require(
                mismatchChoice.Status==QuestChoiceStatus.RuleRevisionMismatch,
                "quest revision mismatch admitted choice");
        }

        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var durable=await reopened.GetQuestViewAsync(firstCharacter,rule);
            var sibling=await reopened.GetQuestViewAsync(secondCharacter,rule);
            Checks.Require(durable.View is
                    { State:QuestProgressState.Completed,Stage:2,
                      ProgressRevision:2 }&&
                sibling.View is
                    { State:QuestProgressState.NotStarted,ProgressRevision:0 },
                "quest progress did not survive DB reopen or remained isolated");
        }
    }

    internal static TrustedQuestRule Rule(int revision,string source)
    {
        var stage2=new TrustedQuestStageRule(
            2,"任務已完成。",new Dictionary<int,TrustedQuestChoiceRule>());
        var stage1=new TrustedQuestStageRule(
            1,"是否完成這個已驗證步驟？",
            new Dictionary<int,TrustedQuestChoiceRule>
            {
                [2]=new(2,"完成",2,QuestProgressState.Completed)
            });
        var stage0=new TrustedQuestStageRule(
            0,"這是一個合成的 verified Quest 測試。",
            new Dictionary<int,TrustedQuestChoiceRule>
            {
                [1]=new(1,"接受",1,QuestProgressState.Active),
                [3]=new(3,"另一個合法起始選項",1,QuestProgressState.Active)
            });
        return new TrustedQuestRule(
            91001,revision,"Verified Quest Fixture",0,
            new Dictionary<int,TrustedQuestStageRule>
            {
                [0]=stage0,[1]=stage1,[2]=stage2
            },source);
    }
}
