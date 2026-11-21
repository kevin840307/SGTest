using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Combat;
using System.Threading.Channels;
using SGTest.Server.World;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Gateway;

/// <summary>
/// Authenticated gameplay request admission and visual event staging.
/// Shares the one WorldResultRouter channel registry and binding gate.
/// </summary>
public sealed partial class WorldResultRouter
{
    public bool TryRequestNormalAttack(TrustedNormalAttack request)
    {
        if (request.ActorId <= 0 || request.ConnectionEpoch <= 0 ||
            request.RequestMapEpoch <= 0 || request.AttackSequence == 0 ||
            request.TargetEntityId <= 0)
            return false;
        lock (_gate)
        {
            if (!_connections.TryGetValue(request.ActorId, out var connection) ||
                connection.IsClosed ||
                connection.Binding.ActorId != request.ActorId ||
                connection.Binding.ConnectionEpoch != request.ConnectionEpoch)
                return false;
        }
        return _attacks.Writer.TryWrite(request);
    }

    public bool TryRequestSkill(TrustedSkillUse request)
    {
        if(request.ActorId<=0 || request.ConnectionEpoch<=0 ||
            request.RequestMapEpoch<=0 || request.SkillSequence==0 ||
            request.SkillId<=0 || request.TargetEntityId<0)
            return false;
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed ||
                connection.Binding.ActorId!=request.ActorId ||
                connection.Binding.ConnectionEpoch!=request.ConnectionEpoch)
                return false;
        }
        return _skills.Writer.TryWrite(request);
    }

    public async Task<MonsterWorldCoordinator.SkillPrepareResult> PrepareSkillAsync(
        TrustedSkillUse request,CancellationToken cancellationToken=default)
    {
        if(request.ActorId<=0 || request.ConnectionEpoch<=0 ||
            request.RequestMapEpoch<=0 || request.SkillSequence==0 ||
            request.SkillId<=0 || request.TargetEntityId<0)
            throw new ArgumentOutOfRangeException(nameof(request));

        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed ||
                connection.Binding!=new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch))
                return new(null,new SkillUseOutcome(
                    SkillUseStatus.OldMapEpoch,0,request.SkillId,
                    request.TargetEntityId,0,0,0,0));
        }

        var completion=new TaskCompletionSource<MonsterWorldCoordinator.SkillPrepareResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await _skillPreflights.Writer.WriteAsync(
            new(request,completion),cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SkillUseOutcome> FinishPreparedSkillAsync(
        MonsterWorldCoordinator.PreparedSkillUse prepared,
        SkillUseStatus? cancelStatus=null,
        CancellationToken cancellationToken=default)
    {
        if(cancelStatus is SkillUseStatus.Applied or SkillUseStatus.Miss)
            throw new ArgumentOutOfRangeException(nameof(cancelStatus));
        var completion=new TaskCompletionSource<SkillUseOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await _preparedSkills.Writer.WriteAsync(
            new(prepared,cancelStatus,true,completion),cancellationToken)
            .ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SkillUseOutcome> FinishPreparedPlayerSkillAsync(
        MonsterWorldCoordinator.PreparedSkillUse prepared,
        SkillUseStatus? cancelStatus=null,
        CancellationToken cancellationToken=default)
    {
        if(cancelStatus is SkillUseStatus.Applied or SkillUseStatus.Miss)
            throw new ArgumentOutOfRangeException(nameof(cancelStatus));
        var completion=new TaskCompletionSource<SkillUseOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await _preparedSkills.Writer.WriteAsync(
            new(prepared,cancelStatus,false,completion),cancellationToken)
            .ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryPostPlayerDamageVisual(PlayerDamageSettlement result)
    {
        if(result.ActorId<=0 || result.ConnectionEpoch<=0 || result.MapEpoch<=0 ||
            result.DamageSequence==0 || result.Damage<=0)
            return false;
        return _playerDamageVisuals.Writer.TryWrite(result);
    }

    public bool TryPostPlayerPvpCombatVisual(PlayerDamageSettlement result)
    {
        if(result.SourceKind!=PlayerDamageSourceKind.PlayerPvp||
           result.ActorId<=0||result.ConnectionEpoch<=0||result.MapEpoch<=0||
           result.DamageSequence==0||result.AttackerActorId<=0||
           result.AttackerConnectionEpoch<=0||result.AttackerAttackSequence==0||
           result.Damage<=0||result.AttackerFacing is <1 or >8||
           result.VictimFacing is <1 or >8)
            return false;
        return _playerPvpCombatVisuals.Writer.TryWrite(result);
    }

    public bool TryPostPlayerPvpSkillVisual(PlayerDamageSettlement result)
    {
        if(result.SourceKind!=PlayerDamageSourceKind.PlayerSkill||
           result.ActorId<=0||result.ConnectionEpoch<=0||result.MapEpoch<=0||
           result.DamageSequence==0||result.AttackerActorId<=0||
           result.AttackerConnectionEpoch<=0||result.AttackerSkillSequence==0||
           result.AttackerSkillId is <1 or >100_000_000||
           result.Damage<=0||result.AttackerFacing is <1 or >8||
           result.VictimFacing is <1 or >8)
            return false;
        return _playerPvpSkillVisuals.Writer.TryWrite(result);
    }

    public bool TryPostPlayerRespawnVisual(
        ActorSnapshot actor,uint respawnSequence)
    {
        if(actor.ActorId<=0 || actor.ConnectionEpoch<=0 || actor.MapEpoch<=0 ||
            respawnSequence==0)
            return false;
        return _playerRespawnVisuals.Writer.TryWrite(
            new(actor,respawnSequence));
    }

    public bool TryPostGroundDropClaimed(long dropId) =>
        dropId>0 && _claimedGroundDrops.Writer.TryWrite(dropId);

    public bool TryRequestResync(TrustedWorldResyncRequest request)
    {
        if (request.ActorId <= 0 || request.ConnectionEpoch <= 0 || request.MapEpoch <= 0)
            return false;
        lock (_gate)
        {
            if (!_connections.TryGetValue(request.ActorId, out var connection) ||
                connection.IsClosed ||
                connection.Binding != new ConnectionBinding(
                    request.ActorId, request.ConnectionEpoch, request.MapEpoch))
                return false;
        }
        return _resync.Writer.TryWrite(request);
    }
}
