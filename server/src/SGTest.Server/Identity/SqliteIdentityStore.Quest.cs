using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum QuestProgressState : byte
{
    NotStarted=0,
    Active=1,
    Completed=2
}

public enum QuestAccessStatus : byte
{
    Allowed=0,
    StaleGeneration=1,
    UnknownQuest=2,
    TooFar=3,
    PlayerDead=4
}

public enum QuestViewStatus : byte
{
    Ready=0,
    RuleRevisionMismatch=1
}

public enum QuestChoiceStatus : byte
{
    Applied=0,
    AlreadyApplied=1,
    UnknownQuest=2,
    TooFar=3,
    InvalidChoice=4,
    AlreadyCompleted=5,
    StaleSequence=6,
    StaleGeneration=7,
    PlayerDead=8,
    RuleRevisionMismatch=9
}

public readonly record struct TrustedQuestContext(
    long CharacterId,long ConnectionGeneration,long MapEpoch,uint ActionSequence);

public sealed record TrustedQuestChoiceRule(
    int ChoiceId,string Label,int NextStage,QuestProgressState NextState);

public sealed record TrustedQuestStageRule(
    int StageId,string Text,IReadOnlyDictionary<int,TrustedQuestChoiceRule> Choices);

public sealed record TrustedQuestRule(
    int QuestId,int RuleRevision,string Title,int StartStage,
    IReadOnlyDictionary<int,TrustedQuestStageRule> Stages,string SourceReference);

public sealed record QuestView(
    int QuestId,int RuleRevision,QuestProgressState State,int Stage,
    int ProgressRevision,string Title,string Text,
    IReadOnlyList<TrustedQuestChoiceRule> Choices);

public sealed record QuestViewResult(QuestViewStatus Status,QuestView? View);

public sealed record QuestChoiceResult(
    QuestChoiceStatus Status,int NpcSpawnId,int QuestId,int ChoiceId,
    QuestProgressState State,int Stage,int ProgressRevision);

public sealed partial class SqliteIdentityStore
{
    public async Task<QuestViewResult> GetQuestViewAsync(
        long characterId,TrustedQuestRule rule,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ValidateQuestRule(rule);
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            var progress=ReadQuestProgress(connection,tx,characterId,rule);
            if(progress.RuleMismatch)
            {
                tx.Commit();
                return new QuestViewResult(QuestViewStatus.RuleRevisionMismatch,null);
            }
            var view=BuildQuestView(rule,progress.State,progress.Stage,
                progress.ProgressRevision);
            tx.Commit();
            return new QuestViewResult(QuestViewStatus.Ready,view);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<QuestChoiceResult> ApplyQuestChoiceAsync(
        TrustedQuestContext context,QuestAccessStatus access,int npcSpawnId,
        TrustedQuestRule rule,int choiceId,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ValidateQuestContext(context,npcSpawnId,rule,choiceId);
        return await Task.Run<QuestChoiceResult>(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"
SELECT npc_spawn_id,quest_id,rule_revision,choice_id,
       state_after,stage_after,progress_revision_after,source_reference
FROM quest_actions
WHERE character_id=$actor AND connection_generation=$conn
  AND map_epoch=$epoch AND action_sequence=$seq;";
                replay.Parameters.AddWithValue("$actor",context.CharacterId);
                replay.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
                replay.Parameters.AddWithValue("$epoch",context.MapEpoch);
                replay.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    int originalNpc=reader.GetInt32(0);
                    int originalQuest=reader.GetInt32(1);
                    int originalRule=reader.GetInt32(2);
                    int originalChoice=reader.GetInt32(3);
                    var state=(QuestProgressState)reader.GetInt32(4);
                    int stage=reader.GetInt32(5);
                    int revision=reader.GetInt32(6);
                    string source=reader.GetString(7);
                    bool exact=originalNpc==npcSpawnId&&
                        originalQuest==rule.QuestId&&
                        originalRule==rule.RuleRevision&&
                        originalChoice==choiceId&&
                        source==rule.SourceReference;
                    if(!exact)
                        return RejectQuestChoice(
                            QuestChoiceStatus.StaleSequence,npcSpawnId,
                            rule.QuestId,choiceId);
                    return new(QuestChoiceStatus.AlreadyApplied,originalNpc,
                        originalQuest,originalChoice,state,stage,revision);
                }
            }

            long last=ScalarLong(connection,@"
SELECT COALESCE(MAX(action_sequence),0) FROM quest_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;",tx,
                ("$actor",context.CharacterId),
                ("$conn",context.ConnectionGeneration),
                ("$epoch",context.MapEpoch));
            if(last>=context.ActionSequence)
                return RejectQuestChoice(
                    QuestChoiceStatus.StaleSequence,npcSpawnId,
                    rule.QuestId,choiceId);

            var accessFailure=QuestAccessFailure(access);
            if(accessFailure is not null)
                return RejectQuestChoice(
                    accessFailure.Value,npcSpawnId,rule.QuestId,choiceId);

            var progress=ReadQuestProgress(connection,tx,context.CharacterId,rule);
            if(progress.RuleMismatch)
                return new(QuestChoiceStatus.RuleRevisionMismatch,npcSpawnId,
                    rule.QuestId,choiceId,progress.State,progress.Stage,
                    progress.ProgressRevision);
            if(progress.State==QuestProgressState.Completed)
                return new(QuestChoiceStatus.AlreadyCompleted,npcSpawnId,
                    rule.QuestId,choiceId,progress.State,progress.Stage,
                    progress.ProgressRevision);

            if(!rule.Stages.TryGetValue(progress.Stage,out var stageRule)||
               !stageRule.Choices.TryGetValue(choiceId,out var choice))
                return new(QuestChoiceStatus.InvalidChoice,npcSpawnId,
                    rule.QuestId,choiceId,progress.State,progress.Stage,
                    progress.ProgressRevision);

            int nextRevision=checked(progress.ProgressRevision+1);
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(progress.State==QuestProgressState.NotStarted)
            {
                using var insert=connection.CreateCommand();
                insert.Transaction=tx;
                insert.CommandText=@"
INSERT INTO quest_progress(
    character_id,quest_id,rule_revision,stage,state,progress_revision,
    source_reference,created_unix_ms,updated_unix_ms)
VALUES($actor,$quest,$rule,$stage,$state,$revision,$source,$now,$now);";
                insert.Parameters.AddWithValue("$actor",context.CharacterId);
                insert.Parameters.AddWithValue("$quest",rule.QuestId);
                insert.Parameters.AddWithValue("$rule",rule.RuleRevision);
                insert.Parameters.AddWithValue("$stage",choice.NextStage);
                insert.Parameters.AddWithValue("$state",(int)choice.NextState);
                insert.Parameters.AddWithValue("$revision",nextRevision);
                insert.Parameters.AddWithValue("$source",rule.SourceReference);
                insert.Parameters.AddWithValue("$now",now);
                if(insert.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("Quest progress insert failed");
            }
            else
            {
                using var update=connection.CreateCommand();
                update.Transaction=tx;
                update.CommandText=@"
UPDATE quest_progress
SET stage=$stage,state=$state,progress_revision=$next,updated_unix_ms=$now
WHERE character_id=$actor AND quest_id=$quest
  AND rule_revision=$rule AND progress_revision=$previous
  AND source_reference=$source;";
                update.Parameters.AddWithValue("$stage",choice.NextStage);
                update.Parameters.AddWithValue("$state",(int)choice.NextState);
                update.Parameters.AddWithValue("$next",nextRevision);
                update.Parameters.AddWithValue("$now",now);
                update.Parameters.AddWithValue("$actor",context.CharacterId);
                update.Parameters.AddWithValue("$quest",rule.QuestId);
                update.Parameters.AddWithValue("$rule",rule.RuleRevision);
                update.Parameters.AddWithValue("$previous",progress.ProgressRevision);
                update.Parameters.AddWithValue("$source",rule.SourceReference);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Quest progress changed during transaction");
            }

            using(var receipt=connection.CreateCommand())
            {
                receipt.Transaction=tx;
                receipt.CommandText=@"
INSERT INTO quest_actions(
    character_id,connection_generation,map_epoch,action_sequence,npc_spawn_id,
    quest_id,rule_revision,choice_id,state_before,stage_before,
    state_after,stage_after,progress_revision_after,source_reference,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$npc,$quest,$rule,$choice,
       $state_before,$stage_before,$state_after,$stage_after,$revision,$source,$now);";
                receipt.Parameters.AddWithValue("$actor",context.CharacterId);
                receipt.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
                receipt.Parameters.AddWithValue("$epoch",context.MapEpoch);
                receipt.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
                receipt.Parameters.AddWithValue("$npc",npcSpawnId);
                receipt.Parameters.AddWithValue("$quest",rule.QuestId);
                receipt.Parameters.AddWithValue("$rule",rule.RuleRevision);
                receipt.Parameters.AddWithValue("$choice",choiceId);
                receipt.Parameters.AddWithValue("$state_before",(int)progress.State);
                receipt.Parameters.AddWithValue("$stage_before",progress.Stage);
                receipt.Parameters.AddWithValue("$state_after",(int)choice.NextState);
                receipt.Parameters.AddWithValue("$stage_after",choice.NextStage);
                receipt.Parameters.AddWithValue("$revision",nextRevision);
                receipt.Parameters.AddWithValue("$source",rule.SourceReference);
                receipt.Parameters.AddWithValue("$now",now);
                if(receipt.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("Quest action receipt insert failed");
            }

            tx.Commit();
            return new(QuestChoiceStatus.Applied,npcSpawnId,rule.QuestId,
                choiceId,choice.NextState,choice.NextStage,nextRevision);
        },cancellationToken).ConfigureAwait(false);
    }

    private static QuestView BuildQuestView(
        TrustedQuestRule rule,QuestProgressState state,int stage,int revision)
    {
        if(!rule.Stages.TryGetValue(stage,out var stageRule))
            throw new InvalidDataException("Quest progress references missing stage");
        IReadOnlyList<TrustedQuestChoiceRule> choices=
            state==QuestProgressState.Completed
                ? Array.Empty<TrustedQuestChoiceRule>()
                : stageRule.Choices.Values.OrderBy(x=>x.ChoiceId).ToArray();
        return new(rule.QuestId,rule.RuleRevision,state,stage,revision,
            rule.Title,stageRule.Text,choices);
    }

    private static (
        QuestProgressState State,int Stage,int ProgressRevision,bool RuleMismatch)
        ReadQuestProgress(
            SqliteConnection connection,SqliteTransaction tx,long characterId,
            TrustedQuestRule rule)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"
SELECT rule_revision,stage,state,progress_revision,source_reference
FROM quest_progress WHERE character_id=$actor AND quest_id=$quest;";
        command.Parameters.AddWithValue("$actor",characterId);
        command.Parameters.AddWithValue("$quest",rule.QuestId);
        using var reader=command.ExecuteReader();
        if(!reader.Read())
            return(QuestProgressState.NotStarted,rule.StartStage,0,false);
        int revision=reader.GetInt32(0);
        int stage=reader.GetInt32(1);
        var state=(QuestProgressState)reader.GetInt32(2);
        int progressRevision=reader.GetInt32(3);
        string source=reader.GetString(4);
        bool mismatch=revision!=rule.RuleRevision||source!=rule.SourceReference||
            !rule.Stages.ContainsKey(stage);
        return(state,stage,progressRevision,mismatch);
    }

    private static QuestChoiceStatus? QuestAccessFailure(QuestAccessStatus access)=>
        access switch
        {
            QuestAccessStatus.Allowed=>null,
            QuestAccessStatus.StaleGeneration=>QuestChoiceStatus.StaleGeneration,
            QuestAccessStatus.UnknownQuest=>QuestChoiceStatus.UnknownQuest,
            QuestAccessStatus.TooFar=>QuestChoiceStatus.TooFar,
            QuestAccessStatus.PlayerDead=>QuestChoiceStatus.PlayerDead,
            _=>QuestChoiceStatus.StaleGeneration
        };

    private static QuestChoiceResult RejectQuestChoice(
        QuestChoiceStatus status,int npcSpawnId,int questId,int choiceId)=>
        new(status,npcSpawnId,questId,choiceId,
            QuestProgressState.NotStarted,0,0);

    private static void ValidateQuestContext(
        TrustedQuestContext context,int npcSpawnId,TrustedQuestRule rule,int choiceId)
    {
        if(context.CharacterId<=0||context.ConnectionGeneration<=0||
           context.MapEpoch<=0||context.ActionSequence==0)
            throw new ArgumentOutOfRangeException(nameof(context));
        if(npcSpawnId<=0)throw new ArgumentOutOfRangeException(nameof(npcSpawnId));
        if(choiceId<=0)throw new ArgumentOutOfRangeException(nameof(choiceId));
        ValidateQuestRule(rule);
    }

    internal static void ValidateQuestRule(TrustedQuestRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if(rule.QuestId<=0||rule.QuestId>100000000||
           rule.RuleRevision<=0||rule.StartStage<0||rule.StartStage>1000000||
           string.IsNullOrWhiteSpace(rule.Title)||rule.Title.Length>128||
           string.IsNullOrWhiteSpace(rule.SourceReference)||
           rule.SourceReference.Length>512||
           rule.Stages.Count is <1 or >64||
           !rule.Stages.ContainsKey(rule.StartStage))
            throw new ArgumentException("Malformed trusted quest rule",nameof(rule));

        foreach(var pair in rule.Stages)
        {
            var stage=pair.Value;
            if(pair.Key!=stage.StageId||stage.StageId<0||stage.StageId>1000000||
               string.IsNullOrWhiteSpace(stage.Text)||stage.Text.Length>2048||
               stage.Choices.Count>16)
                throw new ArgumentException("Malformed trusted quest stage",nameof(rule));
            foreach(var choicePair in stage.Choices)
            {
                var choice=choicePair.Value;
                if(choicePair.Key!=choice.ChoiceId||choice.ChoiceId<=0||
                   choice.ChoiceId>100000000||
                   string.IsNullOrWhiteSpace(choice.Label)||choice.Label.Length>256||
                   choice.NextState is not
                       (QuestProgressState.Active or QuestProgressState.Completed)||
                   !rule.Stages.ContainsKey(choice.NextStage))
                    throw new ArgumentException("Malformed trusted quest choice",nameof(rule));
                if(choice.NextState==QuestProgressState.Completed&&
                   rule.Stages[choice.NextStage].Choices.Count!=0)
                    throw new ArgumentException(
                        "Completed quest stage must be terminal",nameof(rule));
            }
        }
    }
}
