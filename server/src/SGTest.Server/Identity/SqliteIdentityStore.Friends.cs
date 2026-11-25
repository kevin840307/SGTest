using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum FriendStoreStatus : byte
{
    Success=0,TargetNotFound=1,CannotTargetSelf=2,
    AlreadyFriends=3,NotFriends=4,Conflict=5
}

public sealed record FriendRecord(long CharacterId,string Name);

public sealed partial class SqliteIdentityStore
{
    public async Task<IReadOnlyList<FriendRecord>> GetFriendsAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)return Array.Empty<FriendRecord>();
        return await Task.Run<IReadOnlyList<FriendRecord>>(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT
CASE WHEN f.character_low_id=$id THEN f.character_high_id ELSE f.character_low_id END AS friend_id,
c.name
FROM character_friendships f
JOIN characters c ON c.character_id=
    CASE WHEN f.character_low_id=$id THEN f.character_high_id ELSE f.character_low_id END
WHERE f.character_low_id=$id OR f.character_high_id=$id
ORDER BY c.name,friend_id;";
            command.Parameters.AddWithValue("$id",characterId);
            using var reader=command.ExecuteReader();
            var result=new List<FriendRecord>();
            while(reader.Read())
                result.Add(new(reader.GetInt64(0),reader.GetString(1)));
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<(FriendStoreStatus Status,long TargetCharacterId,string TargetName)> ResolveFriendInviteAsync(
        long characterId,long targetCharacterId,string targetName="",
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)return(FriendStoreStatus.Conflict,0,string.Empty);
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            long target=ResolveFriendTarget(connection,tx,targetCharacterId,targetName);
            if(target<=0)return(FriendStoreStatus.TargetNotFound,0,string.Empty);
            if(target==characterId)return(FriendStoreStatus.CannotTargetSelf,target,string.Empty);
            if(!FriendCharacterExists(connection,tx,characterId))
                return(FriendStoreStatus.Conflict,0,string.Empty);
            long low=Math.Min(characterId,target);
            long high=Math.Max(characterId,target);
            using(var exists=connection.CreateCommand())
            {
                exists.Transaction=tx;
                exists.CommandText=@"SELECT 1 FROM character_friendships
WHERE character_low_id=$low AND character_high_id=$high LIMIT 1;";
                exists.Parameters.AddWithValue("$low",low);
                exists.Parameters.AddWithValue("$high",high);
                if(exists.ExecuteScalar() is not null)
                    return(FriendStoreStatus.AlreadyFriends,target,string.Empty);
            }
            using var name=connection.CreateCommand();
            name.Transaction=tx;
            name.CommandText="SELECT name FROM characters WHERE character_id=$id LIMIT 1;";
            name.Parameters.AddWithValue("$id",target);
            string display=Convert.ToString(name.ExecuteScalar())??string.Empty;
            return string.IsNullOrEmpty(display)
                ?(FriendStoreStatus.TargetNotFound,0,string.Empty)
                :(FriendStoreStatus.Success,target,display);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<(FriendStoreStatus Status,long TargetCharacterId)> AddFriendAsync(
        long characterId,long targetCharacterId,string targetName="",
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)return(FriendStoreStatus.Conflict,0);
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            long target=ResolveFriendTarget(connection,tx,targetCharacterId,targetName);
            if(target<=0)return(FriendStoreStatus.TargetNotFound,0);
            if(target==characterId)return(FriendStoreStatus.CannotTargetSelf,target);
            if(!FriendCharacterExists(connection,tx,characterId))
                return(FriendStoreStatus.Conflict,0);
            long low=Math.Min(characterId,target);
            long high=Math.Max(characterId,target);
            using(var exists=connection.CreateCommand())
            {
                exists.Transaction=tx;
                exists.CommandText=@"SELECT 1 FROM character_friendships
WHERE character_low_id=$low AND character_high_id=$high LIMIT 1;";
                exists.Parameters.AddWithValue("$low",low);
                exists.Parameters.AddWithValue("$high",high);
                if(exists.ExecuteScalar() is not null)
                    return(FriendStoreStatus.AlreadyFriends,target);
            }
            using(var insert=connection.CreateCommand())
            {
                insert.Transaction=tx;
                insert.CommandText=@"INSERT INTO character_friendships(
character_low_id,character_high_id,created_unix_ms)
VALUES($low,$high,$now);";
                insert.Parameters.AddWithValue("$low",low);
                insert.Parameters.AddWithValue("$high",high);
                insert.Parameters.AddWithValue(
                    "$now",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                if(insert.ExecuteNonQuery()!=1)
                    return(FriendStoreStatus.Conflict,target);
            }
            tx.Commit();
            return(FriendStoreStatus.Success,target);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<FriendStoreStatus> RemoveFriendAsync(
        long characterId,long targetCharacterId,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||targetCharacterId<=0)
            return FriendStoreStatus.NotFriends;
        if(characterId==targetCharacterId)
            return FriendStoreStatus.CannotTargetSelf;
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            long low=Math.Min(characterId,targetCharacterId);
            long high=Math.Max(characterId,targetCharacterId);
            using var delete=connection.CreateCommand();
            delete.Transaction=tx;
            delete.CommandText=@"DELETE FROM character_friendships
WHERE character_low_id=$low AND character_high_id=$high;";
            delete.Parameters.AddWithValue("$low",low);
            delete.Parameters.AddWithValue("$high",high);
            int changed=delete.ExecuteNonQuery();
            if(changed!=1)return FriendStoreStatus.NotFriends;
            tx.Commit();
            return FriendStoreStatus.Success;
        },cancellationToken).ConfigureAwait(false);
    }

    private static long ResolveFriendTarget(
        SqliteConnection connection,SqliteTransaction tx,long targetCharacterId,string targetName)
    {
        if(targetCharacterId>0)
        {
            if(!string.IsNullOrEmpty(targetName))return 0;
            return FriendCharacterExists(connection,tx,targetCharacterId)?targetCharacterId:0;
        }
        if(string.IsNullOrWhiteSpace(targetName))return 0;
        (string Display,string Key) normalized;
        try{normalized=NormalizeCharacterName(targetName);}
        catch(ArgumentException){return 0;}
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="SELECT character_id FROM characters WHERE name_key=$key LIMIT 1;";
        command.Parameters.AddWithValue("$key",normalized.Key);
        object? value=command.ExecuteScalar();
        return value is null?0:Convert.ToInt64(value);
    }

    private static bool FriendCharacterExists(
        SqliteConnection connection,SqliteTransaction tx,long characterId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="SELECT 1 FROM characters WHERE character_id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id",characterId);
        return command.ExecuteScalar() is not null;
    }
}
