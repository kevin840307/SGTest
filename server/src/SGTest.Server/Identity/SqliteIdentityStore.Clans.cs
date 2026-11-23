using System.Text;
using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum ClanStoreStatus : byte
{
    Success=0,AlreadyInClan=1,NotInClan=2,ClanNotFound=3,
    NameTaken=4,InvalidName=5,ClanFull=6,Conflict=7,
    NotLeader=8,TargetNotMember=9,CannotTargetSelf=10,InvalidAnnouncement=11
}
public enum ClanMemberRole : byte { Member=0, Leader=1 }
public sealed record ClanMemberRecord(long CharacterId,string Name,ClanMemberRole Role);
public sealed record ClanRecord(
    long ClanId,uint Revision,long LeaderCharacterId,string Name,string Announcement,
    IReadOnlyList<ClanMemberRecord> Members);

public sealed partial class SqliteIdentityStore
{
    public const int ClanMaximumMembers=100;
    public const int ClanMaximumNameBytes=72;
    public const int ClanMaximumAnnouncementBytes=1024;

    private static (string Display,string Key) NormalizeClanName(string name)
    {
        if(name is null)throw new ArgumentNullException(nameof(name));
        string display=name.Normalize(NormalizationForm.FormC).Trim();
        int bytes=Encoding.UTF8.GetByteCount(display);
        if(display.Length is <2 or >24||bytes>ClanMaximumNameBytes||
           display.Any(char.IsControl))
            throw new ArgumentException("Clan name must be 2-24 visible characters",nameof(name));
        return(display,display.ToUpperInvariant());
    }

    private static string NormalizeClanAnnouncement(string announcement)
    {
        if(announcement is null)throw new ArgumentNullException(nameof(announcement));
        string display=announcement.Normalize(NormalizationForm.FormC).Trim();
        if(Encoding.UTF8.GetByteCount(display)>ClanMaximumAnnouncementBytes||
           display.Any(ch=>char.IsControl(ch)&&ch!='\n'))
            throw new ArgumentException("Clan announcement exceeds the bounded UTF-8 contract",
                nameof(announcement));
        return display;
    }

    public async Task<(ClanStoreStatus Status,ClanRecord? Clan)> CreateClanAsync(
        long characterId,string name,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)return(ClanStoreStatus.Conflict,null);
        (string Display,string Key) normalized;
        try{normalized=NormalizeClanName(name);}
        catch(ArgumentException){return(ClanStoreStatus.InvalidName,null);}
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            if(!ClanCharacterExists(connection,tx,characterId))
                return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            if(CharacterClanId(connection,tx,characterId)>0)
                return(ClanStoreStatus.AlreadyInClan,(ClanRecord?)null);
            using(var duplicate=connection.CreateCommand())
            {
                duplicate.Transaction=tx;
                duplicate.CommandText="SELECT 1 FROM clans WHERE name_key=$key LIMIT 1;";
                duplicate.Parameters.AddWithValue("$key",normalized.Key);
                if(duplicate.ExecuteScalar() is not null)
                    return(ClanStoreStatus.NameTaken,(ClanRecord?)null);
            }
            long now=_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            long clanId;
            using(var command=connection.CreateCommand())
            {
                command.Transaction=tx;
                command.CommandText=@"INSERT INTO clans(
name,name_key,leader_character_id,revision,created_unix_ms)
VALUES($name,$key,$leader,1,$now);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$name",normalized.Display);
                command.Parameters.AddWithValue("$key",normalized.Key);
                command.Parameters.AddWithValue("$leader",characterId);
                command.Parameters.AddWithValue("$now",now);
                clanId=(long)(command.ExecuteScalar()??0L);
            }
            if(clanId<=0)return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            using(var member=connection.CreateCommand())
            {
                member.Transaction=tx;
                member.CommandText=@"INSERT INTO clan_members(
character_id,clan_id,role,joined_unix_ms) VALUES($character,$clan,1,$now);";
                member.Parameters.AddWithValue("$character",characterId);
                member.Parameters.AddWithValue("$clan",clanId);
                member.Parameters.AddWithValue("$now",now);
                if(member.ExecuteNonQuery()!=1)return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            }
            var snapshot=ReadClan(connection,tx,clanId);
            tx.Commit();
            return(ClanStoreStatus.Success,snapshot);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<(ClanStoreStatus Status,ClanRecord? Clan)> JoinClanAsync(
        long characterId,long clanId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||clanId<=0)return(ClanStoreStatus.ClanNotFound,null);
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            if(!ClanCharacterExists(connection,tx,characterId))
                return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            if(CharacterClanId(connection,tx,characterId)>0)
                return(ClanStoreStatus.AlreadyInClan,(ClanRecord?)null);
            var current=ReadClan(connection,tx,clanId);
            if(current is null)return(ClanStoreStatus.ClanNotFound,(ClanRecord?)null);
            if(current.Members.Count>=ClanMaximumMembers)
                return(ClanStoreStatus.ClanFull,(ClanRecord?)null);
            using(var command=connection.CreateCommand())
            {
                command.Transaction=tx;
                command.CommandText=@"INSERT INTO clan_members(
character_id,clan_id,role,joined_unix_ms) VALUES($character,$clan,0,$now);";
                command.Parameters.AddWithValue("$character",characterId);
                command.Parameters.AddWithValue("$clan",clanId);
                command.Parameters.AddWithValue("$now",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                if(command.ExecuteNonQuery()!=1)return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            }
            BumpClanRevision(connection,tx,clanId);
            var snapshot=ReadClan(connection,tx,clanId);
            tx.Commit();
            return(ClanStoreStatus.Success,snapshot);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<(ClanStoreStatus Status,ClanRecord? Clan)> SetClanAnnouncementAsync(
        long actorCharacterId,string announcement,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        string normalized;
        try{normalized=NormalizeClanAnnouncement(announcement);}
        catch(ArgumentException){return(ClanStoreStatus.InvalidAnnouncement,null);}
        if(actorCharacterId<=0)return(ClanStoreStatus.NotInClan,null);
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            long clanId=CharacterClanId(connection,tx,actorCharacterId);
            if(clanId<=0)return(ClanStoreStatus.NotInClan,(ClanRecord?)null);
            var clan=ReadClan(connection,tx,clanId);
            if(clan is null)return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            if(clan.LeaderCharacterId!=actorCharacterId)
                return(ClanStoreStatus.NotLeader,clan);
            using(var update=connection.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText=@"UPDATE clans
SET announcement=$announcement,revision=revision+1 WHERE clan_id=$clan;";
                update.Parameters.AddWithValue("$announcement",normalized);
                update.Parameters.AddWithValue("$clan",clanId);
                if(update.ExecuteNonQuery()!=1)
                    return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            }
            var snapshot=ReadClan(connection,tx,clanId);
            tx.Commit();
            return(ClanStoreStatus.Success,snapshot);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<(ClanStoreStatus Status,ClanRecord? Clan)> KickClanMemberAsync(
        long actorCharacterId,long targetCharacterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(actorCharacterId<=0)return(ClanStoreStatus.NotInClan,null);
        if(targetCharacterId<=0)return(ClanStoreStatus.TargetNotMember,null);
        if(actorCharacterId==targetCharacterId)return(ClanStoreStatus.CannotTargetSelf,null);
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            long clanId=CharacterClanId(connection,tx,actorCharacterId);
            if(clanId<=0)return(ClanStoreStatus.NotInClan,(ClanRecord?)null);
            var clan=ReadClan(connection,tx,clanId);
            if(clan is null)return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            if(clan.LeaderCharacterId!=actorCharacterId)
                return(ClanStoreStatus.NotLeader,clan);
            if(!clan.Members.Any(x=>x.CharacterId==targetCharacterId))
                return(ClanStoreStatus.TargetNotMember,clan);
            using(var remove=connection.CreateCommand())
            {
                remove.Transaction=tx;
                remove.CommandText=@"DELETE FROM clan_members
WHERE clan_id=$clan AND character_id=$target AND role=0;";
                remove.Parameters.AddWithValue("$clan",clanId);
                remove.Parameters.AddWithValue("$target",targetCharacterId);
                if(remove.ExecuteNonQuery()!=1)
                    return(ClanStoreStatus.TargetNotMember,clan);
            }
            BumpClanRevision(connection,tx,clanId);
            var snapshot=ReadClan(connection,tx,clanId);
            tx.Commit();
            return(ClanStoreStatus.Success,snapshot);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<(ClanStoreStatus Status,ClanRecord? Clan)> TransferClanLeadershipAsync(
        long actorCharacterId,long targetCharacterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(actorCharacterId<=0)return(ClanStoreStatus.NotInClan,null);
        if(targetCharacterId<=0)return(ClanStoreStatus.TargetNotMember,null);
        if(actorCharacterId==targetCharacterId)return(ClanStoreStatus.CannotTargetSelf,null);
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            long clanId=CharacterClanId(connection,tx,actorCharacterId);
            if(clanId<=0)return(ClanStoreStatus.NotInClan,(ClanRecord?)null);
            var clan=ReadClan(connection,tx,clanId);
            if(clan is null)return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            if(clan.LeaderCharacterId!=actorCharacterId)
                return(ClanStoreStatus.NotLeader,clan);
            if(!clan.Members.Any(x=>x.CharacterId==targetCharacterId))
                return(ClanStoreStatus.TargetNotMember,clan);
            using(var promote=connection.CreateCommand())
            {
                promote.Transaction=tx;
                promote.CommandText=@"UPDATE clan_members SET role=CASE
WHEN character_id=$target THEN 1 ELSE 0 END WHERE clan_id=$clan;
UPDATE clans SET leader_character_id=$target,revision=revision+1 WHERE clan_id=$clan;";
                promote.Parameters.AddWithValue("$target",targetCharacterId);
                promote.Parameters.AddWithValue("$clan",clanId);
                promote.ExecuteNonQuery();
            }
            var snapshot=ReadClan(connection,tx,clanId);
            if(snapshot is null||snapshot.LeaderCharacterId!=targetCharacterId)
                return(ClanStoreStatus.Conflict,(ClanRecord?)null);
            tx.Commit();
            return(ClanStoreStatus.Success,snapshot);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<(ClanStoreStatus Status,ClanRecord? Clan,long PreviousClanId)>
        LeaveClanAsync(long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)return(ClanStoreStatus.NotInClan,null,0);
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            long clanId=CharacterClanId(connection,tx,characterId);
            if(clanId<=0)return(ClanStoreStatus.NotInClan,(ClanRecord?)null,0L);
            var clan=ReadClan(connection,tx,clanId);
            if(clan is null)return(ClanStoreStatus.Conflict,(ClanRecord?)null,clanId);
            if(clan.LeaderCharacterId==characterId)
            {
                var successor=clan.Members
                    .Where(x=>x.CharacterId!=characterId)
                    .OrderBy(x=>x.CharacterId).FirstOrDefault();
                if(successor is null)
                {
                    using var removeMember=connection.CreateCommand();
                    removeMember.Transaction=tx;
                    removeMember.CommandText="DELETE FROM clan_members WHERE character_id=$id;";
                    removeMember.Parameters.AddWithValue("$id",characterId);
                    removeMember.ExecuteNonQuery();
                    using var removeClan=connection.CreateCommand();
                    removeClan.Transaction=tx;
                    removeClan.CommandText="DELETE FROM clans WHERE clan_id=$clan;";
                    removeClan.Parameters.AddWithValue("$clan",clanId);
                    if(removeClan.ExecuteNonQuery()!=1)
                        return(ClanStoreStatus.Conflict,(ClanRecord?)null,clanId);
                    tx.Commit();
                    return(ClanStoreStatus.Success,(ClanRecord?)null,clanId);
                }
                using(var promote=connection.CreateCommand())
                {
                    promote.Transaction=tx;
                    promote.CommandText=@"UPDATE clan_members SET role=CASE
WHEN character_id=$next THEN 1 ELSE 0 END WHERE clan_id=$clan;
UPDATE clans SET leader_character_id=$next,revision=revision+1 WHERE clan_id=$clan;";
                    promote.Parameters.AddWithValue("$next",successor.CharacterId);
                    promote.Parameters.AddWithValue("$clan",clanId);
                    promote.ExecuteNonQuery();
                }
            }
            using(var remove=connection.CreateCommand())
            {
                remove.Transaction=tx;
                remove.CommandText="DELETE FROM clan_members WHERE character_id=$id;";
                remove.Parameters.AddWithValue("$id",characterId);
                if(remove.ExecuteNonQuery()!=1)
                    return(ClanStoreStatus.Conflict,(ClanRecord?)null,clanId);
            }
            if(clan.LeaderCharacterId!=characterId)
                BumpClanRevision(connection,tx,clanId);
            var snapshot=ReadClan(connection,tx,clanId);
            tx.Commit();
            return(ClanStoreStatus.Success,snapshot,clanId);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClanRecord?> GetClanForCharacterAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)return null;
        return await Task.Run(()=>
        {
            using var connection=Open();
            long clanId=CharacterClanId(connection,null,characterId);
            return clanId<=0?null:ReadClan(connection,null,clanId);
        },cancellationToken).ConfigureAwait(false);
    }

    private static bool ClanCharacterExists(
        SqliteConnection connection,SqliteTransaction? tx,long characterId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="SELECT 1 FROM characters WHERE character_id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id",characterId);
        return command.ExecuteScalar() is not null;
    }

    private static long CharacterClanId(
        SqliteConnection connection,SqliteTransaction? tx,long characterId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="SELECT clan_id FROM clan_members WHERE character_id=$id;";
        command.Parameters.AddWithValue("$id",characterId);
        return command.ExecuteScalar() is long value?value:0;
    }

    private static void BumpClanRevision(
        SqliteConnection connection,SqliteTransaction tx,long clanId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="UPDATE clans SET revision=revision+1 WHERE clan_id=$clan;";
        command.Parameters.AddWithValue("$clan",clanId);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("Clan revision update failed");
    }

    private static ClanRecord? ReadClan(
        SqliteConnection connection,SqliteTransaction? tx,long clanId)
    {
        long leader;uint revision;string name;string announcement;
        using(var command=connection.CreateCommand())
        {
            command.Transaction=tx;
            command.CommandText=@"SELECT name,leader_character_id,revision,announcement
FROM clans WHERE clan_id=$clan;";
            command.Parameters.AddWithValue("$clan",clanId);
            using var reader=command.ExecuteReader();
            if(!reader.Read())return null;
            name=reader.GetString(0);
            leader=reader.GetInt64(1);
            revision=checked((uint)reader.GetInt64(2));
            announcement=reader.GetString(3);
        }
        var members=new List<ClanMemberRecord>();
        using(var command=connection.CreateCommand())
        {
            command.Transaction=tx;
            command.CommandText=@"SELECT m.character_id,c.name,m.role
FROM clan_members m JOIN characters c ON c.character_id=m.character_id
WHERE m.clan_id=$clan
ORDER BY m.role DESC,m.joined_unix_ms,m.character_id;";
            command.Parameters.AddWithValue("$clan",clanId);
            using var reader=command.ExecuteReader();
            while(reader.Read())
                members.Add(new(reader.GetInt64(0),reader.GetString(1),
                    (ClanMemberRole)reader.GetInt32(2)));
        }
        return new(clanId,revision,leader,name,announcement,members);
    }
}
