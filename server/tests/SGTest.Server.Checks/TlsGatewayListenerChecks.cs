using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class TlsGatewayListenerChecks
{
    public static void Verify(string root)
    {
        Checks.Run("TLS listener rejects invalid bounds and public-only certificate before binding",
            () => InvalidConfiguration(root).GetAwaiter().GetResult());
        Checks.Run("TLS listener global capacity and cancelled slow handshake release socket and slot",
            () => Admission(root, false).GetAwaiter().GetResult());
        Checks.Run("TLS listener per-address capacity refuses without allocating another session",
            () => Admission(root, true).GetAwaiter().GetResult());
        Checks.Run("TLS listener authenticates reconnect, WorldReady, MoveReceipt and cleans actors on shutdown",
            () => AuthenticatedReconnect(root).GetAwaiter().GetResult());
        Checks.Run("verified recovery TLS request atomically changes HP and last stack, then relogin persists",
            () => AuthenticatedRecovery(root).GetAwaiter().GetResult());
        Checks.Run("inventory discard TLS request atomically removes owned stack and relogin persists",
            () => AuthenticatedInventoryDiscard(root).GetAwaiter().GetResult());
        Checks.Run("Plaintext and invalid credentials close only that client and release admission",
            () => UntrustedClients(root).GetAwaiter().GetResult());
        Checks.Run("TLS session infrastructure failure stops admission and is observable by supervisor",
            () => SessionFailure(root).GetAwaiter().GetResult());
        Checks.Run("Host serve boots TLS on synthetic maps, stops cleanly and rejects invalid CLI",
            () => HostEntry(root).GetAwaiter().GetResult());
        Checks.Run("20 distinct accounts use real TLS listener auth, choose, AOI and move receipt with cleanup",
            () => AuthenticatedTwentyClients(root).GetAwaiter().GetResult());
    }

    private static async Task InvalidConfiguration(string root)
    {
        await using var fixture = await Fixture.Create(root);
        bool bounds = false, certificate = false;
        try { await using var invalid = fixture.Listener(new(MaxConnections: 0)); }
        catch (ArgumentOutOfRangeException) { bounds = true; }
        using var publicOnly = new X509Certificate2(fixture.Certificate.Export(X509ContentType.Cert));
        try
        {
            await using var invalid = new TlsGatewayListener(IPAddress.Loopback, 0,
                fixture.Authorization, publicOnly, fixture.Router);
        }
        catch (ArgumentException) { certificate = true; }
        Checks.Require(bounds && certificate, "listener accepted unsafe deployment configuration");
    }

    private static async Task Admission(string root, bool perAddress)
    {
        await using var fixture = await Fixture.Create(root);
        await using var listener = fixture.Listener(new(MaxConnections: perAddress ? 2 : 1,
            MaxConnectionsPerAddress: 1));
        using var stop = new CancellationTokenSource();
        var run = listener.RunAsync(stop.Token);
        var endpoint = listener.LocalEndpoint;
        using var first = new TcpClient();
        await first.ConnectAsync(endpoint.Address, endpoint.Port);
        await Until(() => listener.Statistics.ActiveConnections == 1);
        using var refused = new TcpClient();
        await refused.ConnectAsync(endpoint.Address, endpoint.Port);
        await Until(() => perAddress ? listener.Statistics.RejectedAddress == 1
                                    : listener.Statistics.RejectedCapacity == 1);
        Checks.Require(listener.Statistics.AcceptedConnections == 1 &&
            listener.Statistics.ActiveAddresses == 1, "rejection allocated TLS/identity session");
        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Checks.Require(listener.Statistics.ActiveConnections == 0 &&
            listener.Statistics.ActiveAddresses == 0 && listener.Statistics.CompletedConnections == 1 &&
            fixture.Router.Statistics.ActiveSessions == 0, "slow handshake leaked admission capacity");
        using var replacement = new TcpListener(endpoint);
        replacement.Start();
        replacement.Stop();
    }

    private static async Task AuthenticatedReconnect(string root)
    {
        await using var fixture = await Fixture.Create(root);
        await using var listener = fixture.Listener(new(MaxConnections: 2, MaxConnectionsPerAddress: 2));
        using var stop = new CancellationTokenSource();
        var run = listener.RunAsync(stop.Token);
        long previousEpoch = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(listener.LocalEndpoint);
            using var tls = new SslStream(client.GetStream(), false, (_, cert, _, _) =>
                cert?.GetCertHashString() == fixture.Certificate.GetCertHashString());
            await TlsSocketChecks.AuthenticateClient(tls);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("listener_user", "Listener-Password-123!")));
            Checks.Require(WireProtocol.TryDecodeAuthResult(await Read(tls), out var auth) &&
                auth.Status == AuthWireStatus.Success, "listener Auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(await Read(tls), out var list) &&
                list.Characters.Count == 1, "listener character ownership/list failed");
            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(fixture.CharacterId)));
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(await Read(tls), out var choose) &&
                choose.Status == CharacterChooseWireStatus.Success && choose.MapEpoch > previousEpoch,
                "listener reconnect did not advance generation");
            previousEpoch = choose.MapEpoch;
            // The authoritative DB baseline always precedes WorldReady.
            var baseline=await Read(tls);
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(baseline,out var loaded) &&
                loaded is not null && loaded.CharacterId==fixture.CharacterId &&
                loaded.StartIndex==0 && loaded.Complete &&
                loaded.Items.Count==2 &&
                loaded.Items.Select(x=>x.ItemUid).OrderBy(x=>x)
                    .SequenceEqual(fixture.InventoryUids.OrderBy(x=>x)),
                "TLS reconnect did not reload the same two persistent inventory UIDs");
            Checks.Require(WireProtocol.TryDecodeWorldReady(await Read(tls), out var ready) &&
                ready.ActorId == fixture.CharacterId && ready.ReceiverMapEpoch == choose.MapEpoch,
                "listener did not deliver authoritative WorldReady");
            await tls.WriteAsync(WireProtocol.EncodeWorldReadyAck(
                new(ready.ReceiverMapEpoch, ready.ServerTick)));
            await tls.WriteAsync(WireProtocol.EncodeMoveTarget(new(choose.MapEpoch, 1, 24, 8)));
            bool receipt = false;
            for (int frame = 0; frame < 16 && !receipt; frame++)
            {
                var bytes = await Read(tls);
                receipt = WireProtocol.TryDecodeMoveReceipt(bytes, out var value) &&
                    value.MovementSequence == 1 && value.Status == MoveReceiptStatus.Accepted;
            }
            Checks.Require(receipt, "listener failed to route reliable MoveReceipt");
            if (attempt == 1)
            {
                stop.Cancel(); // still-authenticated socket; server must release actor first
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                await tls.DisposeAsync();
                await Until(() => listener.Statistics.ActiveConnections == 0 &&
                    fixture.Host.Statistics.ActiveActors == 0);
            }
        }
        var stats = listener.Statistics;
        Checks.Require(stats.AcceptedConnections == 2 && stats.CompletedConnections == 2 &&
            stats.ActiveConnections == 0 && stats.ActiveAddresses == 0 && stats.CommandsAccepted == 2 &&
            fixture.Host.Statistics.ActiveActors == 0 && fixture.Router.Statistics.ActiveSessions == 0,
            "shutdown left authenticated actor/socket/route or incorrect command accounting");
        await Task.WhenAll(listener.DisposeAsync().AsTask(), listener.DisposeAsync().AsTask());
    }

    private static async Task AuthenticatedRecovery(string root)
    {
        await using var fixture=await Fixture.Create(root,withRecovery:true);
        await using var listener=fixture.Listener(
            new(MaxConnections:2,MaxConnectionsPerAddress:2));
        using var stop=new CancellationTokenSource();
        var run=listener.RunAsync(stop.Token);
        long firstUid=fixture.InventoryUids[0];
        long otherUid=fixture.InventoryUids[1];

        async Task<(SslStream Tls,TcpClient Client,long Epoch)> Login()
        {
            var client=new TcpClient();
            await client.ConnectAsync(listener.LocalEndpoint);
            var tls=new SslStream(client.GetStream(),false,(_,cert,_,_)=>
                cert?.GetCertHashString()==fixture.Certificate.GetCertHashString());
            await TlsSocketChecks.AuthenticateClient(tls);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("listener_user","Listener-Password-123!")));
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                await Read(tls),out var auth) &&
                auth.Status==AuthWireStatus.Success,
                "authenticated recovery login denied");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                await Read(tls),out var characters) &&
                characters.Characters.Count==1,
                "recovery login character list missing");
            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(fixture.CharacterId)));
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                await Read(tls),out var selected) &&
                selected.Status==CharacterChooseWireStatus.Success,
                "recovery login failed to choose character");
            Checks.Require(WireProtocol.TryDecodePlayerCombatState(
                await Read(tls),out var hp) &&
                hp.MapEpoch==selected.MapEpoch &&
                hp.CurrentHp is 60 or 100 && hp.MaxHp==100,
                "recovery did not fetch durable current HP on login");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await Read(tls),out var inventory) &&
                inventory is not null && inventory.Complete,
                "recovery inventory baseline missing");
            Checks.Require(WireProtocol.TryDecodeWorldReady(
                await Read(tls),out var ready) &&
                ready.ReceiverMapEpoch==selected.MapEpoch,
                "recovery player WorldReady missing");
            await tls.WriteAsync(WireProtocol.EncodeWorldReadyAck(
                new(ready.ReceiverMapEpoch,ready.ServerTick)));
            return (tls,client,selected.MapEpoch);
        }

        async Task<RecoveryUseResultWire> ReadRecovery(SslStream tls)
        {
            for(int i=0;i<32;i++)
            {
                var frame=await Read(tls);
                if(WireProtocol.TryDecodeRecoveryUseResult(frame,out var result))
                    return result;
            }
            throw new Exception("Recovery use result did not arrive in reliable FIFO");
        }

        var first=await Login();
        try
        {
            await first.Tls.WriteAsync(WireProtocol.EncodeRecoveryUse(
                new(first.Epoch,1,firstUid)));
            var granted=await ReadRecovery(first.Tls);
            Checks.Require(granted.Status==RecoveryUseStatus.Granted &&
                granted.ItemUid==firstUid && granted.ItemDefinitionId==1001 &&
                granted.RemainingQuantity==1 && granted.CurrentHp==90 &&
                granted.MaxHp==100,
                "authenticated first potion did not restore one amount");
            await first.Tls.WriteAsync(WireProtocol.EncodeRecoveryUse(
                new(first.Epoch,1,firstUid)));
            var repeated=await ReadRecovery(first.Tls);
            Checks.Require(repeated.Status==RecoveryUseStatus.AlreadyGranted &&
                repeated.RemainingQuantity==1 && repeated.CurrentHp==90,
                "TLS replay duplicated recovery consumption");
            await first.Tls.WriteAsync(WireProtocol.EncodeRecoveryUse(
                new(first.Epoch,2,firstUid)));
            var emptied=await ReadRecovery(first.Tls);
            Checks.Require(emptied.Status==RecoveryUseStatus.Granted &&
                emptied.RemainingQuantity==0 && emptied.CurrentHp==100,
                "TLS second potion did not consume last stack");
            Checks.Require((await fixture.Store.GetCharacterCombatProfileAsync(
                fixture.CharacterId))?.Data.CurrentHp==100 &&
                (await fixture.Store.ListInventoryItemsAsync(
                    fixture.CharacterId)).Select(x=>x.ItemUid)
                    .SequenceEqual(new[]{otherUid}),
                "recovery granted wrong persistent inventory/HP after TLS action");
        }
        finally
        {
            await first.Tls.DisposeAsync();
            first.Client.Dispose();
        }
        await Until(()=>listener.Statistics.ActiveConnections==0);
        var second=await Login();
        await second.Tls.DisposeAsync();
        second.Client.Dispose();
        await Until(()=>listener.Statistics.ActiveConnections==0);
        Checks.Require((await fixture.Store.ListInventoryItemsAsync(
            fixture.CharacterId)).Count==1,
            "relogin minted consumed recovery stack");
        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task AuthenticatedInventoryDiscard(string root)
    {
        await using var fixture=await Fixture.Create(root);
        await using var listener=fixture.Listener(
            new(MaxConnections:2,MaxConnectionsPerAddress:2));
        using var stop=new CancellationTokenSource();
        var run=listener.RunAsync(stop.Token);
        long firstUid=fixture.InventoryUids[0];
        long otherUid=fixture.InventoryUids[1];

        async Task<(SslStream Tls,TcpClient Client,long Epoch,InventorySnapshotWire Inventory)> Login()
        {
            var client=new TcpClient();
            await client.ConnectAsync(listener.LocalEndpoint);
            var tls=new SslStream(client.GetStream(),false,(_,cert,_,_)=>
                cert?.GetCertHashString()==fixture.Certificate.GetCertHashString());
            await TlsSocketChecks.AuthenticateClient(tls);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("listener_user","Listener-Password-123!")));
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                await Read(tls),out var auth) &&
                auth.Status==AuthWireStatus.Success,
                "authenticated discard login denied");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                await Read(tls),out var characters) &&
                characters.Characters.Count==1,
                "discard login character list missing");
            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(
                new(fixture.CharacterId)));
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                await Read(tls),out var selected) &&
                selected.Status==CharacterChooseWireStatus.Success,
                "discard login failed to choose character");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await Read(tls),out var inventory) &&
                inventory is not null && inventory.Complete,
                "discard inventory baseline missing");
            Checks.Require(WireProtocol.TryDecodeWorldReady(
                await Read(tls),out var ready) &&
                ready.ReceiverMapEpoch==selected.MapEpoch,
                "discard player WorldReady missing");
            await tls.WriteAsync(WireProtocol.EncodeWorldReadyAck(
                new(ready.ReceiverMapEpoch,ready.ServerTick)));
            return (tls,client,selected.MapEpoch,inventory!);
        }

        async Task<InventoryDiscardResultWire> ReadDiscard(SslStream tls)
        {
            for(int i=0;i<32;i++)
            {
                var frame=await Read(tls);
                if(WireProtocol.TryDecodeInventoryDiscardResult(frame,out var result))
                    return result;
            }
            throw new Exception(
                "Inventory discard result did not arrive in reliable FIFO");
        }

        var first=await Login();
        try
        {
            Checks.Require(first.Inventory.Items.Count==2 &&
                first.Inventory.Items.Any(x=>x.ItemUid==firstUid&&x.Quantity==2) &&
                first.Inventory.Items.Any(x=>x.ItemUid==otherUid&&x.Quantity==3),
                "discard login baseline did not expose durable owned stacks");
            await first.Tls.WriteAsync(WireProtocol.EncodeInventoryDiscard(
                new(first.Epoch,1,firstUid,1)));
            var applied=await ReadDiscard(first.Tls);
            Checks.Require(applied.Status==InventoryDiscardStatus.Applied &&
                applied.ItemUid==firstUid && applied.ItemDefinitionId==2701 &&
                applied.DiscardedQuantity==1 && applied.RemainingQuantity==1,
                "authenticated discard did not remove exactly one owned unit");
            await first.Tls.WriteAsync(WireProtocol.EncodeInventoryDiscard(
                new(first.Epoch,1,firstUid,1)));
            var replay=await ReadDiscard(first.Tls);
            Checks.Require(replay.Status==InventoryDiscardStatus.AlreadyApplied &&
                replay.DiscardedQuantity==1 && replay.RemainingQuantity==1,
                "TLS discard replay consumed another unit");
            await first.Tls.WriteAsync(WireProtocol.EncodeInventoryDiscard(
                new(first.Epoch,2,firstUid,1)));
            var emptied=await ReadDiscard(first.Tls);
            Checks.Require(emptied.Status==InventoryDiscardStatus.Applied &&
                emptied.RemainingQuantity==0,
                "TLS second discard did not remove final stack unit");
            var persisted=await fixture.Store.ListInventoryItemsAsync(
                fixture.CharacterId);
            Checks.Require(persisted.Count==1 &&
                persisted[0].ItemUid==otherUid && persisted[0].Quantity==3,
                "discard mutated wrong persistent inventory UID/quantity");
        }
        finally
        {
            await first.Tls.DisposeAsync();
            first.Client.Dispose();
        }

        await Until(()=>listener.Statistics.ActiveConnections==0);
        var second=await Login();
        try
        {
            Checks.Require(second.Inventory.Items.Count==1 &&
                second.Inventory.Items[0].ItemUid==otherUid &&
                second.Inventory.Items[0].Quantity==3,
                "relogin inventory baseline resurrected discarded stack");
        }
        finally
        {
            await second.Tls.DisposeAsync();
            second.Client.Dispose();
        }
        await Until(()=>listener.Statistics.ActiveConnections==0);
        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task SessionFailure(string root)
    {
        await using var fixture = await Fixture.Create(root);
        var listener = fixture.Listener(new(MaxConnections: 2, MaxConnectionsPerAddress: 2));
        var run = listener.RunAsync();
        var endpoint = listener.LocalEndpoint;
        // Deliberately invalidate the borrowed certificate after startup. This
        // models an infrastructure fault, not an untrusted malformed packet.
        fixture.Certificate.Dispose();
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint);
        bool failed = false;
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception error) when (error is CryptographicException or InvalidOperationException or ObjectDisposedException)
        { failed = true; }
        finally
        {
            try { await listener.DisposeAsync(); }
            catch (Exception error) when (error is CryptographicException or InvalidOperationException or ObjectDisposedException) { }
        }
        Checks.Require(failed && listener.Statistics.ActiveConnections == 0 &&
            listener.Statistics.ActiveAddresses == 0 && fixture.Router.Statistics.ActiveSessions == 0,
            "session failure was hidden or left open admission/socket/route");
        using var replacement = new TcpListener(endpoint);
        replacement.Start();
        replacement.Stop();
    }

    private static async Task UntrustedClients(string root)
    {
        await using var fixture = await Fixture.Create(root);
        await using var listener = fixture.Listener(new(MaxConnections: 2, MaxConnectionsPerAddress: 2,
            Connection: new(HandshakeTimeoutMilliseconds: 500, AuthorizationTimeoutMilliseconds: 1000)));
        using var stop = new CancellationTokenSource();
        var run = listener.RunAsync(stop.Token);
        using (var plaintext = new TcpClient())
        {
            await plaintext.ConnectAsync(listener.LocalEndpoint);
            await plaintext.GetStream().WriteAsync("not-a-TLS-client"u8.ToArray());
        }
        await Until(() => listener.Statistics.CompletedConnections == 1);
        Checks.Require(!run.IsCompleted, "untrusted plaintext stopped all admission");
        using (var denied = new TcpClient())
        {
            await denied.ConnectAsync(listener.LocalEndpoint);
            using var tls = new SslStream(denied.GetStream(), false, (_, cert, _, _) =>
                cert?.GetCertHashString() == fixture.Certificate.GetCertHashString());
            await TlsSocketChecks.AuthenticateClient(tls);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("listener_user", "Wrong-Password-123!")));
            Checks.Require(WireProtocol.TryDecodeAuthResult(await Read(tls), out var result) &&
                result.Status == AuthWireStatus.InvalidCredentials, "invalid login was not rejected");
        }
        await Until(() => listener.Statistics.CompletedConnections == 2);
        Checks.Require(!run.IsCompleted && listener.Statistics.ActiveConnections == 0 &&
            fixture.Router.Statistics.ActiveSessions == 0 && fixture.Host.Statistics.ActiveActors == 0,
            "invalid login killed listener or left untrusted actor/socket");
        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task HostEntry(string root)
    {
        await using var fixture = await Fixture.Create(root);
        string content = Path.Combine(root, "listener-host-content");
        foreach (int id in new[] { 13, 15 })
        {
            string path = Checks.Fixture(Path.Combine(content, "maps"), id, 4, 4, (_, _) => 0);
            File.WriteAllText(Path.Combine(path, "npcs.json"), JsonSerializer.Serialize(new
            {
                schema = "sgtest.npcs/v1", coordinate_space = "world_pixels", map_id = id,
                npcs = Array.Empty<object>()
            }));
        }
        string pfx = Path.Combine(content, "test-only.pfx");
        File.WriteAllBytes(pfx, fixture.Certificate.Export(X509ContentType.Pfx, "Listener-PFX-test-only"));
        string repository = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(repository, "server", "host")))
            repository = Directory.GetParent(repository)?.FullName ?? throw new Exception("Host build not found");
        string hostDll = Path.Combine(repository, "server/host/SGTest.Server.Host/bin/Release/net8.0/SGTest.Server.Host.dll");
        async Task<(int Exit, string Out, string Error)> Run(params string[] args)
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(hostDll);
            foreach (string arg in args) start.ArgumentList.Add(arg);
            start.Environment["SGTEST_TLS_PASSWORD"] = "Listener-PFX-test-only";
            using var process = Process.Start(start) ?? throw new Exception("Host process not started");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            catch { process.Kill(entireProcessTree: true); throw; }
            return (process.ExitCode, await output, await errors);
        }
        var valid = await Run("serve", "--content", content, "--identity", Path.Combine(content, "identity.db"),
            "--certificate", pfx, "--port", "0", "--seconds", "1");
        Checks.Require(valid.Exit == 0 && valid.Out.Contains("TLS_HOST_READY") &&
            valid.Out.Contains("TLS_HOST_STOPPED") && valid.Out.Contains("\"ActiveConnections\":0"),
            "serve startup/shutdown failed: " + valid.Error);
        // Runtime startup must explicitly opt into manually audited recovery
        // data. A client-derived name or unverified .tres never enables heals.
        string rules=Path.Combine(content,"recovery-verified.json");
        File.WriteAllText(rules,JsonSerializer.Serialize(new {
            kind="verified_recovery_item_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="ci:synthetic-rule-reviewed",
            rules=new[]{new{item_definition_id=1001,restore_hp=140,required_level=1}}
        }));
        var enabled=await Run("serve","--content",content,"--identity",
            Path.Combine(content,"identity.db"),"--certificate",pfx,
            "--port","0","--recovery-rules",rules,"--seconds","1");
        Checks.Require(enabled.Exit==0 && enabled.Out.Contains("TLS_HOST_READY") &&
            enabled.Out.Contains("\"verified_recovery_rule_count\":1"),
            "verified recovery manifest was not admitted by formal Host: "+enabled.Error);
        string unverified=Path.Combine(content,"recovery-unverified.json");
        File.WriteAllText(unverified,JsonSerializer.Serialize(new {
            kind="verified_recovery_item_rules",version=1,
            rules_verified=false,gameplay_ready=true,
            source_reference="ci:unverified",
            rules=new[]{new{item_definition_id=1001,restore_hp=140,required_level=1}}
        }));
        var denied=await Run("serve","--content",content,"--identity",
            Path.Combine(content,"identity.db"),"--certificate",pfx,
            "--port","0","--recovery-rules",unverified,"--seconds","1");
        Checks.Require(denied.Exit==1 && !denied.Out.Contains("TLS_HOST_READY"),
            "unverified recovery rule file started a production listener");

        var invalid = await Run("serve", "--content", content, "--identity", Path.Combine(content, "identity.db"),
            "--certificate", pfx, "--bind", "not-an-ip");
        Checks.Require(invalid.Exit == 2 && !invalid.Out.Contains("TLS_HOST_READY"),
            "invalid host configuration opened a listener");
    }


    /// <summary>
    /// End-to-end admission through the real listener and SQLite identity.
    /// Unlike the earlier pre-bound 20-TLS AOI resync test, each independent
    /// client must authenticate its own account, select its own character,
    /// and verify every other actor appears through the shared AOI pipeline.
    /// This is a synthetic 20-client .NET loopback test, not 20 Godot renderers.
    /// </summary>
    private static async Task AuthenticatedTwentyClients(string root)
    {
        const int players = 20;
        const string password = "Listener-Password-123!";
        await using var fixture = await Fixture.Create(root);
        var accounts = new (string Username, long CharacterId)[players];
        accounts[0] = ("listener_user", fixture.CharacterId);
        for (int i = 1; i < players; i++)
        {
            string username = $"load_user_{i:D2}";
            long accountId = (await fixture.Store.CreateAccountAsync(username, password))
                .AccountId;
            var character = await fixture.Store.CreateCharacterAsync(
                accountId, 0, $"LoadHero{i:D2}", new CharacterSpawn(115, 8, 8));
            accounts[i] = (username, character.Character?.CharacterId ??
                throw new Exception("20-client synthetic character creation failed"));
        }
        Checks.Require(accounts.Select(x => x.CharacterId).Distinct().Count() == players,
            "load accounts share a CharacterId");

        await using var listener = fixture.Listener(new(
            MaxConnections: players, MaxConnectionsPerAddress: players, Backlog: 32,
            Connection: new TlsWireConnectionOptions(
                HandshakeTimeoutMilliseconds: 10000,
                AuthorizationTimeoutMilliseconds: 30000)));
        using var stop = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var run = listener.RunAsync(stop.Token);
        var allRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int readCount = 0;
        long transferredBytes = 0;
        int receivedSpawns = 0;
        int receipts = 0;
        var readyDurations = new double[players];
        try
        {
            async Task RunPlayer(int index)
            {
                long started = Stopwatch.GetTimestamp();
                using var client = new TcpClient(AddressFamily.InterNetwork);
                await client.ConnectAsync(listener.LocalEndpoint, deadline.Token);
                // Pin the fixture's ephemeral self-signed certificate instead
                // of disabling certificate validation for this E2E login.
                using var tls = new SslStream(client.GetStream(), false,
                    (_, certificate, _, _) =>
                        certificate?.GetCertHashString() ==
                        fixture.Certificate.GetCertHashString());
                await TlsSocketChecks.AuthenticateClient(tls)
                    .WaitAsync(deadline.Token);
                async Task<byte[]> Receive()
                {
                    var header = new byte[WireProtocol.HeaderBytes];
                    await tls.ReadExactlyAsync(header, deadline.Token);
                    int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                    Checks.Require(length >= WireProtocol.HeaderBytes &&
                        length <= WireProtocol.MaximumFrameBytes,
                        $"authenticated client {index} invalid TLS frame length");
                    var frame = new byte[length];
                    header.CopyTo(frame, 0);
                    await tls.ReadExactlyAsync(frame.AsMemory(header.Length), deadline.Token);
                    Checks.Require(WireProtocol.TryReadHeader(frame, out _),
                        $"authenticated client {index} invalid compact header");
                    Interlocked.Add(ref transferredBytes, length);
                    return frame;
                }

                await tls.WriteAsync(WireProtocol.EncodeAuthRequest(
                    new(accounts[index].Username, password)), deadline.Token);
                Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await Receive(), out var auth) && auth.Status == AuthWireStatus.Success,
                    $"load authentication failed for account {index}");
                Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await Receive(), out var list) &&
                    list.Characters.Count == 1 &&
                    list.Characters[0].CharacterId == accounts[index].CharacterId,
                    $"load account {index} character ownership leaked");
                await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(
                    new(accounts[index].CharacterId)), deadline.Token);
                Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await Receive(), out var choice) &&
                    choice.Status == CharacterChooseWireStatus.Success &&
                    choice.CharacterId == accounts[index].CharacterId &&
                    choice.MapId == 115, $"load character {index} choose failed");
                Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                    await Receive(), out var inventory) &&
                    inventory.CharacterId == accounts[index].CharacterId &&
                    inventory.Complete &&
                    inventory.Items.Count == (index == 0 ? 2 : 0),
                    $"load inventory {index} was not owned by the selected actor");

                // Every session receives its own WorldReady generation, then
                // ACKs on the real parser before posting an ordinary MoveTarget.
                var seen = new HashSet<long>();
                bool worldReady = false;
                bool receiptSeen = false;
                bool selfStateSeen = false;
                int frames = 0;
                while (!worldReady || !receiptSeen || !selfStateSeen ||
                    seen.Count < players - 1)
                {
                    var frame = await Receive();
                    var valid = WireProtocol.TryReadHeader(frame, out var header);
                    Checks.Require(valid && ++frames <= 128,
                        $"load client {index} AOI frame limit exceeded");
                    switch (header.Opcode)
                    {
                        case WireOpcode.WorldReady:
                            Checks.Require(!worldReady &&
                                WireProtocol.TryDecodeWorldReady(frame, out var ready) &&
                                ready.ActorId == choice.CharacterId &&
                                ready.ReceiverMapEpoch == choice.MapEpoch,
                                $"load client {index} received an invalid WorldReady");
                            worldReady = true;
                            await tls.WriteAsync(WireProtocol.EncodeWorldReadyAck(
                                new(choice.MapEpoch, ready.ServerTick)), deadline.Token);
                            await tls.WriteAsync(WireProtocol.EncodeMoveTarget(
                                new(choice.MapEpoch, 1, 24, 8)), deadline.Token);
                            break;
                        case WireOpcode.SelfState:
                            Checks.Require(worldReady &&
                                WireProtocol.TryDecodeEntityState(
                                    frame, out _, out var self) &&
                                self.ActorId == choice.CharacterId &&
                                self.ReceiverMapEpoch == choice.MapEpoch,
                                $"load client {index} received foreign self state");
                            selfStateSeen = true;
                            break;
                        case WireOpcode.EntitySpawn:
                            Checks.Require(worldReady &&
                                WireProtocol.TryDecodeEntityState(
                                    frame, out _, out var peer) &&
                                peer.ReceiverMapEpoch == choice.MapEpoch &&
                                peer.ActorId != choice.CharacterId &&
                                accounts.Any(x => x.CharacterId == peer.ActorId) &&
                                seen.Add(peer.ActorId),
                                $"load client {index} saw a foreign or repeated Spawn");
                            break;
                        case WireOpcode.EntityDelta:
                            Checks.Require(WireProtocol.TryDecodeEntityState(
                                frame, out _, out var delta) &&
                                delta.ActorId != choice.CharacterId &&
                                accounts.Any(x => x.CharacterId == delta.ActorId),
                                $"load client {index} foreign AOI Delta");
                            break;
                        case WireOpcode.MoveReceipt:
                            Checks.Require(!receiptSeen &&
                                WireProtocol.TryDecodeMoveReceipt(
                                    frame, out var move) &&
                                move.MapEpoch == choice.MapEpoch &&
                                move.MovementSequence == 1 &&
                                move.Status == MoveReceiptStatus.Accepted,
                                $"load client {index} move did not ACK");
                            receiptSeen = true;
                            break;
                        default:
                            throw new Exception(
                                $"load client {index} unexpected wire opcode {header.Opcode}");
                    }
                }
                readyDurations[index] =
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Interlocked.Add(ref receivedSpawns, seen.Count);
                Interlocked.Increment(ref receipts);
                if (Interlocked.Increment(ref readCount) == players)
                    allRead.TrySetResult();
                // Do not let early clients disconnect before slower peers
                // have observed their complete 19-player presence baseline.
                await allRead.Task.WaitAsync(deadline.Token);
            }

            await Task.WhenAll(Enumerable.Range(0, players)
                .Select(RunPlayer)).WaitAsync(deadline.Token);
            Checks.Require(fixture.Host.Statistics.ActiveActors == players &&
                fixture.Router.Statistics.ActiveSessions == players &&
                listener.Statistics.ActiveConnections == players,
                "not all 20 separately authenticated sessions were simultaneously alive");
            // The above scopes dispose when the workers complete. World cleanup
            // may run asynchronously after TCP EOF; the listener must settle it.
            await Until(() => fixture.Host.Statistics.ActiveActors == 0 &&
                fixture.Router.Statistics.ActiveSessions == 0 &&
                listener.Statistics.ActiveConnections == 0);
            var stats = listener.Statistics;
            Checks.Require(stats.AcceptedConnections == players &&
                stats.CompletedConnections == players &&
                stats.CommandsAccepted == players &&
                stats.RejectedAddress == 0 && stats.RejectedCapacity == 0 &&
                receivedSpawns == players * (players - 1) && receipts == players,
                "20 authenticated client listener/AOI/Move cleanup or accounting mismatch");
            Array.Sort(readyDurations);
            static double Percentile(double[] times, double fraction) =>
                times[Math.Clamp((int)Math.Ceiling(times.Length * fraction) - 1,
                    0, times.Length - 1)];
            Console.WriteLine(
                $"SERVER_AUTHENTICATED_20_TLS_PASS distinct_accounts={players} " +
                $"concurrent_sessions={players} peer_spawns={receivedSpawns} " +
                $"move_receipts={receipts} server_framed_bytes={transferredBytes} " +
                $"login_to_full_AOI_p50_ms={Percentile(readyDurations, .50):F2} " +
                $"login_to_full_AOI_p95_ms={Percentile(readyDurations, .95):F2} " +
                $"login_to_full_AOI_p99_ms={Percentile(readyDurations, .99):F2} " +
                $"admission_rejections={stats.RejectedAddress + stats.RejectedCapacity} " +
                "synthetic_map=true godot_clients=0 tls=true");
        }
        finally
        {
            stop.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("Listener condition timed out");
            await Task.Delay(5);
        }
    }

    private static async Task<byte[]> Read(SslStream tls)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] header = new byte[8];
        await tls.ReadExactlyAsync(header, timeout.Token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Checks.Require(length is >= 8 and <= 4096, "TLS frame bound");
        byte[] frame = new byte[length];
        header.CopyTo(frame, 0);
        await tls.ReadExactlyAsync(frame.AsMemory(8), timeout.Token);
        return frame;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ServerWorldHost Host;
        public required SqliteIdentityStore Store;
        public required X509Certificate2 Certificate;
        public required WorldResultRouter Router;
        public required IdentityTlsAuthorization Authorization;
        public long CharacterId;
        public required long[] InventoryUids;
        public required Task Routing;
        public required Task Visibility;
        public CancellationTokenSource? CombatStop;
        public Task? CombatWorker;

        public static async Task<Fixture> Create(string root,bool withRecovery=false)
        {
            string path = Checks.Fixture(root, 115, 4, 4, (_, _) => 0);
            var host = new ServerWorldHost([BlockingGrid.Load(path, 115)]);
            await host.StartAsync();
            var store = new SqliteIdentityStore(Path.Combine(root, "listener-"+Guid.NewGuid().ToString("N")+".db"));
            await store.InitializeAsync();
            long account = (await store.CreateAccountAsync("listener_user", "Listener-Password-123!")).AccountId;
            var character = await store.CreateCharacterAsync(account, 0, "ListenerHero", new(115, 8, 8));
            long characterId=character.Character!.CharacterId;
            long generation=await store.AllocateLootWorldGenerationAsync();
            if(withRecovery)
            {
                await store.SetCharacterCombatProfileAsync(characterId,
                    new CharacterCombatProfileData(
                        CombatProfileTrust.SyntheticTest,"ci:tls-verified-recovery",
                        Race:0,Career:0,Strength:5,Dexterity:5,Constitution:5,Wisdom:5,
                        Level:1,CurrentHp:60,MaxHp:100,Attack:20,Defense:5,
                        Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15));
            }
            var committed=await store.CreateTrustedGroundDropsAsync([
                new TrustedDropSpawn(115,8,8,1001150001,
                    SqliteIdentityStore.ComposeLootDeathRevision(generation,7),
                    0,withRecovery?1001:2701,2,LootSourceTrust.SyntheticTest,"ci:inventory-login"),
                new TrustedDropSpawn(115,8,8,1001150001,
                    SqliteIdentityStore.ComposeLootDeathRevision(generation,7),
                    1,3701,3,LootSourceTrust.SyntheticTest,"ci:inventory-login")
            ]);
            for(int i=0;i<committed.Count;i++)
            {
                var claim=await store.ClaimGroundDropAsync(
                    new TrustedPickupContext(characterId,19,1,(uint)(i+1),115,8,8),
                    committed[i].DropId);
                Checks.Require(claim.Status==PickupStatus.Granted,
                    "seed trusted durable inventory before reconnect");
            }
            var saved=await store.ListInventoryItemsAsync(characterId);
            var router=new WorldResultRouter();
            var aoi=new WorldAoiPump(router);
            var combat=withRecovery ? new PlayerCombatSettlement(
                store,delivery:router.QueuePlayerDamage) : null;
            var combatStop=withRecovery ? new CancellationTokenSource() : null;
            Task? combatWorker=combat is null ? null : combat.RunAsync(combatStop!.Token);
            IReadOnlyDictionary<int,RecoveryItemRule>? rules=withRecovery
                ? new Dictionary<int,RecoveryItemRule>{{1001,new(1001,30,1)}} : null;
            return new() {
                Host=host,Store=store,Certificate=TlsSocketChecks.CreateCertificate(),
                Router=router,
                Authorization=new(store,host,combat:combat,
                    pickupWorldGeneration:generation,recoveryRules:rules),
                CharacterId=characterId,
                InventoryUids=saved.Select(x=>x.ItemUid).ToArray(),
                Routing=router.RunAsync(host.Results),Visibility=aoi.RunAsync(),
                CombatStop=combatStop,CombatWorker=combatWorker
            };
        }

        public TlsGatewayListener Listener(TlsGatewayListenerOptions options) =>
            new(IPAddress.Loopback, 0, Authorization, Certificate, Router, options);

        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync();
            await Task.WhenAll(Routing, Visibility).WaitAsync(TimeSpan.FromSeconds(5));
            CombatStop?.Cancel();
            if(CombatWorker is not null)
                await CombatWorker.WaitAsync(TimeSpan.FromSeconds(5));
            CombatStop?.Dispose();
            await Host.DisposeAsync();
            Certificate.Dispose();
            Store.Dispose();
        }
    }
}
