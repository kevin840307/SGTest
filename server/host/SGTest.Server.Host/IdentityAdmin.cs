using SGTest.Server.Map;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;
using SGTest.Server.World;

/// <summary>Local operator commands only, never dispatched from a game socket.</summary>
internal static class IdentityAdmin
{
    private const string PasswordEnvironment = "SGTEST_ACCOUNT_PASSWORD";

    public static async Task<int> RunAsync(bool createAccount, string[] args)
    {
        string password = string.Empty;
        try
        {
            string[] known = createAccount
                ? ["--identity", "--username"]
                : ["--identity", "--username", "--content", "--name", "--slot", "--map", "--x", "--y"];
            var options = Parse(args, known);
            string database = Path.GetFullPath(options["--identity"]);
            string username = options["--username"];
            CharacterSpawn spawn = default;
            int slot = 0;
            if (!createAccount)
            {
                if (!File.Exists(database)) throw new ArgumentException("Create the identity account database first");
                if (!int.TryParse(options["--slot"], out slot) || slot is < 0 or > 7)
                    throw new ArgumentException("Slot must be 0..7");
                if (!int.TryParse(options["--map"], out int map) || map is not (13 or 15))
                    throw new ArgumentException("This Host currently serves Map13 and Map15 only");
                double x = Coordinate(options["--x"]), y = Coordinate(options["--y"]);
                var blocking = BlockingGrid.Load(Path.Combine(Path.GetFullPath(options["--content"]),
                    "maps", map.ToString(CultureInfo.InvariantCulture)), map);
                if (!blocking.TryWorldCell(x, y, out var cell) || !blocking.IsWalkable(cell))
                    throw new ArgumentException("Explicit spawn must be inside a walkable original Blocking cell");
                spawn = new(map, x, y); // operator-selected point, never an invented legacy default
            }
            password = ReadPassword();
            _ = new UTF8Encoding(false, true).GetByteCount(password);
            PasswordHasher.ValidateNewPassword(password);
            using var store = new SqliteIdentityStore(database);
            await store.InitializeAsync();
            if (createAccount)
            {
                var result = await store.CreateAccountAsync(username, password);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    result = result.Status == AccountCreateStatus.Created ? "ACCOUNT_CREATED" : "ACCOUNT_REFUSED",
                    status = result.Status.ToString(), account_id = result.AccountId
                }));
                return result.Status == AccountCreateStatus.Created ? 0 : 3;
            }
            var auth = await store.AuthenticateAsync(username, password);
            if (auth.Status != AuthenticationStatus.Success || auth.Account is null)
            {
                Console.Error.WriteLine("CHARACTER_REFUSED: credentials invalid or account disabled");
                return 3;
            }
            var character = await store.CreateCharacterAsync(auth.Account.AccountId,
                slot, options["--name"], spawn);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result = character.Status == CharacterCreateStatus.Created ? "CHARACTER_CREATED" : "CHARACTER_REFUSED",
                status = character.Status.ToString(),
                account_id = auth.Account.AccountId,
                character_id = character.Character?.CharacterId,
                map_id = character.Character?.MapId,
                slot = character.Character?.Slot
            }));
            return character.Status == CharacterCreateStatus.Created ? 0 : 3;
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine("IDENTITY_ADMIN_CONFIGURATION: " + error.Message);
            Console.Error.WriteLine(createAccount
                ? "Usage: account-create --identity <DB> --username <NAME>"
                : "Usage: character-create --identity <DB> --username <NAME> --content <PRIVATE_ROOT> " +
                  "--name <CHARACTER> --slot <0..7> --map <13|15> --x <WORLD_X> --y <WORLD_Y>");
            return 2;
        }
        catch (OperationCanceledException) { return 4; }
        catch (Exception error) when (error is IOException or InvalidDataException or SqliteException)
        {
            Console.Error.WriteLine("IDENTITY_ADMIN_FAIL: " + error.GetType().Name + ": " + error.Message);
            return 1;
        }
        finally { password = string.Empty; }
    }

    private static Dictionary<string, string> Parse(string[] args, string[] known)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length != known.Length * 2)
            throw new ArgumentException("Supply every documented option exactly once; no password argument is accepted");
        for (int i = 0; i < args.Length; i += 2)
            if (!known.Contains(args[i]) || string.IsNullOrWhiteSpace(args[i + 1]) ||
                !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown, duplicate or empty option");
        return values;
    }

    private static double Coordinate(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ||
            !double.IsFinite(result)) throw new ArgumentException("Coordinates must be finite invariant-culture numbers");
        return result;
    }

    private static string ReadPassword()
    {
        string? supplied = Environment.GetEnvironmentVariable(PasswordEnvironment);
        if (supplied is not null) return supplied;
        if (Console.IsInputRedirected)
            throw new ArgumentException("Non-interactive use requires " + PasswordEnvironment);
        Console.Write("Password (hidden): ");
        var text = new StringBuilder();
        bool exceeded = false;
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                if (exceeded) throw new ArgumentException("Password input exceeded the limit; re-enter without truncation");
                return text.ToString();
            }
            if (key.Key == ConsoleKey.Escape) throw new OperationCanceledException();
            if (exceeded) continue; // Drain through Enter rather than exposing unread tail to the shell.
            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length >= 2 && char.IsLowSurrogate(text[^1]) && char.IsHighSurrogate(text[^2]))
                    text.Length -= 2;
                else if (text.Length > 0) text.Length--;
                continue;
            }
            if (key.KeyChar == '\0') continue;
            if (text.Length >= 256) { exceeded = true; continue; }
            text.Append(key.KeyChar);
        }
    }
}
