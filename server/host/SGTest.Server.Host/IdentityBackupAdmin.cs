using System.Text.Json;
using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

/// <summary>
/// Local operator snapshot command. It never initializes or migrates the source.
/// Stop the game Host before using this for an operational backup.
/// </summary>
internal static class IdentityBackupAdmin
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options=Parse(args);
            string source=Path.GetFullPath(options["--identity"]);
            string output=Path.GetFullPath(options["--output"]);
            if(!File.Exists(source))
                throw new ArgumentException("Identity database does not exist");
            var result=await SqliteIdentityStore.BackupDatabaseAsync(
                source,output).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result="IDENTITY_BACKUP_CREATED",
                schema_version=result.SchemaVersion,
                bytes=result.ByteLength,
                sha256=result.Sha256,
                output=result.DestinationPath
            }));
            return 0;
        }
        catch(ArgumentException error)
        {
            Console.Error.WriteLine(
                "IDENTITY_BACKUP_CONFIGURATION: "+error.Message);
            Console.Error.WriteLine(
                "Usage: identity-backup --identity <DB> --output <NEW_BACKUP_DB>");
            return 2;
        }
        catch(Exception error) when(error is IOException or InvalidDataException or
                                      UnauthorizedAccessException or SqliteException)
        {
            Console.Error.WriteLine(
                "IDENTITY_BACKUP_FAIL: "+error.GetType().Name+": "+error.Message);
            return 1;
        }
    }

    private static Dictionary<string,string> Parse(string[] args)
    {
        string[] known=["--identity","--output"];
        if(args.Length!=known.Length*2)
            throw new ArgumentException(
                "Supply every documented option exactly once");
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=0;i<args.Length;i+=2)
            if(!known.Contains(args[i])||
               string.IsNullOrWhiteSpace(args[i+1])||
               !values.TryAdd(args[i],args[i+1]))
                throw new ArgumentException(
                    "Unknown, duplicate or empty option");
        return values;
    }
}
