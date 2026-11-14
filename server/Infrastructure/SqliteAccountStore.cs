using Microsoft.Data.Sqlite;
using SGTest.Server.Domain;

namespace SGTest.Server.Infrastructure;

public sealed class SqliteAccountStore : IAccountStore
{
    readonly string connectionString;
    readonly object writeGate=new();
    public string DatabasePath { get; }

    public SqliteAccountStore(string path)
    {
        DatabasePath=Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString=new SqliteConnectionStringBuilder
        {
            DataSource=DatabasePath,
            Mode=SqliteOpenMode.ReadWriteCreate,
            Cache=SqliteCacheMode.Shared,
            Pooling=true,
            DefaultTimeout=5,
        }.ToString();
        Migrate();
    }

    SqliteConnection Open()
    {
        var connection=new SqliteConnection(connectionString);
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText="PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    void Migrate()
    {
        lock(writeGate)
        {
            using var connection=Open();
            using(var pragma=connection.CreateCommand())
            {
                pragma.CommandText="PRAGMA journal_mode=WAL;";
                string mode=Convert.ToString(pragma.ExecuteScalar()) ?? "";
                if(!mode.Equals("wal",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SQLite WAL could not be enabled: {mode}");
            }
            using(var pragma=connection.CreateCommand())
            {
                pragma.CommandText="PRAGMA synchronous=NORMAL;";
                pragma.ExecuteNonQuery();
            }
            using var command=connection.CreateCommand();
            command.CommandText="""
                CREATE TABLE IF NOT EXISTS schema_migration(
                    version INTEGER PRIMARY KEY,
                    applied_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS account(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL,
                    normalized_username TEXT NOT NULL UNIQUE,
                    password_salt BLOB NOT NULL,
                    password_hash BLOB NOT NULL,
                    state INTEGER NOT NULL DEFAULT 1,
                    created_utc TEXT NOT NULL,
                    version INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS character(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    account_id INTEGER NOT NULL REFERENCES account(id) ON DELETE CASCADE,
                    name TEXT NOT NULL,
                    normalized_name TEXT NOT NULL UNIQUE,
                    created_utc TEXT NOT NULL,
                    version INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS ix_character_account ON character(account_id,id);
                INSERT OR IGNORE INTO schema_migration(version,applied_utc)
                VALUES(1,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """;
            command.ExecuteNonQuery();
        }
    }

    public AccountCredentials? FindCredentials(string normalizedUsername)
    {
        using var connection=Open();
        using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT id,username,password_salt,password_hash
            FROM account
            WHERE normalized_username=$username AND state=1
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$username",normalizedUsername);
        using var reader=command.ExecuteReader();
        if(!reader.Read()) return null;
        return new(reader.GetInt64(0),reader.GetString(1),
            (byte[])reader.GetValue(2),(byte[])reader.GetValue(3));
    }

    public bool TryCreateAccount(string username,string normalizedUsername,byte[] salt,byte[] passwordHash,out long accountId)
    {
        lock(writeGate)
        {
            using var connection=Open();
            using var transaction=connection.BeginTransaction(deferred:false);
            using var command=connection.CreateCommand();
            command.Transaction=transaction;
            command.CommandText="""
                INSERT OR IGNORE INTO account(username,normalized_username,password_salt,password_hash,created_utc)
                VALUES($username,$normalized,$salt,$hash,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """;
            command.Parameters.AddWithValue("$username",username);
            command.Parameters.AddWithValue("$normalized",normalizedUsername);
            command.Parameters.Add("$salt",SqliteType.Blob).Value=salt;
            command.Parameters.Add("$hash",SqliteType.Blob).Value=passwordHash;
            if(command.ExecuteNonQuery()!=1)
            {
                transaction.Rollback();
                accountId=0;
                return false;
            }
            command.Parameters.Clear();
            command.CommandText="SELECT last_insert_rowid();";
            accountId=Convert.ToInt64(command.ExecuteScalar());
            transaction.Commit();
            return true;
        }
    }

    public CharacterSummary[] Characters(long accountId)
    {
        using var connection=Open();
        using var command=connection.CreateCommand();
        command.CommandText="SELECT id,name FROM character WHERE account_id=$account ORDER BY id;";
        command.Parameters.AddWithValue("$account",accountId);
        using var reader=command.ExecuteReader();
        var rows=new List<CharacterSummary>();
        while(reader.Read()) rows.Add(new(reader.GetInt64(0),reader.GetString(1)));
        return [..rows];
    }

    public CharacterRecord? Character(long accountId,long characterId)
    {
        using var connection=Open();
        using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT id,account_id,name,normalized_name
            FROM character WHERE account_id=$account AND id=$character LIMIT 1;
            """;
        command.Parameters.AddWithValue("$account",accountId);
        command.Parameters.AddWithValue("$character",characterId);
        using var reader=command.ExecuteReader();
        return reader.Read()
            ? new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetString(3))
            : null;
    }

    public IdentityResult CreateCharacter(long accountId,string name,string normalizedName,int maximumCharacters)
    {
        lock(writeGate)
        {
            using var connection=Open();
            using var transaction=connection.BeginTransaction(deferred:false);
            using(var account=connection.CreateCommand())
            {
                account.Transaction=transaction;
                account.CommandText="SELECT COUNT(*) FROM account WHERE id=$account AND state=1;";
                account.Parameters.AddWithValue("$account",accountId);
                if(Convert.ToInt64(account.ExecuteScalar())!=1)
                {
                    transaction.Rollback();
                    return new(false,"not_authenticated","Account is not authenticated.");
                }
            }
            using(var count=connection.CreateCommand())
            {
                count.Transaction=transaction;
                count.CommandText="SELECT COUNT(*) FROM character WHERE account_id=$account;";
                count.Parameters.AddWithValue("$account",accountId);
                if(Convert.ToInt64(count.ExecuteScalar())>=maximumCharacters)
                {
                    transaction.Rollback();
                    return new(false,"character_limit","Character limit reached.",accountId);
                }
            }
            using(var duplicate=connection.CreateCommand())
            {
                duplicate.Transaction=transaction;
                duplicate.CommandText="SELECT EXISTS(SELECT 1 FROM character WHERE normalized_name=$name);";
                duplicate.Parameters.AddWithValue("$name",normalizedName);
                if(Convert.ToInt64(duplicate.ExecuteScalar())!=0)
                {
                    transaction.Rollback();
                    return new(false,"character_name_exists","Character name already exists.",accountId);
                }
            }
            try
            {
                using var insert=connection.CreateCommand();
                insert.Transaction=transaction;
                insert.CommandText="""
                    INSERT INTO character(account_id,name,normalized_name,created_utc)
                    VALUES($account,$name,$normalized,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                    """;
                insert.Parameters.AddWithValue("$account",accountId);
                insert.Parameters.AddWithValue("$name",name);
                insert.Parameters.AddWithValue("$normalized",normalizedName);
                insert.ExecuteNonQuery();
                insert.Parameters.Clear();
                insert.CommandText="SELECT last_insert_rowid();";
                long characterId=Convert.ToInt64(insert.ExecuteScalar());
                transaction.Commit();
                return new(true,"character_created","Character created.",accountId,characterId);
            }
            catch(SqliteException error) when(error.SqliteErrorCode==19)
            {
                transaction.Rollback();
                return new(false,"character_name_exists","Character name already exists.",accountId);
            }
        }
    }

    public string JournalMode()
    {
        using var connection=Open();
        using var command=connection.CreateCommand();
        command.CommandText="PRAGMA journal_mode;";
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    public bool ForeignKeysEnabled()
    {
        using var connection=Open();
        using var command=connection.CreateCommand();
        command.CommandText="PRAGMA foreign_keys;";
        return Convert.ToInt64(command.ExecuteScalar())==1;
    }
}
