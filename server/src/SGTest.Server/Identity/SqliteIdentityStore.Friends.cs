using System.Text;
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
COALESCE(a.alias,c.name)
FROM character_friendships f
JOIN characters c ON c.character_id=
    CASE WHEN f.character_low_id=$id THEN f.character_high_id ELSE f.character_low_id END
LEFT JOIN character_friend_aliases a
    ON a.owner_character_id=$id AND a.target_character_id=c.character_id
WHERE (f.character_low_id=$id OR f.character_high_id=$id)
AND NOT EXISTS (
    SELECT 1 FROM character_ignores i
    WHERE i.owner_character_id=$id AND i.target_character_id=
        CASE WHEN f.character_low_id=$id THEN f.character_high_id ELSE f.character_low_id END
)
ORDER BY COALESCE(a.alias,c.name),friend_id;";
            command.Parameters.AddWithValue("$id",characterId);
            using var reader=command.ExecuteReader();
            var result=new List<FriendRecord>();
            while(reader.Read())
                result.Add(new(reader.GetInt64(0),reader.GetString(1)));
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FriendRecord>> GetFriendRelationsAsync(
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
            using(var clearIgnore=connection.CreateCommand())
            {
                clearIgnore.Transaction=tx;
                clearIgnore.CommandText=@"DELETE FROM character_ignores
WHERE (owner_character_id=$a AND target_character_id=$b)
   OR (owner_character_id=$b AND target_character_id=$a);";
                clearIgnore.Parameters.AddWithValue("$a",characterId);
                clearIgnore.Parameters.AddWithValue("$b",targetCharacterId);
                clearIgnore.ExecuteNonQuery();
            }
            using(var clearAliases=connection.CreateCommand())
            {
                clearAliases.Transaction=tx;
                clearAliases.CommandText=@"DELETE FROM character_friend_aliases
WHERE (owner_character_id=$a AND target_character_id=$b)
   OR (owner_character_id=$b AND target_character_id=$a);";
                clearAliases.Parameters.AddWithValue("$a",characterId);
                clearAliases.Parameters.AddWithValue("$b",targetCharacterId);
                clearAliases.ExecuteNonQuery();
            }
            tx.Commit();
            return FriendStoreStatus.Success;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<FriendStoreStatus> RenameFriendAsync(
        long characterId,long targetCharacterId,string alias,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||targetCharacterId<=0)
            return FriendStoreStatus.TargetNotFound;
        if(characterId==targetCharacterId)
            return FriendStoreStatus.CannotTargetSelf;
        string clean=(alias??string.Empty).Normalize(NormalizationForm.FormC);
        if(clean!=alias||clean!=clean.Trim()||clean.Length is <1 or >10||clean.Any(char.IsControl))
            return FriendStoreStatus.Conflict;
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            if(!FriendshipExists(connection,tx,characterId,targetCharacterId))
                return FriendStoreStatus.NotFriends;
            using var command=connection.CreateCommand();
            command.Transaction=tx;
            command.CommandText=@"INSERT INTO character_friend_aliases(
owner_character_id,target_character_id,alias,updated_unix_ms)
VALUES($owner,$target,$alias,$now)
ON CONFLICT(owner_character_id,target_character_id)
DO UPDATE SET alias=excluded.alias,updated_unix_ms=excluded.updated_unix_ms;";
            command.Parameters.AddWithValue("$owner",characterId);
            command.Parameters.AddWithValue("$target",targetCharacterId);
            command.Parameters.AddWithValue("$alias",clean);
            command.Parameters.AddWithValue(
                "$now",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            if(command.ExecuteNonQuery()!=1)
                return FriendStoreStatus.Conflict;
            tx.Commit();
            return FriendStoreStatus.Success;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FriendRecord>> GetIgnoredFriendsAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)return Array.Empty<FriendRecord>();
        return await Task.Run<IReadOnlyList<FriendRecord>>(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT i.target_character_id,COALESCE(a.alias,c.name)
FROM character_ignores i
JOIN characters c ON c.character_id=i.target_character_id
LEFT JOIN character_friend_aliases a
    ON a.owner_character_id=$id AND a.target_character_id=c.character_id
WHERE i.owner_character_id=$id
ORDER BY COALESCE(a.alias,c.name),i.target_character_id;";
            command.Parameters.AddWithValue("$id",characterId);
            using var reader=command.ExecuteReader();
            var result=new List<FriendRecord>();
            while(reader.Read())
                result.Add(new(reader.GetInt64(0),reader.GetString(1)));
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<FriendStoreStatus> IgnoreFriendAsync(
        long characterId,long targetCharacterId,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||targetCharacterId<=0)
            return FriendStoreStatus.TargetNotFound;
        if(characterId==targetCharacterId)
            return FriendStoreStatus.CannotTargetSelf;
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            if(!FriendCharacterExists(connection,tx,characterId)||
               !FriendCharacterExists(connection,tx,targetCharacterId))
                return FriendStoreStatus.TargetNotFound;
            if(!FriendshipExists(connection,tx,characterId,targetCharacterId))
                return FriendStoreStatus.NotFriends;
            using(var existing=connection.CreateCommand())
            {
                existing.Transaction=tx;
                existing.CommandText=@"SELECT 1 FROM character_ignores
WHERE owner_character_id=$owner AND target_character_id=$target LIMIT 1;";
                existing.Parameters.AddWithValue("$owner",characterId);
                existing.Parameters.AddWithValue("$target",targetCharacterId);
                if(existing.ExecuteScalar() is not null)
                    return FriendStoreStatus.Conflict;
            }
            using(var insert=connection.CreateCommand())
            {
                insert.Transaction=tx;
                insert.CommandText=@"INSERT INTO character_ignores(
owner_character_id,target_character_id,created_unix_ms)
VALUES($owner,$target,$now);";
                insert.Parameters.AddWithValue("$owner",characterId);
                insert.Parameters.AddWithValue("$target",targetCharacterId);
                insert.Parameters.AddWithValue(
                    "$now",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                if(insert.ExecuteNonQuery()!=1)
                    return FriendStoreStatus.Conflict;
            }
            tx.Commit();
            return FriendStoreStatus.Success;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<FriendStoreStatus> RestoreIgnoredFriendAsync(
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
            if(!FriendshipExists(connection,tx,characterId,targetCharacterId))
                return FriendStoreStatus.NotFriends;
            using var delete=connection.CreateCommand();
            delete.Transaction=tx;
            delete.CommandText=@"DELETE FROM character_ignores
WHERE owner_character_id=$owner AND target_character_id=$target;";
            delete.Parameters.AddWithValue("$owner",characterId);
            delete.Parameters.AddWithValue("$target",targetCharacterId);
            if(delete.ExecuteNonQuery()!=1)
                return FriendStoreStatus.NotFriends;
            tx.Commit();
            return FriendStoreStatus.Success;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsIgnoredAsync(
        long ownerCharacterId,long targetCharacterId,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(ownerCharacterId<=0||targetCharacterId<=0||ownerCharacterId==targetCharacterId)
            return false;
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT 1 FROM character_ignores
WHERE owner_character_id=$owner AND target_character_id=$target LIMIT 1;";
            command.Parameters.AddWithValue("$owner",ownerCharacterId);
            command.Parameters.AddWithValue("$target",targetCharacterId);
            return command.ExecuteScalar() is not null;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsFriendAsync(
        long characterId,long targetCharacterId,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||targetCharacterId<=0||characterId==targetCharacterId)
            return false;
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            long low=Math.Min(characterId,targetCharacterId);
            long high=Math.Max(characterId,targetCharacterId);
            command.CommandText=@"SELECT 1 FROM character_friendships
WHERE character_low_id=$low AND character_high_id=$high LIMIT 1;";
            command.Parameters.AddWithValue("$low",low);
            command.Parameters.AddWithValue("$high",high);
            return command.ExecuteScalar() is not null;
        },cancellationToken).ConfigureAwait(false);
    }

    private static bool FriendshipExists(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long targetCharacterId)
    {
        long low=Math.Min(characterId,targetCharacterId);
        long high=Math.Max(characterId,targetCharacterId);
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT 1 FROM character_friendships
WHERE character_low_id=$low AND character_high_id=$high LIMIT 1;";
        command.Parameters.AddWithValue("$low",low);
        command.Parameters.AddWithValue("$high",high);
        return command.ExecuteScalar() is not null;
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
