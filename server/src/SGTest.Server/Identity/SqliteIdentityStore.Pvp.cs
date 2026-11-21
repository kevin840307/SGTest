using SGTest.Server.Pvp;
using Microsoft.Data.Sqlite;
using SGTest.Server.World;

namespace SGTest.Server.Identity;

public sealed record CharacterPvpMode(
    long CharacterId,PvpMode Mode,long UpdatedUnixMilliseconds);

public sealed record CharacterPvpState(
    long CharacterId,PvpMode Mode,bool RedMan,int RedManKillLeft,
    long PinkUntilUnixMilliseconds,long UpdatedUnixMilliseconds);

public sealed record PvpNameAdminResult(
    string EventId,CharacterPvpState State,bool Replayed);

public sealed partial class SqliteIdentityStore
{
    public async Task<CharacterPvpState> GetCharacterPvpStateAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(()=>
        {
            using var connection=Open();
            return ReadPvpState(connection,characterId);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<CharacterPvpMode> GetCharacterPvpModeAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        var state=await GetCharacterPvpStateAsync(characterId,cancellationToken)
            .ConfigureAwait(false);
        return new(state.CharacterId,state.Mode,state.UpdatedUnixMilliseconds);
    }

    public async Task<CharacterPvpMode> SetCharacterPvpModeAsync(
        long characterId,PvpMode mode,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||mode is not (PvpMode.ProtectedBlue or PvpMode.EnabledWhite))
            throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(()=>
        {
            using var connection=Open();
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var command=connection.CreateCommand();
            command.CommandText=@"INSERT INTO character_pvp_modes(
character_id,mode,red_man,red_man_kill_left,pink_until_unix_ms,updated_unix_ms)
SELECT character_id,$mode,0,0,0,$now FROM characters WHERE character_id=$id
ON CONFLICT(character_id) DO UPDATE SET mode=excluded.mode,updated_unix_ms=excluded.updated_unix_ms
RETURNING mode,updated_unix_ms;";
            command.Parameters.AddWithValue("$id",characterId);
            command.Parameters.AddWithValue("$mode",(int)mode);
            command.Parameters.AddWithValue("$now",now);
            using var reader=command.ExecuteReader();
            if(!reader.Read())throw new InvalidOperationException("Unknown character PvP state");
            return new CharacterPvpMode(characterId,(PvpMode)reader.GetInt32(0),reader.GetInt64(1));
        },cancellationToken).ConfigureAwait(false);
    }

    public Task<CharacterPvpState> MarkPvpHostileAttackAsync(
        long characterId,bool victimWasNormalWhite,DateTimeOffset now,
        CancellationToken cancellationToken=default) =>
        MutatePvpCrimeAsync(characterId,victimWasNormalWhite,kill:false,now,cancellationToken);

    public Task<CharacterPvpState> MarkPvpWhiteKillAsync(
        long characterId,bool victimWasNormalWhite,DateTimeOffset now,
        CancellationToken cancellationToken=default) =>
        MutatePvpCrimeAsync(characterId,victimWasNormalWhite,kill:true,now,cancellationToken);

    /// <summary>
    /// Local operator/admin mutation with durable idempotency and audit.
    /// No game-socket request maps to this method.
    /// </summary>
    public async Task<PvpNameAdminResult> ApplyPvpNameAdminAsync(
        long characterId,PvpNameState nameState,string operatorId,string reason,
        string eventId,DateTimeOffset now,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||(byte)nameState>4)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        operatorId=ValidateAdminText(operatorId,64,nameof(operatorId));
        reason=ValidateAdminText(reason,256,nameof(reason));
        eventId=ValidateAdminEventId(eventId);
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            using(var existing=connection.CreateCommand())
            {
                existing.Transaction=tx;
                existing.CommandText=@"SELECT operator_id,character_id,requested_state,reason,
result_mode,result_red_man,result_red_man_kill_left,result_pink_until_unix_ms,
result_updated_unix_ms FROM pvp_name_admin_events WHERE event_id=$event;";
                existing.Parameters.AddWithValue("$event",eventId);
                using var reader=existing.ExecuteReader();
                if(reader.Read())
                {
                    string priorOperator=reader.GetString(0);
                    long priorCharacter=reader.GetInt64(1);
                    int priorState=reader.GetInt32(2);
                    string priorReason=reader.GetString(3);
                    if(priorOperator!=operatorId||priorCharacter!=characterId||
                       priorState!=(int)nameState||priorReason!=reason)
                        throw new InvalidOperationException(
                            "PvP admin event ID already belongs to another command");
                    var historical=new CharacterPvpState(
                        characterId,(PvpMode)reader.GetInt32(4),reader.GetInt32(5)==1,
                        reader.GetInt32(6),reader.GetInt64(7),reader.GetInt64(8));
                    tx.Commit();
                    return new PvpNameAdminResult(eventId,historical,true);
                }
            }

            _=ReadPvpState(connection,characterId,tx);
            var desired=AdminState(nameState,now);
            UpsertPvpState(connection,tx,characterId,desired.Mode,desired.RedMan,
                desired.RedManKillLeft,desired.PinkUntilUnixMilliseconds,
                now.ToUnixTimeMilliseconds());
            var result=ReadPvpState(connection,characterId,tx);
            using(var audit=connection.CreateCommand())
            {
                audit.Transaction=tx;
                audit.CommandText=@"INSERT INTO pvp_name_admin_events(
event_id,operator_id,character_id,requested_state,reason,
result_mode,result_red_man,result_red_man_kill_left,result_pink_until_unix_ms,
result_updated_unix_ms,created_unix_ms)
VALUES($event,$operator,$character,$state,$reason,$mode,$red,$kills,$pink,$updated,$created);";
                audit.Parameters.AddWithValue("$event",eventId);
                audit.Parameters.AddWithValue("$operator",operatorId);
                audit.Parameters.AddWithValue("$character",characterId);
                audit.Parameters.AddWithValue("$state",(int)nameState);
                audit.Parameters.AddWithValue("$reason",reason);
                audit.Parameters.AddWithValue("$mode",(int)result.Mode);
                audit.Parameters.AddWithValue("$red",result.RedMan?1:0);
                audit.Parameters.AddWithValue("$kills",result.RedManKillLeft);
                audit.Parameters.AddWithValue("$pink",result.PinkUntilUnixMilliseconds);
                audit.Parameters.AddWithValue("$updated",result.UpdatedUnixMilliseconds);
                audit.Parameters.AddWithValue("$created",now.ToUnixTimeMilliseconds());
                if(audit.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("PvP admin audit insert failed");
            }
            tx.Commit();
            return new PvpNameAdminResult(eventId,result,false);
        },cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Trusted GM/server primitive. No network request maps directly here.</summary>
    public async Task<CharacterPvpState> SetCharacterPvpNameStateForGmAsync(
        long characterId,PvpNameState nameState,DateTimeOffset now,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||(byte)nameState>4)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            _=ReadPvpState(connection,characterId,tx);
            PvpMode mode=PvpMode.EnabledWhite;
            bool red=false;
            long pink=0;
            switch(nameState)
            {
                case PvpNameState.ProtectedBlue:
                    mode=PvpMode.ProtectedBlue;
                    break;
                case PvpNameState.EnabledWhite:
                    break;
                case PvpNameState.Red:
                    red=true;
                    break;
                case PvpNameState.Pink:
                    pink=now.Add(PvpCrimePolicy.HostileMarkDuration)
                        .ToUnixTimeMilliseconds();
                    break;
                case PvpNameState.PinkFlashing:
                    pink=now.Add(PvpCrimePolicy.HostileMarkFlashingWindow)
                        .ToUnixTimeMilliseconds();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(nameState));
            }
            UpsertPvpState(connection,tx,characterId,mode,red,0,pink,
                now.ToUnixTimeMilliseconds());
            var result=ReadPvpState(connection,characterId,tx);
            tx.Commit();
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    private async Task<CharacterPvpState> MutatePvpCrimeAsync(
        long characterId,bool victimWasNormalWhite,bool kill,DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            var current=ReadPvpState(connection,characterId,tx);
            if(!victimWasNormalWhite)
            {
                tx.Commit();
                return current;
            }
            if(kill)
            {
                UpsertPvpState(connection,tx,characterId,PvpMode.EnabledWhite,
                    true,current.RedManKillLeft,0,now.ToUnixTimeMilliseconds());
            }
            else if(!current.RedMan)
            {
                long pink=Math.Max(current.PinkUntilUnixMilliseconds,
                    now.Add(PvpCrimePolicy.HostileMarkDuration)
                        .ToUnixTimeMilliseconds());
                UpsertPvpState(connection,tx,characterId,PvpMode.EnabledWhite,
                    false,current.RedManKillLeft,pink,now.ToUnixTimeMilliseconds());
            }
            var result=ReadPvpState(connection,characterId,tx);
            tx.Commit();
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    private static CharacterPvpState AdminState(PvpNameState state,DateTimeOffset now)
    {
        return state switch
        {
            PvpNameState.ProtectedBlue=>new(0,PvpMode.ProtectedBlue,false,0,0,0),
            PvpNameState.EnabledWhite=>new(0,PvpMode.EnabledWhite,false,0,0,0),
            PvpNameState.Red=>new(0,PvpMode.EnabledWhite,true,0,0,0),
            PvpNameState.Pink=>new(0,PvpMode.EnabledWhite,false,0,
                now.Add(PvpCrimePolicy.HostileMarkDuration).ToUnixTimeMilliseconds(),0),
            PvpNameState.PinkFlashing=>new(0,PvpMode.EnabledWhite,false,0,
                now.Add(PvpCrimePolicy.HostileMarkFlashingWindow).ToUnixTimeMilliseconds(),0),
            _=>throw new ArgumentOutOfRangeException(nameof(state))
        };
    }

    private static string ValidateAdminText(string value,int maximum,string parameter)
    {
        if(string.IsNullOrWhiteSpace(value))throw new ArgumentException(
            "Admin audit text is required",parameter);
        value=value.Trim();
        if(value.Length>maximum||value.Any(char.IsControl))
            throw new ArgumentException("Admin audit text is invalid",parameter);
        return value;
    }

    private static string ValidateAdminEventId(string value)
    {
        value=ValidateAdminText(value,64,nameof(value));
        if(value.Any(ch=>!(char.IsAsciiLetterOrDigit(ch)||ch is '-' or '_' or '.')))
            throw new ArgumentException("Admin event ID must be ASCII token",nameof(value));
        return value;
    }

    private static CharacterPvpState ReadPvpState(
        SqliteConnection connection,long characterId,SqliteTransaction? tx=null)
    {
        using(var exists=connection.CreateCommand())
        {
            exists.Transaction=tx;
            exists.CommandText="SELECT COUNT(*) FROM characters WHERE character_id=$id;";
            exists.Parameters.AddWithValue("$id",characterId);
            if(Convert.ToInt64(exists.ExecuteScalar())!=1)
                throw new InvalidOperationException("Unknown character PvP state");
        }
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT mode,red_man,red_man_kill_left,
pink_until_unix_ms,updated_unix_ms FROM character_pvp_modes
WHERE character_id=$id;";
        command.Parameters.AddWithValue("$id",characterId);
        using var reader=command.ExecuteReader();
        if(!reader.Read())
            return new(characterId,PvpMode.ProtectedBlue,false,0,0,0);
        var mode=(PvpMode)reader.GetInt32(0);
        int red=reader.GetInt32(1);
        int kills=reader.GetInt32(2);
        long pink=reader.GetInt64(3);
        long updated=reader.GetInt64(4);
        if(mode is not (PvpMode.ProtectedBlue or PvpMode.EnabledWhite)||
           red is <0 or >1||kills<0||pink<0)
            throw new InvalidDataException("Invalid persisted PvP state");
        return new(characterId,mode,red==1,kills,pink,updated);
    }

    private static void UpsertPvpState(
        SqliteConnection connection,SqliteTransaction tx,long characterId,
        PvpMode mode,bool redMan,int redManKillLeft,long pinkUntil,long updated)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"INSERT INTO character_pvp_modes(
character_id,mode,red_man,red_man_kill_left,pink_until_unix_ms,updated_unix_ms)
VALUES($id,$mode,$red,$kills,$pink,$updated)
ON CONFLICT(character_id) DO UPDATE SET
 mode=excluded.mode,red_man=excluded.red_man,
 red_man_kill_left=excluded.red_man_kill_left,
 pink_until_unix_ms=excluded.pink_until_unix_ms,
 updated_unix_ms=excluded.updated_unix_ms;";
        command.Parameters.AddWithValue("$id",characterId);
        command.Parameters.AddWithValue("$mode",(int)mode);
        command.Parameters.AddWithValue("$red",redMan?1:0);
        command.Parameters.AddWithValue("$kills",redManKillLeft);
        command.Parameters.AddWithValue("$pink",pinkUntil);
        command.Parameters.AddWithValue("$updated",updated);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("PvP state update failed");
    }
}
