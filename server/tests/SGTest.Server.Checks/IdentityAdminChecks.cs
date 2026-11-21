using SGTest.Server.Pvp;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class IdentityAdminChecks
{
    private const string Password = "Admin-Password-123!測試";
    private const string OtherPassword = "Different-Password-456!";
    private const string PfxPassword = "Admin-test-PFX-only";
    private sealed record Context(string Root, string Content, string Database);
    private sealed record Result(int Exit, string Out, string Error);

    public static void Verify(string root, bool tlsOnly = false)
    {
        if (!tlsOnly)
        {
            Checks.Run("Admin CLI creates persistent account/character without retaining plaintext or overwriting duplicates",
                () => Persistence(root).GetAwaiter().GetResult());
            Checks.Run("Admin CLI rejects missing secrets, plaintext options and invalid/blocked original spawn",
                () => InvalidInput(root).GetAwaiter().GetResult());
            Checks.Run("Admin CLI requires account credentials, rejects disabled/duplicate names and preserves owners",
                () => Ownership(root).GetAwaiter().GetResult());
            Checks.Run("Independent admin processes serialize duplicate slots with exactly one created character",
                () => ConcurrentSlot(root).GetAwaiter().GetResult());
            Checks.Run("Local identity-backup CLI creates verified non-overwriting snapshot",
                () => IdentityBackup(root).GetAwaiter().GetResult());
            Checks.Run("Local operator PvP name command is audited idempotent and not a game RPC",
                () => PvpNameAdmin(root).GetAwaiter().GetResult());
        }
        Checks.Run("CLI-provisioned database logs in through real Host serve TLS WorldReady and MoveReceipt",
            () => BootstrapTls(root).GetAwaiter().GetResult());
    }

    private static Context Create(string root)
    {
        string folder = Path.Combine(root, "admin-" + Guid.NewGuid().ToString("N"));
        string content = Path.Combine(folder, "content");
        foreach (int id in new[] { 13, 15 })
        {
            string map = Checks.Fixture(Path.Combine(content, "maps"), id, 4, 4,
                (x,y) => x == 0 && y == 0 ? (byte)7 : (byte)0);
            File.WriteAllText(Path.Combine(map, "npcs.json"), JsonSerializer.Serialize(new
            {
                schema = "sgtest.npcs/v1", coordinate_space = "world_pixels", map_id = id,
                npcs = Array.Empty<object>()
            }));
        }
        return new(folder, content, Path.Combine(folder, "identity.db"));
    }

    private static string[] Account(Context c, string username = "admin_user") =>
        ["account-create", "--identity", c.Database, "--username", username];

    private static string[] Character(Context c, string name = "管理者角色", int slot = 0,
        string username = "admin_user", string x = "24", string y = "24", string map = "15") =>
        ["character-create", "--identity", c.Database, "--username", username, "--content", c.Content,
         "--name", name, "--slot", slot.ToString(), "--map", map, "--x", x, "--y", y];

    private static ProcessStartInfo StartInfo(string? password, string[] args)
    {
        string repository = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(repository, "server", "host")))
            repository = Directory.GetParent(repository)?.FullName ?? throw new Exception("Host build not found");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(repository,
            "server/host/SGTest.Server.Host/bin/Release/net8.0/SGTest.Server.Host.dll"));
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.Environment.Remove("SGTEST_ACCOUNT_PASSWORD");
        if (password is not null) start.Environment["SGTEST_ACCOUNT_PASSWORD"] = password;
        start.Environment["SGTEST_TLS_PASSWORD"] = PfxPassword;
        return start;
    }

    private static async Task<Result> Run(string? password, params string[] args)
    {
        using var process = Process.Start(StartInfo(password, args)) ?? throw new Exception("Host process not started");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        var result = new Result(process.ExitCode, await output, await errors);
        if (!string.IsNullOrEmpty(password))
            Checks.Require(!result.Out.Contains(password) && !result.Error.Contains(password),
                "admin CLI emitted plaintext password");
        return result;
    }

    private static async Task Persistence(string root)
    {
        var c = Create(root);
        var account = await Run(Password, Account(c));
        Checks.Require(account.Exit == 0 && account.Out.Contains("ACCOUNT_CREATED"), "account CLI failed: " + account.Error);
        var character = await Run(Password, Character(c));
        Checks.Require(character.Exit == 0 && character.Out.Contains("CHARACTER_CREATED"), "character CLI failed: " + character.Error);
        var duplicate = await Run(OtherPassword, Account(c, "ADMIN_USER"));
        Checks.Require(duplicate.Exit == 3 && duplicate.Out.Contains("DuplicateUsername"), "duplicate account reset password/identity");
        using var store = new SqliteIdentityStore(c.Database);
        await store.InitializeAsync();
        var auth = await store.AuthenticateAsync("ADMIN_USER", Password);
        var saved = auth.Account?.Characters.SingleOrDefault();
        Checks.Require(auth.Status == AuthenticationStatus.Success && saved is not null &&
            saved.MapId == 15 && saved.X == 24 && saved.Y == 24 && saved.Name == "管理者角色" && saved.Slot == 0,
            "admin-created character did not survive process/store reopen");
        using var document = JsonDocument.Parse(character.Out);
        Checks.Require(saved!.CharacterId == document.RootElement.GetProperty("character_id").GetInt64(), "persistent character ID changed");
        Checks.Require((await store.AuthenticateAsync("admin_user", OtherPassword)).Status == AuthenticationStatus.InvalidCredentials,
            "duplicate account command overwrote existing hash");
        Checks.Require(!Encoding.UTF8.GetString(File.ReadAllBytes(c.Database)).Contains(Password), "database retained plaintext password");
    }

    private static async Task InvalidInput(string root)
    {
        var c = Create(root);
        Checks.Require((await Run(null, Account(c))).Exit == 2 && !File.Exists(c.Database), "missing noninteractive secret created DB");
        Checks.Require((await Run("short", Account(c))).Exit == 2 && !File.Exists(c.Database), "short password created DB");
        Checks.Require((await Run(new string('x',257), Account(c))).Exit == 2 && !File.Exists(c.Database),
            "overlong password created DB or was silently truncated");
        Checks.Require((await Run(Password, Account(c).Concat(["--password", "Forbidden-Cli-Secret"]).ToArray())).Exit == 2,
            "CLI plaintext password option was accepted");
        Checks.Require((await Run(Password, Account(c))).Exit == 0, "input fixture account failed");
        foreach (string x in new[] { "NaN", "Infinity", "-1", "64", "8" })
        {
            string y = x == "8" ? "8" : "24";
            Checks.Require((await Run(Password, Character(c, x:x, y:y))).Exit == 2, "invalid/outside/blocked spawn accepted");
        }
        Checks.Require((await Run(Password, Character(c, map:"14"))).Exit == 2, "unsupported served map accepted");
        Checks.Require((await Run(Password, Character(c, slot:8))).Exit == 2, "out-of-policy slot accepted");
        Checks.Require((await Run(Password, Character(c, name:new string('n',25)))).Exit == 2, "invalid character name accepted");
        string blocking = Path.Combine(c.Content,"maps/15/blocking.dat");
        var bytes = File.ReadAllBytes(blocking);
        bytes[^1] ^= 1;
        File.WriteAllBytes(blocking,bytes);
        Checks.Require((await Run(Password, Character(c))).Exit == 1, "corrupt Blocking source hash accepted");
        using var store = new SqliteIdentityStore(c.Database);
        Checks.Require((await store.AuthenticateAsync("admin_user",Password)).Account?.Characters.Count == 0,
            "refused admin commands created a partial character");
    }

    private static async Task Ownership(string root)
    {
        var c = Create(root);
        Checks.Require((await Run(Password, Account(c))).Exit == 0 &&
            (await Run(OtherPassword, Account(c,"second_user"))).Exit == 0, "ownership fixture account failed");
        Checks.Require((await Run(OtherPassword, Character(c))).Exit == 3, "another account's password was accepted");
        using var store = new SqliteIdentityStore(c.Database);
        long account = (await store.AuthenticateAsync("admin_user",Password)).Account!.AccountId;
        await store.SetDisabledAsync(account,true);
        Checks.Require((await Run(Password, Character(c))).Exit == 3, "disabled account created a character");
        await store.SetDisabledAsync(account,false);
        Checks.Require((await Run(Password, Character(c,name:"UniqueHero"))).Exit == 0, "owner could not create character");
        Checks.Require((await Run(Password, Character(c,name:"DifferentHero"))).Exit == 3, "duplicate slot accepted");
        Checks.Require((await Run(OtherPassword, Character(c,name:"UniqueHero",username:"second_user"))).Exit == 3,
            "globally duplicate name accepted");
        Checks.Require((await store.AuthenticateAsync("admin_user",Password)).Account!.Characters.Count == 1 &&
            (await store.AuthenticateAsync("second_user",OtherPassword)).Account!.Characters.Count == 0,
            "refused ownership/name conflict mutated another account");
    }

    private static async Task ConcurrentSlot(string root)
    {
        var c = Create(root);
        Checks.Require((await Run(Password,Account(c))).Exit == 0, "concurrency fixture account failed");
        var results = await Task.WhenAll(Run(Password,Character(c,name:"FirstHero")), Run(Password,Character(c,name:"SecondHero")));
        Checks.Require(results.Count(r=>r.Exit==0)==1 && results.Count(r=>r.Exit==3)==1, "cross-process slot race did not serialize");
        using var store = new SqliteIdentityStore(c.Database);
        Checks.Require((await store.AuthenticateAsync("admin_user",Password)).Account!.Characters.Count == 1,
            "slot race created duplicate/missing persistent character");
    }


    private static async Task IdentityBackup(string root)
    {
        var c = Create(root);
        Checks.Require((await Run(Password, Account(c))).Exit == 0,
            "backup CLI fixture account failed");
        string backup = Path.Combine(c.Root, "operator-backup.db");
        var result = await Run(null, "identity-backup",
            "--identity", c.Database, "--output", backup);
        Checks.Require(result.Exit == 0 && result.Out.Contains("IDENTITY_BACKUP_CREATED") &&
            File.Exists(backup), "identity-backup CLI failed: " + result.Error);
        using (var document = JsonDocument.Parse(result.Out))
        {
            var rootElement = document.RootElement;
            Checks.Require(rootElement.GetProperty("schema_version").GetInt32() ==
                    SqliteIdentityStore.SchemaVersion &&
                rootElement.GetProperty("bytes").GetInt64() > 0 &&
                rootElement.GetProperty("sha256").GetString()?.Length == 64,
                "identity-backup CLI metadata invalid");
        }
        using (var snapshot = new SqliteIdentityStore(backup))
        {
            await snapshot.InitializeAsync();
            Checks.Require((await snapshot.AuthenticateAsync(
                "admin_user", Password)).Status == AuthenticationStatus.Success,
                "CLI backup did not preserve account authentication");
        }
        Checks.Require((await Run(null, "identity-backup",
            "--identity", c.Database, "--output", backup)).Exit == 1,
            "identity-backup CLI overwrote existing destination");
        Checks.Require((await Run(null, "identity-backup",
            "--identity", c.Database, "--output", c.Database)).Exit == 2,
            "identity-backup CLI accepted source as destination");
    }

    private static string[] PvpName(Context c,long characterId,string state,string eventId,
        string reason="manual moderation",string operatorId="ci-operator") =>
        ["pvp-name-set","--identity",c.Database,
         "--character-id",characterId.ToString(),
         "--state",state,"--operator",operatorId,"--reason",reason,"--event-id",eventId];

    private static async Task PvpNameAdmin(string root)
    {
        var c=Create(root);
        Checks.Require((await Run(Password,Account(c))).Exit==0,
            "PvP admin fixture account failed");
        var created=await Run(Password,Character(c,name:"PvpAdminHero"));
        Checks.Require(created.Exit==0,"PvP admin fixture character failed");
        using var document=JsonDocument.Parse(created.Out);
        long character=document.RootElement.GetProperty("character_id").GetInt64();

        var red=await Run(null,PvpName(c,character,"red","evt-red-001"));
        Checks.Require(red.Exit==0&&red.Out.Contains("PVP_NAME_SET")&&
            red.Out.Contains("\"replayed\":false"),
            "local PvP name red command failed: "+red.Error);
        using(var store=new SqliteIdentityStore(c.Database))
        {
            await store.InitializeAsync();
            var state=await store.GetCharacterPvpStateAsync(character);
            Checks.Require(state.RedMan&&PvpCrimePolicy.NameState(
                state,DateTimeOffset.UtcNow)==PvpNameState.Red,
                "admin red command did not persist authoritative state");
        }

        var replay=await Run(null,PvpName(c,character,"red","evt-red-001"));
        Checks.Require(replay.Exit==0&&replay.Out.Contains("\"replayed\":true"),
            "exact admin event replay was not idempotent");
        var conflict=await Run(null,PvpName(c,character,"blue","evt-red-001"));
        Checks.Require(conflict.Exit==3&&conflict.Error.Contains("event ID"),
            "conflicting reuse of admin event ID was accepted");
        var pink=await Run(null,PvpName(c,character,"flash","evt-flash-002",
            reason:"incident review",operatorId:"gm-local-1"));
        Checks.Require(pink.Exit==0&&pink.Out.Contains("PinkFlashing"),
            "English flash token failed");
        var localizedToken=string.Concat((char)0x9583,(char)0x7c89);
        Checks.Require(
            (await Run(null,PvpName(
                c,character,localizedToken,"evt-localized-rejected"))).Exit==2,
            "localized PvP state token leaked into Server admin contract");

        using(var connection=new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {DataSource=c.Database,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,Pooling=false}.ToString()))
        {
            connection.Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT COUNT(*),MIN(operator_id),MAX(reason)
FROM pvp_name_admin_events WHERE character_id=$id;";
            command.Parameters.AddWithValue("$id",character);
            using var reader=command.ExecuteReader();
            Checks.Require(reader.Read()&&reader.GetInt64(0)==2,
                "admin replay/conflict created duplicate audit rows");
        }
        Checks.Require((await Run(null,PvpName(c,character,"purple","evt-bad"))).Exit==2,
            "invalid PvP admin state accepted");
        Checks.Require((await Run(null,PvpName(c,character,"blue","evt-empty",reason:" "))).Exit==2,
            "empty PvP admin reason accepted");
        Checks.Require((await Run(null,PvpName(c,character+9999,"white","evt-missing"))).Exit==3,
            "unknown PvP admin character accepted");
    }

    private static async Task BootstrapTls(string root)
    {
        var c = Create(root);
        Checks.Require((await Run(Password,Account(c))).Exit == 0 &&
            (await Run(Password,Character(c))).Exit == 0, "bootstrap provisioning failed");
        using var certificate = TlsSocketChecks.CreateCertificate();
        string pfx = Path.Combine(c.Root,"test-only.pfx");
        File.WriteAllBytes(pfx,certificate.Export(X509ContentType.Pfx,PfxPassword));
        using var server = Process.Start(StartInfo(null,["serve","--content",c.Content,"--identity",c.Database,
            "--certificate",pfx,"--port","0","--seconds","8"])) ?? throw new Exception("Host not started");
        server.StandardInput.Close();
        var errors = server.StandardError.ReadToEndAsync();
        try
        {
            string? line = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            using var readyDocument = JsonDocument.Parse(line ?? "{}");
            Checks.Require(readyDocument.RootElement.GetProperty("result").GetString()=="TLS_HOST_READY", "real Host was not ready");
            var endpoint = IPEndPoint.Parse(readyDocument.RootElement.GetProperty("endpoint").GetString()!);
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(endpoint);
                using var tls = new SslStream(client.GetStream(),false,(_,cert,_,_)=>cert?.GetCertHashString()==certificate.GetCertHashString());
                await TlsSocketChecks.AuthenticateClient(tls);
                await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("admin_user",Password)));
                Checks.Require(WireProtocol.TryDecodeAuthResult(await Read(tls),out var auth) && auth.Status==AuthWireStatus.Success,
                    "CLI-created password did not authenticate to Host");
                Checks.Require(WireProtocol.TryDecodeCharacterList(await Read(tls),out var list) && list.Characters.Count==1,
                    "Host did not list CLI-created character");
                await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(list.Characters[0].CharacterId)));
                Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(await Read(tls),out var choose) &&
                    choose.Status==CharacterChooseWireStatus.Success && choose.MapId==15 && choose.X==24 && choose.Y==24,
                    "Host did not enter explicit validated spawn");
                Checks.Require(WireProtocol.TryDecodeInventorySnapshot(await Read(tls),out var inventory) &&
                    inventory is not null && inventory.CharacterId==choose.CharacterId &&
                    inventory.StartIndex==0 && inventory.Complete &&
                    inventory.Items.Count==0,
                    "fresh CLI character did not receive complete empty inventory baseline");
                Checks.Require(WireProtocol.TryDecodeWorldReady(await Read(tls),out var world) && world.ReceiverMapEpoch==choose.MapEpoch,
                    "Host did not deliver WorldReady after provisioned login");
                await tls.WriteAsync(WireProtocol.EncodeWorldReadyAck(
                    new(world.ReceiverMapEpoch, world.ServerTick)));
                await tls.WriteAsync(WireProtocol.EncodeMoveTarget(new(choose.MapEpoch,1,40,24)));
                bool receipt = false;
                for(int i=0;i<16 && !receipt;i++)
                    receipt = WireProtocol.TryDecodeMoveReceipt(await Read(tls),out var move) && move.Status==MoveReceiptStatus.Accepted && move.MovementSequence==1;
                Checks.Require(receipt,"real Host did not accept and acknowledge provisioned player's Move");
            }
            var output = server.StandardOutput.ReadToEndAsync();
            await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            string stopped = await output;
            Checks.Require(server.ExitCode==0 && stopped.Contains("TLS_HOST_STOPPED") &&
                stopped.Contains("\"ActiveConnections\":0") && stopped.Contains("\"ActiveActors\":0") &&
                stopped.Contains("\"ActiveSessions\":0"), "Host did not clean provisioned login: " + await errors);
            Checks.Require(!stopped.Contains(Password) && !(await errors).Contains(Password), "Host emitted credential");
        }
        finally
        {
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree:true);
                await server.WaitForExitAsync();
            }
        }
    }

    private static async Task<byte[]> Read(SslStream tls)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] header = new byte[8];
        await tls.ReadExactlyAsync(header,timeout.Token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Checks.Require(length is >=8 and <=4096,"TLS frame bound");
        byte[] frame = new byte[length];
        header.CopyTo(frame,0);
        await tls.ReadExactlyAsync(frame.AsMemory(8),timeout.Token);
        return frame;
    }
}
