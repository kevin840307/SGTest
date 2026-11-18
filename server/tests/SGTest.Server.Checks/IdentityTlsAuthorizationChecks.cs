using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class IdentityTlsAuthorizationChecks
{
    public static void Verify(string root)
    {
        Checks.Run("TLS Auth CharacterChoose binds owned persistent character into World",
            () => OwnedCharacterEntersWorld(root).GetAwaiter().GetResult());
        Checks.Run("TLS empty account creates character, refreshes list and enters World without reconnect",
            () => CreateCharacterAndEnterWorld(root).GetAwaiter().GetResult());
        Checks.Run("TLS character creation is unavailable without server-authorized spawn policy",
            () => CharacterCreationRequiresPolicy(root).GetAwaiter().GetResult());
        Checks.Run("TLS NPC shop sell buy replay and relogin preserve wallet and Item UID",
            () => ShopSellBuyAndRelogin(root).GetAwaiter().GetResult());
        Checks.Run("TLS invalid password never reaches CharacterList or World",
            () => InvalidPasswordRejected(root).GetAwaiter().GetResult());
        Checks.Run("TLS authenticated account cannot choose another account character",
            () => ForeignCharacterRejected(root).GetAwaiter().GetResult());
    }

    private static async Task OwnedCharacterEntersWorld(string root)
    {
        string identityRoot = TempRoot();
        try
        {
            string mapDir = Checks.Fixture(root, 71, 5, 5, (_, _) => 0);
            var grid = BlockingGrid.Load(mapDir, 71);
            await using var world = new ServerWorldHost(new[] { grid });
            await world.StartAsync();
            using var store = new SqliteIdentityStore(Path.Combine(identityRoot, "identity.db"));
            await store.InitializeAsync();
            long accountId = (await store.CreateAccountAsync("tls_user", "Tls-Password-123!")).AccountId;
            var created = await store.CreateCharacterAsync(accountId, 0, "Hero", new CharacterSpawn(71, 8, 8));
            long characterId = created.Character?.CharacterId ?? throw new Exception("fixture character missing");
            var authorization = new IdentityTlsAuthorization(store, world);

            using var certificate = TlsSocketChecks.CreateCertificate();
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accepted = listener.AcceptTcpClientAsync();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, port);
            var serverClient = await accepted;
            var serverTask = TlsWireConnection.RunServerAsync(serverClient, certificate,
                authorization.AuthorizeAndBindAsync, world.TryPostMove);

            var tls = TlsSocketChecks.CreateClientTls(client);
            await TlsSocketChecks.AuthenticateClient(tls);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("TLS_USER", "Tls-Password-123!")));
            await tls.FlushAsync();
            var auth = await ReadFrameAsync(tls);
            Checks.Require(WireProtocol.TryDecodeAuthResult(auth, out var authResult) &&
                authResult.Status == AuthWireStatus.Success, "TLS auth success response");
            var listFrame = await ReadFrameAsync(tls);
            Checks.Require(WireProtocol.TryDecodeCharacterList(listFrame, out var list) &&
                list.Characters.Count == 1 && list.Characters[0].CharacterId == characterId &&
                list.Characters[0].Name == "Hero", "owned CharacterList mismatch");

            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(characterId)));
            await tls.FlushAsync();
            var chooseFrame = await ReadFrameAsync(tls);
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(chooseFrame, out var choose) &&
                choose.Status == CharacterChooseWireStatus.Success && choose.CharacterId == characterId &&
                choose.MapId == 71 && choose.X == 8 && choose.Y == 8 && choose.MapEpoch > 0,
                "CharacterChoose did not bind authoritative spawn");

            await tls.WriteAsync(WireProtocol.EncodeMoveTarget(new(choose.MapEpoch, 1, 24, 8)));
            await tls.FlushAsync();
            using var resultCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            WorldTickResult result;
            do
            {
                result = await world.Results.ReadAsync(resultCts.Token);
            }
            while (!result.Receipts.Any(r => r.ActorId == characterId && r.MovementSequence == 1));
            var receipt = result.Receipts.Single(r => r.ActorId == characterId && r.MovementSequence == 1);
            Checks.Require(receipt.Status == MoveReceiptStatus.Accepted && receipt.MapEpoch == choose.MapEpoch,
                "authenticated gameplay move was not bound to chosen character");

            await tls.DisposeAsync();
            client.Close();
            var ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            Checks.Require(ended.CommandsAccepted == 1,
                "TLS authenticated gameplay command count mismatch");
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }


    private static async Task CreateCharacterAndEnterWorld(string root)
    {
        string identityRoot = TempRoot();
        try
        {
            string mapDir = Checks.Fixture(root, 74, 5, 5, (_, _) => 0);
            var grid = BlockingGrid.Load(mapDir, 74);
            await using var world = new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store = new SqliteIdentityStore(
                Path.Combine(identityRoot, "identity.db"));
            await store.InitializeAsync();
            long accountId = (await store.CreateAccountAsync(
                "new_player", "Create-Password-123!")).AccountId;
            Checks.Require((await store.ListCharactersAsync(accountId)).Count == 0,
                "new account unexpectedly has characters");

            var authorization = new IdentityTlsAuthorization(
                store, world,
                characterCreation: new CharacterCreationPolicy(
                    new CharacterSpawn(74, 8, 8)));
            var (tls, serverTask, client) = await StartSession(authorization, world);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("new_player", "Create-Password-123!")));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(tls), out var auth) &&
                auth.Status == AuthWireStatus.Success,
                "new-player auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(tls), out var empty) &&
                empty.Characters.Count == 0,
                "empty account did not receive empty selectable list");

            await tls.WriteAsync(WireProtocol.EncodeCharacterCreate(
                new(0, "CreatedHero")));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(
                    await ReadFrameAsync(tls), out var created) &&
                created.Status == CharacterCreateWireStatus.Success &&
                created.CharacterId > 0 && created.Slot == 0 &&
                created.Name == "CreatedHero",
                "character create result mismatch");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(tls), out var refreshed) &&
                refreshed.Characters.Count == 1 &&
                refreshed.Characters[0].CharacterId == created.CharacterId &&
                refreshed.Characters[0].Slot == 0 &&
                refreshed.Characters[0].Name == "CreatedHero",
                "character list was not refreshed after creation");

            // Same connection may recover from a name conflict without
            // reconnecting; Server then refreshes the authoritative list again.
            await tls.WriteAsync(WireProtocol.EncodeCharacterCreate(
                new(1, "CreatedHero")));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(
                    await ReadFrameAsync(tls), out var conflict) &&
                conflict.Status == CharacterCreateWireStatus.NameTaken &&
                conflict.CharacterId == 0,
                "duplicate character name did not fail closed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(tls), out var afterConflict) &&
                afterConflict.Characters.Count == 1,
                "failed create changed authoritative character list");

            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(
                new(created.CharacterId)));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await ReadFrameAsync(tls), out var choose) &&
                choose.Status == CharacterChooseWireStatus.Success &&
                choose.CharacterId == created.CharacterId &&
                choose.MapId == 74 && choose.X == 8 && choose.Y == 8,
                "freshly created character did not enter configured spawn");

            await tls.DisposeAsync();
            client.Dispose();
            var ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            Checks.Require(ended.Reason == TlsWireEndReason.CleanEof &&
                (await store.ListCharactersAsync(accountId)).Count == 1,
                "created character lifecycle did not persist cleanly");
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }


    private static async Task CharacterCreationRequiresPolicy(string root)
    {
        string identityRoot = TempRoot();
        try
        {
            string mapDir = Checks.Fixture(root, 75, 3, 3, (_, _) => 0);
            var grid = BlockingGrid.Load(mapDir, 75);
            await using var world = new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store = new SqliteIdentityStore(
                Path.Combine(identityRoot, "identity.db"));
            await store.InitializeAsync();
            long accountId = (await store.CreateAccountAsync(
                "no_spawn", "No-Spawn-Password-123!")).AccountId;
            var authorization = new IdentityTlsAuthorization(store, world);
            var (tls, serverTask, client) = await StartSession(authorization, world);

            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("no_spawn", "No-Spawn-Password-123!")));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(tls), out var auth) &&
                auth.Status == AuthWireStatus.Success,
                "no-policy account auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(tls), out var empty) &&
                empty.Characters.Count == 0,
                "no-policy empty account list mismatch");

            await tls.WriteAsync(WireProtocol.EncodeCharacterCreate(
                new(0, "CannotSpawn")));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(
                    await ReadFrameAsync(tls), out var denied) &&
                denied.Status == CharacterCreateWireStatus.Unavailable &&
                denied.CharacterId == 0,
                "creation without server spawn policy was not refused");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(tls), out var refreshed) &&
                refreshed.Characters.Count == 0 &&
                (await store.ListCharactersAsync(accountId)).Count == 0,
                "refused character creation mutated persistent account");

            await tls.DisposeAsync();
            client.Dispose();
            var ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            Checks.Require(ended.Reason == TlsWireEndReason.AuthorizationRejected &&
                world.Statistics.ActiveActors == 0,
                "no-policy creation entered World");
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }



    private static async Task ShopSellBuyAndRelogin(string root)
    {
        string identityRoot = TempRoot();
        try
        {
            string mapDir = Checks.Fixture(root, 76, 5, 5, (_, _) => 0);
            var grid = BlockingGrid.Load(mapDir, 76);
            await using var world = new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store = new SqliteIdentityStore(
                Path.Combine(identityRoot, "identity.db"));
            await store.InitializeAsync();
            long accountId = (await store.CreateAccountAsync(
                "shop_user", "Shop-Password-123!")).AccountId;
            long characterId = (await store.CreateCharacterAsync(
                accountId, 0, "ShopHero", new CharacterSpawn(76, 8, 8)))
                .Character?.CharacterId ?? throw new Exception("shop fixture character missing");

            long generation = await store.AllocateLootWorldGenerationAsync();
            var drop = await store.CreateTrustedGroundDropAsync(new(
                76, 8, 8, 9001,
                SqliteIdentityStore.ComposeLootDeathRevision(generation, 1),
                0, 1001, 2, LootSourceTrust.SyntheticTest, "shop-e2e-seed"));
            var grant = await store.ClaimGroundDropAsync(new TrustedPickupContext(
                characterId, 1, 1, 1, 76, 8, 8), drop.DropId);
            Checks.Require(grant.Status == PickupStatus.Granted &&
                grant.ItemUid > 0 && grant.Quantity == 2, "shop seed inventory grant failed");
            long seededUid = grant.ItemUid;

            var shop = new NpcShopCatalog.Shop(
                76, 99, 8, 8, "synthetic-shop-e2e",
                new Dictionary<int,NpcShopCatalog.Item>
                {
                    [1001] = new(1001, 10, 5)
                });
            var catalog = new NpcShopCatalog([shop]);
            var authorization = new IdentityTlsAuthorization(
                store, world, pickupWorldGeneration: generation, shops: catalog);
            var router = new WorldResultRouter();

            var first = await StartRoutedSession(authorization, world, router);
            await first.Tls.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("shop_user", "Shop-Password-123!")));
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(first.Tls), out var auth) &&
                auth.Status == AuthWireStatus.Success, "shop auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(first.Tls), out var list) &&
                list.Characters.Single().CharacterId == characterId,
                "shop character list mismatch");
            await first.Tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(characterId)));
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await ReadFrameAsync(first.Tls), out var choose) &&
                choose.Status == CharacterChooseWireStatus.Success,
                "shop choose failed");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                    await ReadFrameAsync(first.Tls), out var inventory) &&
                inventory is not null && inventory.Complete &&
                inventory.Items.Count == 1 &&
                inventory.Items[0].ItemUid == seededUid &&
                inventory.Items[0].Quantity == 2,
                "shop login inventory baseline mismatch");

            await first.Tls.WriteAsync(WireProtocol.EncodeShopOpen(
                new(choose.MapEpoch, 1, 99)));
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopSnapshot(
                    await ReadFrameAsync(first.Tls), out var opened) &&
                opened is not null && opened.Status == ShopOpenStatus.Opened &&
                opened.Money == 0 && opened.Items.Count == 1 &&
                opened.Items[0] == new ShopItemWire(1001, 10, 5),
                "shop open snapshot mismatch");

            await first.Tls.WriteAsync(WireProtocol.EncodeShopSell(
                new(choose.MapEpoch, 2, 99, seededUid, 1001, 1)));
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Tls), out var soldOne) &&
                soldOne.Status == ShopTransactionStatus.Applied &&
                soldOne.Kind == ShopTransactionKind.Sell &&
                soldOne.Money == 5 && soldOne.RemainingQuantity == 1,
                "shop partial sell mismatch");

            await first.Tls.WriteAsync(WireProtocol.EncodeShopBuy(
                new(choose.MapEpoch, 3, 99, 1001, 1)));
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Tls), out var insufficient) &&
                insufficient.Status == ShopTransactionStatus.InsufficientMoney &&
                insufficient.Money == 5 && insufficient.ItemUid == 0,
                "shop insufficient money did not fail closed");

            await first.Tls.WriteAsync(WireProtocol.EncodeShopSell(
                new(choose.MapEpoch, 4, 99, seededUid, 1001, 1)));
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Tls), out var soldLast) &&
                soldLast.Status == ShopTransactionStatus.Applied &&
                soldLast.Money == 10 && soldLast.RemainingQuantity == 0,
                "shop final sell mismatch");

            var buyFrame = WireProtocol.EncodeShopBuy(
                new(choose.MapEpoch, 5, 99, 1001, 1));
            await first.Tls.WriteAsync(buyFrame);
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Tls), out var bought) &&
                bought.Status == ShopTransactionStatus.Applied &&
                bought.Kind == ShopTransactionKind.Buy &&
                bought.Money == 0 && bought.ItemUid > 0 &&
                bought.ItemUid != seededUid && bought.RemainingQuantity == 1,
                "shop purchase mismatch");
            long boughtUid = bought.ItemUid;

            await first.Tls.WriteAsync(buyFrame); // exact transport retry
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Tls), out var replay) &&
                replay.Status == ShopTransactionStatus.AlreadyApplied &&
                replay.ItemUid == boughtUid && replay.Money == 0,
                "shop retry was not idempotent");

            await first.Tls.DisposeAsync();
            first.Client.Dispose();
            var firstEnded = await first.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(firstEnded.Reason == TlsWireEndReason.CleanEof,
                "shop first TLS session did not close cleanly");

            var persisted = await store.ListInventoryItemsAsync(characterId);
            Checks.Require(persisted.Count == 1 &&
                persisted[0].ItemUid == boughtUid &&
                persisted[0].ItemDefinitionId == 1001 &&
                persisted[0].Quantity == 1 &&
                persisted[0].SourceDropId == 0 &&
                await store.GetCharacterMoneyAsync(characterId) == 0,
                "shop durable inventory/wallet mismatch");

            var second = await StartRoutedSession(authorization, world, router);
            await second.Tls.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("shop_user", "Shop-Password-123!")));
            await second.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(second.Tls), out var auth2) &&
                auth2.Status == AuthWireStatus.Success, "shop relogin auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(second.Tls), out var list2) &&
                list2.Characters.Single().CharacterId == characterId,
                "shop relogin list mismatch");
            await second.Tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(characterId)));
            await second.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await ReadFrameAsync(second.Tls), out var choose2) &&
                choose2.Status == CharacterChooseWireStatus.Success,
                "shop relogin choose failed");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                    await ReadFrameAsync(second.Tls), out var inventory2) &&
                inventory2 is not null && inventory2.Complete &&
                inventory2.Items.Count == 1 &&
                inventory2.Items[0].ItemUid == boughtUid &&
                inventory2.Items[0].SourceDropId == 0,
                "shop-origin Item UID did not survive relogin");

            await second.Tls.DisposeAsync();
            second.Client.Dispose();
            var secondEnded = await second.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(secondEnded.Reason == TlsWireEndReason.CleanEof,
                "shop relogin session did not close cleanly");
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }


    private static async Task InvalidPasswordRejected(string root)
    {
        string identityRoot = TempRoot();
        try
        {
            string mapDir = Checks.Fixture(root, 72, 3, 3, (_, _) => 0);
            var grid = BlockingGrid.Load(mapDir, 72);
            await using var world = new ServerWorldHost(new[] { grid });
            await world.StartAsync();
            using var store = new SqliteIdentityStore(Path.Combine(identityRoot, "identity.db"));
            await store.InitializeAsync();
            long accountId = (await store.CreateAccountAsync("wrong_pw", "Correct-Password-123!")).AccountId;
            await store.CreateCharacterAsync(accountId, 0, "Hero2", new CharacterSpawn(72, 8, 8));
            var authorization = new IdentityTlsAuthorization(store, world);

            var (tls, serverTask, client) = await StartSession(authorization, world);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("wrong_pw", "Incorrect-Password!")));
            await tls.FlushAsync();
            var frame = await ReadFrameAsync(tls);
            Checks.Require(WireProtocol.TryDecodeAuthResult(frame, out var result) &&
                result.Status == AuthWireStatus.InvalidCredentials, "wrong password result");
            var ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            Checks.Require(ended.Reason == TlsWireEndReason.AuthorizationRejected &&
                world.Statistics.ActiveActors == 0, "wrong password entered World");
            await tls.DisposeAsync(); client.Dispose(); await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }

    private static async Task ForeignCharacterRejected(string root)
    {
        string identityRoot = TempRoot();
        try
        {
            string mapDir = Checks.Fixture(root, 73, 3, 3, (_, _) => 0);
            var grid = BlockingGrid.Load(mapDir, 73);
            await using var world = new ServerWorldHost(new[] { grid });
            await world.StartAsync();
            using var store = new SqliteIdentityStore(Path.Combine(identityRoot, "identity.db"));
            await store.InitializeAsync();
            long ownerA = (await store.CreateAccountAsync("owner_a", "OwnerA-Password-123!")).AccountId;
            long ownerB = (await store.CreateAccountAsync("owner_b", "OwnerB-Password-123!")).AccountId;
            long foreignId = (await store.CreateCharacterAsync(ownerA, 0, "ForeignHero", new CharacterSpawn(73, 8, 8)))
                .Character?.CharacterId ?? throw new Exception("foreign fixture character missing");
            await store.CreateCharacterAsync(ownerB, 0, "OwnHero", new CharacterSpawn(73, 8, 8));
            var authorization = new IdentityTlsAuthorization(store, world);

            var (tls, serverTask, client) = await StartSession(authorization, world);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("owner_b", "OwnerB-Password-123!")));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(await ReadFrameAsync(tls), out var auth) &&
                auth.Status == AuthWireStatus.Success, "owner B auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(await ReadFrameAsync(tls), out var list) &&
                list.Characters.All(c => c.CharacterId != foreignId), "foreign character leaked into CharacterList");
            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(foreignId)));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(await ReadFrameAsync(tls), out var choose) &&
                choose.Status == CharacterChooseWireStatus.NotOwned,
                "foreign CharacterId was not rejected");
            var ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            Checks.Require(ended.Reason == TlsWireEndReason.AuthorizationRejected &&
                world.Statistics.ActiveActors == 0, "foreign character entered World");
            await tls.DisposeAsync(); client.Dispose(); await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }



    private static async Task<(SslStream Tls, Task<TlsWireConnectionResult> ServerTask, TcpClient Client)>
        StartRoutedSession(
            IdentityTlsAuthorization authorization, ServerWorldHost world,
            WorldResultRouter router)
    {
        var certificate = TlsSocketChecks.CreateCertificate();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();
        var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await accepted;
        var serverTask = authorization.RunAuthenticatedSocketAsync(
            serverClient, certificate, router).ContinueWith(t =>
            {
                certificate.Dispose();
                listener.Stop();
                return t.GetAwaiter().GetResult();
            }, TaskScheduler.Default);
        var tls = TlsSocketChecks.CreateClientTls(client);
        await TlsSocketChecks.AuthenticateClient(tls);
        return (tls, serverTask, client);
    }


    private static async Task<(SslStream Tls, Task<TlsWireConnectionResult> ServerTask, TcpClient Client)>
        StartSession(IdentityTlsAuthorization authorization, ServerWorldHost world)
    {
        var certificate = TlsSocketChecks.CreateCertificate();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();
        var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await accepted;
        var serverTask = TlsWireConnection.RunServerAsync(serverClient, certificate,
            authorization.AuthorizeAndBindAsync, world.TryPostMove).ContinueWith(t =>
            {
                certificate.Dispose(); listener.Stop(); return t.GetAwaiter().GetResult();
            }, TaskScheduler.Default);
        var tls = TlsSocketChecks.CreateClientTls(client);
        await TlsSocketChecks.AuthenticateClient(tls);
        return (tls, serverTask, client);
    }

    private static async Task<byte[]> ReadFrameAsync(SslStream tls)
    {
        byte[] header = new byte[WireProtocol.HeaderBytes];
        await tls.ReadExactlyAsync(header);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        Checks.Require(length >= WireProtocol.HeaderBytes && length <= WireProtocol.MaximumFrameBytes,
            "test client received invalid frame length");
        byte[] frame = new byte[length];
        header.CopyTo(frame, 0);
        if (length > header.Length) await tls.ReadExactlyAsync(frame.AsMemory(header.Length));
        return frame;
    }

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "sgtest_tls_identity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return root;
    }
}
