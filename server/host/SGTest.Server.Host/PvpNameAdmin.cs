using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;
using SGTest.Server.World;

/// <summary>
/// Local operator PvP-name command. It is intentionally unreachable from game sockets.
/// Every accepted mutation requires an explicit operator, reason and idempotent event ID.
/// </summary>
internal static class PvpNameAdmin
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options=Parse(args);
            string database=Path.GetFullPath(options["--identity"]);
            if(!File.Exists(database))
                throw new ArgumentException("Identity database does not exist");
            if(!long.TryParse(options["--character-id"],NumberStyles.None,
                    CultureInfo.InvariantCulture,out long characterId)||characterId<=0)
                throw new ArgumentException("Character ID must be a positive integer");
            PvpNameState state=ParseState(options["--state"]);
            using var store=new SqliteIdentityStore(database);
            await store.InitializeAsync().ConfigureAwait(false);
            var result=await store.ApplyPvpNameAdminAsync(
                characterId,state,options["--operator"],options["--reason"],
                options["--event-id"],DateTimeOffset.UtcNow).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result=result.Replayed?"PVP_NAME_REPLAY":"PVP_NAME_SET",
                event_id=result.EventId,
                character_id=result.State.CharacterId,
                requested_state=state.ToString(),
                mode=result.State.Mode.ToString(),
                red=result.State.RedMan,
                pink_until_unix_ms=result.State.PinkUntilUnixMilliseconds,
                replayed=result.Replayed
            }));
            return 0;
        }
        catch(ArgumentException error)
        {
            Console.Error.WriteLine("PVP_NAME_ADMIN_CONFIGURATION: "+error.Message);
            Console.Error.WriteLine(
                "Usage: pvp-name-set --identity <DB> --character-id <ID> " +
                "--state <blue|white|pink|flash|red> --operator <ID> " +
                "--reason <TEXT> --event-id <TOKEN>");
            return 2;
        }
        catch(KeyNotFoundException error)
        {
            Console.Error.WriteLine("PVP_NAME_ADMIN_REFUSED: "+error.Message);
            return 3;
        }
        catch(InvalidOperationException error)
        {
            Console.Error.WriteLine("PVP_NAME_ADMIN_REFUSED: "+error.Message);
            return 3;
        }
        catch(Exception error) when(error is IOException or InvalidDataException or SqliteException)
        {
            Console.Error.WriteLine(
                "PVP_NAME_ADMIN_FAIL: "+error.GetType().Name+": "+error.Message);
            return 1;
        }
    }

    private static Dictionary<string,string> Parse(string[] args)
    {
        string[] known=[
            "--identity","--character-id","--state","--operator","--reason","--event-id"];
        if(args.Length!=known.Length*2)
            throw new ArgumentException("Supply every documented option exactly once");
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=0;i<args.Length;i+=2)
            if(!known.Contains(args[i])||string.IsNullOrWhiteSpace(args[i+1])||
               !values.TryAdd(args[i],args[i+1]))
                throw new ArgumentException("Unknown, duplicate or empty option");
        return values;
    }

    private static class StateToken
    {
        public const string ProtectedBlue = "blue";
        public const string EnabledWhite = "white";
        public const string HostilePink = "pink";
        public const string HostilePinkFlashing = "flash";
        public const string CriminalRed = "red";

        public static readonly IReadOnlyDictionary<string,PvpNameState> Values =
            new Dictionary<string,PvpNameState>(StringComparer.Ordinal)
            {
                [ProtectedBlue]=PvpNameState.ProtectedBlue,
                [EnabledWhite]=PvpNameState.EnabledWhite,
                [HostilePink]=PvpNameState.Pink,
                [HostilePinkFlashing]=PvpNameState.PinkFlashing,
                [CriminalRed]=PvpNameState.Red
            };
    }

    private static PvpNameState ParseState(string value)
    {
        string token=value.Trim().ToLowerInvariant();
        if(StateToken.Values.TryGetValue(token,out var state))
            return state;
        throw new ArgumentException(
            "State must be blue, white, pink, flash or red");
    }
}
