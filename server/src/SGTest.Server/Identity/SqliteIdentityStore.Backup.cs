using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public sealed record IdentityBackupResult(
    string DestinationPath,int SchemaVersion,long ByteLength,string Sha256);

public sealed partial class SqliteIdentityStore
{
    /// <summary>
    /// Creates a transactionally consistent SQLite snapshot of the current identity
    /// database. Existing destination files are never replaced.
    /// </summary>
    public Task<IdentityBackupResult> BackupAsync(
        string destinationPath,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        return BackupDatabaseAsync(_databasePath,destinationPath,cancellationToken);
    }

    /// <summary>
    /// Offline/operator backup primitive. It never initializes or migrates the source
    /// database, so an older known schema can be copied before this binary upgrades it.
    /// </summary>
    public static async Task<IdentityBackupResult> BackupDatabaseAsync(
        string databasePath,string destinationPath,
        CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Identity database path required",nameof(databasePath));
        if(string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Backup destination path required",nameof(destinationPath));
        string source=Path.GetFullPath(databasePath);
        string destination=Path.GetFullPath(destinationPath);
        var comparison=OperatingSystem.IsWindows()
            ?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        if(string.Equals(source,destination,comparison))
            throw new ArgumentException(
                "Backup destination must differ from identity database",
                nameof(destinationPath));
        if(!File.Exists(source))
            throw new FileNotFoundException("Identity database does not exist",source);
        return await Task.Run(
            ()=>CreateBackupCore(source,destination),cancellationToken)
            .ConfigureAwait(false);
    }

    private void CreateMigrationBackup(int sourceVersion)
    {
        if(sourceVersion is <1 or >=SchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(sourceVersion));
        string directory=Path.GetDirectoryName(_databasePath)??".";
        string name=Path.GetFileName(_databasePath);
        string timestamp=DateTimeOffset.UtcNow.ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",CultureInfo.InvariantCulture);
        string destination=Path.Combine(directory,
            $"{name}.pre-v{sourceVersion}-to-v{SchemaVersion}.{timestamp}.{Guid.NewGuid():N}.bak");
        _=CreateBackupCore(_databasePath,destination);
    }

    private static IdentityBackupResult CreateBackupCore(
        string source,string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)??".");
        if(File.Exists(destination))
            throw new IOException("Backup destination already exists");
        // Reserve the exact path atomically so a concurrent operator cannot make
        // us overwrite a file between validation and SQLite open.
        using(File.Open(destination,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){}

        try
        {
            int sourceVersion;
            using(var sourceConnection=new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource=source,Mode=SqliteOpenMode.ReadOnly,
                    Cache=SqliteCacheMode.Private,Pooling=false
                }.ToString()))
            {
                sourceConnection.Open();
                sourceVersion=ReadRequiredSchemaVersion(sourceConnection);
                using var destinationConnection=new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource=destination,Mode=SqliteOpenMode.ReadWrite,
                        Cache=SqliteCacheMode.Private,Pooling=false
                    }.ToString());
                destinationConnection.Open();
                sourceConnection.BackupDatabase(destinationConnection);
                using var integrity=destinationConnection.CreateCommand();
                integrity.CommandText="PRAGMA integrity_check;";
                string result=Convert.ToString(integrity.ExecuteScalar(),
                    CultureInfo.InvariantCulture)??string.Empty;
                if(!string.Equals(result,"ok",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Identity backup failed SQLite integrity_check");
                if(ReadRequiredSchemaVersion(destinationConnection)!=sourceVersion)
                    throw new InvalidDataException(
                        "Identity backup schema version changed during copy");
            }

            byte[] digest=SHA256.HashData(File.ReadAllBytes(destination));
            return new IdentityBackupResult(
                destination,sourceVersion,new FileInfo(destination).Length,
                Convert.ToHexString(digest));
        }
        catch
        {
            CleanupPartialBackup(destination);
            throw;
        }
    }

    private static int? TryReadExistingSchemaVersion(SqliteConnection connection)
    {
        using(var table=connection.CreateCommand())
        {
            table.CommandText=@"
SELECT COUNT(*)
FROM sqlite_master
WHERE type='table' AND name='schema_version';";
            long present=Convert.ToInt64(
                table.ExecuteScalar(),CultureInfo.InvariantCulture);
            if(present==0)return null;
            if(present!=1)
                throw new InvalidDataException(
                    "Unsupported identity schema metadata");
        }
        return ReadRequiredSchemaVersion(connection);
    }

    private static int ReadRequiredSchemaVersion(SqliteConnection connection)
    {
        using var count=connection.CreateCommand();
        count.CommandText="SELECT COUNT(*) FROM schema_version;";
        long rows=Convert.ToInt64(
            count.ExecuteScalar(),CultureInfo.InvariantCulture);
        if(rows!=1)
            throw new InvalidDataException(
                "Unsupported identity schema version");
        using var version=connection.CreateCommand();
        version.CommandText="SELECT version FROM schema_version LIMIT 1;";
        long value=Convert.ToInt64(
            version.ExecuteScalar(),CultureInfo.InvariantCulture);
        if(value is <1 or >int.MaxValue)
            throw new InvalidDataException(
                "Unsupported identity schema version");
        return checked((int)value);
    }

    private static void CleanupPartialBackup(string path)
    {
        foreach(string candidate in new[]{path,path+"-wal",path+"-shm"})
        {
            try
            {
                if(File.Exists(candidate))File.Delete(candidate);
            }
            catch
            {
                // Preserve the original backup exception. A partial file is never
                // reported as successful and will not be overwritten on retry.
            }
        }
    }
}
