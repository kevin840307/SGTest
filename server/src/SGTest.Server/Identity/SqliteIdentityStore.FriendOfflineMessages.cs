using System.Text;

namespace SGTest.Server.Identity;

public enum FriendOfflineQueueStatus : byte
{
    Queued=0,
    TargetNotFound=1,
    NotFriends=2,
    Ignored=3,
    Conflict=4
}

public sealed record FriendOfflineQueueResult(
    FriendOfflineQueueStatus Status,long TargetCharacterId,string TargetName);

public sealed record FriendOfflineMessageRecord(
    long MessageId,long SenderCharacterId,long SenderConnectionEpoch,
    uint SenderChatSequence,string SenderName,string Message);

public sealed partial class SqliteIdentityStore
{
    public async Task<FriendOfflineQueueResult> QueueFriendOfflineMessageAsync(
        long senderCharacterId,long senderConnectionEpoch,uint senderChatSequence,
        long targetCharacterId,string message,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(senderCharacterId<=0||senderConnectionEpoch<=0||senderChatSequence==0||
           targetCharacterId<=0||targetCharacterId==senderCharacterId||
           string.IsNullOrEmpty(message)||Encoding.UTF8.GetByteCount(message)>101)
            return new(FriendOfflineQueueStatus.Conflict,targetCharacterId,string.Empty);

        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            if(!FriendCharacterExists(connection,tx,senderCharacterId)||
               !FriendCharacterExists(connection,tx,targetCharacterId))
                return new FriendOfflineQueueResult(
                    FriendOfflineQueueStatus.TargetNotFound,targetCharacterId,string.Empty);
            if(!FriendshipExists(connection,tx,senderCharacterId,targetCharacterId))
                return new FriendOfflineQueueResult(
                    FriendOfflineQueueStatus.NotFriends,targetCharacterId,string.Empty);

            using(var ignored=connection.CreateCommand())
            {
                ignored.Transaction=tx;
                ignored.CommandText=@"SELECT 1 FROM character_ignores
WHERE (owner_character_id=$sender AND target_character_id=$target)
   OR (owner_character_id=$target AND target_character_id=$sender)
LIMIT 1;";
                ignored.Parameters.AddWithValue("$sender",senderCharacterId);
                ignored.Parameters.AddWithValue("$target",targetCharacterId);
                if(ignored.ExecuteScalar() is not null)
                    return new FriendOfflineQueueResult(
                        FriendOfflineQueueStatus.Ignored,targetCharacterId,string.Empty);
            }

            string targetName;
            using(var name=connection.CreateCommand())
            {
                name.Transaction=tx;
                name.CommandText=@"SELECT COALESCE(a.alias,c.name)
FROM characters c
LEFT JOIN character_friend_aliases a
  ON a.owner_character_id=$sender AND a.target_character_id=c.character_id
WHERE c.character_id=$target LIMIT 1;";
                name.Parameters.AddWithValue("$sender",senderCharacterId);
                name.Parameters.AddWithValue("$target",targetCharacterId);
                targetName=Convert.ToString(name.ExecuteScalar())??string.Empty;
            }
            if(string.IsNullOrWhiteSpace(targetName))
                return new FriendOfflineQueueResult(
                    FriendOfflineQueueStatus.TargetNotFound,targetCharacterId,string.Empty);

            using(var insert=connection.CreateCommand())
            {
                insert.Transaction=tx;
                insert.CommandText=@"INSERT INTO character_friend_offline_messages(
recipient_character_id,sender_character_id,sender_connection_epoch,
sender_chat_sequence,message,created_unix_ms)
VALUES($recipient,$sender,$connection,$sequence,$message,$now);";
                insert.Parameters.AddWithValue("$recipient",targetCharacterId);
                insert.Parameters.AddWithValue("$sender",senderCharacterId);
                insert.Parameters.AddWithValue("$connection",senderConnectionEpoch);
                insert.Parameters.AddWithValue("$sequence",(long)senderChatSequence);
                insert.Parameters.AddWithValue("$message",message);
                insert.Parameters.AddWithValue(
                    "$now",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                if(insert.ExecuteNonQuery()!=1)
                    return new FriendOfflineQueueResult(
                        FriendOfflineQueueStatus.Conflict,targetCharacterId,string.Empty);
            }
            tx.Commit();
            return new FriendOfflineQueueResult(
                FriendOfflineQueueStatus.Queued,targetCharacterId,targetName);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FriendOfflineMessageRecord>>
        GetFriendOfflineMessagesAsync(
            long recipientCharacterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(recipientCharacterId<=0)
            return Array.Empty<FriendOfflineMessageRecord>();
        return await Task.Run<IReadOnlyList<FriendOfflineMessageRecord>>(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT
m.message_id,m.sender_character_id,m.sender_connection_epoch,
m.sender_chat_sequence,c.name,m.message
FROM character_friend_offline_messages m
JOIN characters c ON c.character_id=m.sender_character_id
WHERE m.recipient_character_id=$recipient
AND EXISTS(
  SELECT 1 FROM character_friendships f
  WHERE f.character_low_id=MIN(m.recipient_character_id,m.sender_character_id)
    AND f.character_high_id=MAX(m.recipient_character_id,m.sender_character_id)
)
AND NOT EXISTS(
  SELECT 1 FROM character_ignores i
  WHERE i.owner_character_id=m.recipient_character_id
    AND i.target_character_id=m.sender_character_id
)
ORDER BY m.message_id;";
            command.Parameters.AddWithValue("$recipient",recipientCharacterId);
            using var reader=command.ExecuteReader();
            var result=new List<FriendOfflineMessageRecord>();
            while(reader.Read())
                result.Add(new(
                    reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),
                    checked((uint)reader.GetInt64(3)),reader.GetString(4),reader.GetString(5)));
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteFriendOfflineMessageAsync(
        long recipientCharacterId,long messageId,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(recipientCharacterId<=0||messageId<=0)return false;
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"DELETE FROM character_friend_offline_messages
WHERE message_id=$message AND recipient_character_id=$recipient;";
            command.Parameters.AddWithValue("$message",messageId);
            command.Parameters.AddWithValue("$recipient",recipientCharacterId);
            return command.ExecuteNonQuery()==1;
        },cancellationToken).ConfigureAwait(false);
    }
}
