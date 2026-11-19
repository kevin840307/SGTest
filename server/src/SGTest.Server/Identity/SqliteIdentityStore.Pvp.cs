using Microsoft.Data.Sqlite;
using SGTest.Server.World;

namespace SGTest.Server.Identity;

public sealed record CharacterPvpMode(
    long CharacterId,PvpMode Mode,long UpdatedUnixMilliseconds);

public sealed record CharacterPvpState(
    long CharacterId,PvpMode Mode,bool RedMan,int RedManKillLeft,
    long PinkUntilUnixMilliseconds,long UpdatedUnixMilliseconds);

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
                    pink=now.AddSeconds(PvpCrimePolicy.PinkDurationSeconds)
                        .ToUnixTimeMilliseconds();
                    break;
                case PvpNameState.PinkFlashing:
                    pink=now.AddSeconds(PvpCrimePolicy.PinkFlashSeconds)
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
                    now.AddSeconds(PvpCrimePolicy.PinkDurationSeconds)
                        .ToUnixTimeMilliseconds());
                UpsertPvpState(connection,tx,characterId,PvpMode.EnabledWhite,
                    false,current.RedManKillLeft,pink,now.ToUnixTimeMilliseconds());
            }
            var result=ReadPvpState(connection,characterId,tx);
            tx.Commit();
            return result;
        },cancellationToken).ConfigureAwait(false);
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
