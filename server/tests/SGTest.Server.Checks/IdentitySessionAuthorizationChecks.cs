using SGTest.Server.Npc;
using SGTest.Server.Map;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Transport;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class IdentitySessionAuthorizationChecks
{
    public static void Verify(string root)
    {
        Checks.Run("AES Auth CharacterChoose binds owned persistent character into World",
            () => OwnedCharacterEntersWorld(root).GetAwaiter().GetResult());
        Checks.Run("AES empty account creates character, refreshes list and enters World without reconnect",
            () => CreateCharacterAndEnterWorld(root).GetAwaiter().GetResult());
        Checks.Run("AES character creation is unavailable without server-authorized spawn policy",
            () => CharacterCreationRequiresPolicy(root).GetAwaiter().GetResult());
        Checks.Run("AES character appearance rejects missing/unlisted choices and persists selected look across login",
            () => CharacterAppearanceCreation(root).GetAwaiter().GetResult());
        Checks.Run("AES extended character creation persists stats and requires secondary password",
            () => ProtectedCharacterCreation(root).GetAwaiter().GetResult());
        Checks.Run("AES NPC shop sell buy replay and relogin preserve wallet and Item UID",
            () => ShopSellBuyAndRelogin(root).GetAwaiter().GetResult());
        Checks.Run("AES personal storage deposit withdraw replay and relogin preserve Item UID",
            () => StorageDepositWithdrawAndRelogin(root).GetAwaiter().GetResult());
        Checks.Run("AES public account registration is opt-in rate-limited and still requires Auth",
            () => PublicAccountRegistration(root).GetAwaiter().GetResult());
        Checks.Run("AES invalid password never reaches CharacterList or World",
            () => InvalidPasswordRejected(root).GetAwaiter().GetResult());
        Checks.Run("AES authenticated account cannot choose another account character",
            () => RejectedCharacterSelection(root).GetAwaiter().GetResult());
        Checks.Run("AES character password set and delete return authoritative refreshed list",
            () => CharacterManagementViaAes(root).GetAwaiter().GetResult());
        Checks.Run("AES legacy proof-free choose cannot enter a password-protected owned character",
            () => RejectedCharacterSelection(root,protectedOwned:true).GetAwaiter().GetResult());
    }

    private static async Task CharacterManagementViaAes(string root)
    {
        string identityRoot=TempRoot();
        try
        {
            string mapDir=Checks.Fixture(root,118,5,5,(_,_)=>0);
            var grid=BlockingGrid.Load(mapDir,118);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(Path.Combine(identityRoot,"identity.db"));
            await store.InitializeAsync();
            const string accountName="secondary_user";
            const string password="Secondary-Account-123!";
            long account=(await store.CreateAccountAsync(accountName,password)).AccountId;
            long character=(await store.CreateCharacterAsync(
                account,0,"SecretHero",new CharacterSpawn(118,8,8))).Character!.CharacterId;
            var authorization=new IdentitySessionAuthorization(store,world);
            var (tls,serverTask,client)=await StartSession(authorization,world);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new(accountName,password)));
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                await ReadFrameAsync(tls),out var auth) &&
                auth.Status==AuthWireStatus.Success,"secondary AES account login failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                await ReadFrameAsync(tls),out var list) &&
                list.Characters.Single().CharacterId==character,"secondary AES selection list failed");

            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(character)));
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                await ReadFrameAsync(tls),out var required) &&
                required.Status==CharacterChooseWireStatus.PasswordRequired,
                "legacy character entered world without second password");

            await tls.WriteAsync(WireProtocol.EncodeCharacterManage(
                new(CharacterManageAction.ChangePassword,character,null,"2468")));
            Checks.Require(WireProtocol.TryDecodeCharacterManageResult(
                await ReadFrameAsync(tls),out var initial) &&
                initial.Status==CharacterManageStatus.Success,
                "secondary password setup rejected");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                await ReadFrameAsync(tls),out list) &&
                list.Characters.Single().CharacterId==character,
                "set password did not refresh server list");

            await tls.WriteAsync(WireProtocol.EncodeCharacterManage(
                new(CharacterManageAction.ChangePassword,character,"2468","1357")));
            Checks.Require(WireProtocol.TryDecodeCharacterManageResult(
                await ReadFrameAsync(tls),out var changed) &&
                changed.Status==CharacterManageStatus.Success,
                "secondary password rotation rejected");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                await ReadFrameAsync(tls),out list) &&
                list.Characters.Count==1,
                "password rotation omitted list refresh");

            await tls.WriteAsync(WireProtocol.EncodeCharacterManage(
                new(CharacterManageAction.Delete,character,"2468",null)));
            Checks.Require(WireProtocol.TryDecodeCharacterManageResult(
                await ReadFrameAsync(tls),out var denied) &&
                denied.Status==CharacterManageStatus.InvalidPassword,
                "obsolete second password authorized deletion");
            Checks.Require((await store.ListCharactersAsync(account)).Count==1,
                "rejected deletion damaged character");

            await tls.WriteAsync(WireProtocol.EncodeCharacterManage(
                new(CharacterManageAction.Delete,character,"1357",null)));
            Checks.Require(WireProtocol.TryDecodeCharacterManageResult(
                await ReadFrameAsync(tls),out var removed) &&
                removed.Status==CharacterManageStatus.Success,
                "authenticated AES character delete rejected");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                await ReadFrameAsync(tls),out list) &&
                list.Characters.Count==0 &&
                (await store.ListCharactersAsync(account)).Count==0,
                "deleted character remained visible after authoritative refresh");

            await tls.DisposeAsync();
            client.Dispose();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot,true); }
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
            var authorization = new IdentitySessionAuthorization(store, world);

            var settings = AesSocketChecks.CreateSettings();
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accepted = listener.AcceptTcpClientAsync();
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, port);
            var serverClient = await accepted;
            var serverTask = AesWireConnection.RunServerAsync(serverClient, settings,
                (tls,token) => authorization.AuthorizeAndBindAsync(tls,token), world.TryPostMove);

            var tls = AesSocketChecks.CreateClientStream(client);

            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("tls_user", "Tls-Password-123!")));
            await tls.FlushAsync();
            var auth = await ReadFrameAsync(tls);
            Checks.Require(WireProtocol.TryDecodeAuthResult(auth, out var authResult) &&
                authResult.Status == AuthWireStatus.Success, "AES auth success response");
            var listFrame = await ReadFrameAsync(tls);
            Checks.Require(WireProtocol.TryDecodeCharacterList(listFrame, out var list) &&
                list.Characters.Count == 1 && list.Characters[0].CharacterId == characterId &&
                list.Characters[0].Name == "Hero", "owned CharacterList mismatch");

            await CharacterChooseTestHarness.ChooseAsync(tls,characterId);
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
                "AES authenticated gameplay command count mismatch");
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

            var authorization = new IdentitySessionAuthorization(
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

            await tls.WriteAsync(WireProtocol.EncodeCharacterCreate(new(0,"NoRuleLook",new CharacterAppearance(0,0,0))));
            Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(await ReadFrameAsync(tls),out var noRules) &&
                noRules.Status==CharacterCreateWireStatus.InvalidAppearance,"client enabled appearance without trusted rules");
            Checks.Require(WireProtocol.TryDecodeCharacterList(await ReadFrameAsync(tls),out var stillEmpty) &&
                stillEmpty.Characters.Count==0,"unconfigured appearance mutated identity data");

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

            await CharacterChooseTestHarness.ChooseAsync(tls,created.CharacterId);
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
            int persistedCharacters=(await store.ListCharactersAsync(accountId)).Count;
            Checks.Require(ended.Reason is AesWireEndReason.CleanEof or AesWireEndReason.TransportError &&
                persistedCharacters == 1,
                $"created character lifecycle did not persist cleanly: " +
                $"end={ended.Reason}, characters={persistedCharacters}");
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }


    private static async Task CharacterAppearanceCreation(string root)
    {
        string identityRoot=TempRoot();
        try
        {
            var grid=BlockingGrid.Load(Checks.Fixture(root,79,3,3,(_,_)=>0),79);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(Path.Combine(identityRoot,"identity.db"));
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync("appearance_player","Appearance-Password-123!")).AccountId;
            var look=new CharacterAppearance(1,3,7);
            var rules=new CharacterAppearanceRules("synthetic/tls-appearance/v1",[new(0,0,0),look]);
            var auth=new IdentitySessionAuthorization(store,world,
                characterCreation:new(new CharacterSpawn(79,8,8),rules));
            for(int pass=0;pass<2;pass++)
            {
                var (tls,serverTask,client)=await StartSession(auth,world);
                await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("appearance_player","Appearance-Password-123!")));
                Checks.Require(WireProtocol.TryDecodeAuthResult(await ReadFrameAsync(tls),out var login) &&
                    login.Status==AuthWireStatus.Success,"appearance AES auth failed");
                Checks.Require(WireProtocol.TryDecodeCharacterList(await ReadFrameAsync(tls),out var list) &&
                    list.CreationAppearances!.SequenceEqual(rules.Choices) && list.Characters.Count==pass,
                    "appearance choices/list did not survive login");
                if(pass==0)
                {
                    foreach(var request in new CharacterCreateWire[]{new(0,"AppearanceHero"),new(0,"AppearanceHero",new(2,3,7))})
                    {
                        await tls.WriteAsync(WireProtocol.EncodeCharacterCreate(request));
                        Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(await ReadFrameAsync(tls),out var denied) &&
                            denied.Status==CharacterCreateWireStatus.InvalidAppearance && denied.Appearance is null,
                            "unlisted or missing appearance was not denied");
                        Checks.Require(WireProtocol.TryDecodeCharacterList(await ReadFrameAsync(tls),out var unchanged) &&
                            unchanged.Characters.Count==0,"rejected appearance created a character");
                    }
                    await tls.WriteAsync(WireProtocol.EncodeCharacterCreate(new(0,"AppearanceHero",look)));
                    Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(await ReadFrameAsync(tls),out var created) &&
                        created.Status==CharacterCreateWireStatus.Success && created.Appearance==look,"allowed appearance not acknowledged");
                    Checks.Require(WireProtocol.TryDecodeCharacterList(await ReadFrameAsync(tls),out var refreshed) &&
                        refreshed.Characters.Single().Appearance==look,"authoritative list lost appearance");
                    // Replay is an ordinary slot conflict, never a second grant.
                    await tls.WriteAsync(WireProtocol.EncodeCharacterCreate(new(0,"AppearanceHero",look)));
                    Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(await ReadFrameAsync(tls),out var duplicate) &&
                        duplicate.Status==CharacterCreateWireStatus.SlotOccupied,"replayed creation granted again");
                    await ReadFrameAsync(tls);
                }
                else Checks.Require(list.Characters.Single().Appearance==look,"relogin appearance changed");
                await tls.DisposeAsync();
                client.Dispose();
                await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            var persisted=(await store.ListCharactersAsync(account)).Single();
            Checks.Require(persisted.Appearance==new CharacterAppearanceState(look,rules.SourceReference) &&
                world.Statistics.ActiveActors==0,"appearance source changed or unauthorized actor spawned");
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot,true); }
    }


    private static async Task ProtectedCharacterCreation(string root)
    {
        string identityRoot=TempRoot();
        try
        {
            var grid=BlockingGrid.Load(Checks.Fixture(root,80,3,3,(_,_)=>0),80);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(Path.Combine(identityRoot,"identity.db"));
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "protected_create","Protected-Create-Password-123!")).AccountId;
            var look=new CharacterAppearance(4,2,6);
            var rules=new CharacterAppearanceRules(
                "synthetic/aes-protected-create/v1",[look]);
            var auth=new IdentitySessionAuthorization(store,world,
                characterCreation:new(new CharacterSpawn(80,8,8),rules));
            var (tls,serverTask,client)=await StartSession(auth,world);

            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new(
                "protected_create","Protected-Create-Password-123!")));
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(tls),out var login) &&
                login.Status==AuthWireStatus.Success,
                "protected-create auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(tls),out var empty) &&
                empty.Characters.Count==0 &&
                empty.CreationAppearances!.SequenceEqual(rules.Choices),
                "protected-create menu mismatch");

            await tls.WriteAsync(WireProtocol.EncodeCharacterCreate(new(
                0,"ProtectHero",look,
                new CharacterCreationWire(3,2,2,1,"2468"))));
            Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(
                    await ReadFrameAsync(tls),out var created) &&
                created.Status==CharacterCreateWireStatus.Success &&
                created.CharacterId>0 && created.Appearance==look,
                "extended protected character create failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(tls),out var refreshed) &&
                refreshed.Characters.Single().CharacterId==created.CharacterId,
                "protected character list refresh failed");

            var persisted=(await store.ListCharactersAsync(account)).Single();
            Checks.Require(persisted.RequiresCharacterPassword &&
                persisted.Creation!.Attributes==
                    new CharacterCreationAttributes(3,2,2,1) &&
                persisted.Creation.SourceReference==CharacterCreationRules.SourceReference,
                "extended creation settings were not persisted");

            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(
                new(created.CharacterId)));
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await ReadFrameAsync(tls),out var passwordRequired) &&
                passwordRequired.Status==CharacterChooseWireStatus.PasswordRequired &&
                world.Statistics.ActiveActors==0,
                "proof-free protected choose was not held at password gate");

            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(
                new(created.CharacterId,"2468")));
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await ReadFrameAsync(tls),out var choose) &&
                choose.Status==CharacterChooseWireStatus.Success &&
                choose.CharacterId==created.CharacterId &&
                choose.MapId==80 && choose.X==8 && choose.Y==8,
                "correct secondary password did not enter configured spawn");

            bool sawCreationProfile=false;
            var seenBaselineOpcodes=new List<ushort>();
            using var baselineTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                for(int i=0;i<16 && !sawCreationProfile;i++)
                {
                    var frame=await ReadFrameAsync(tls,baselineTimeout.Token);
                    ushort rawOpcode=BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6,2));
                    seenBaselineOpcodes.Add(rawOpcode);
                    if(!WireProtocol.TryDecodePlayerCreationProfileState(
                        frame,out var creationProfile))
                    {
                        if(rawOpcode==(ushort)WireOpcode.PlayerCreationProfileState)
                        {
                            bool headerOk=WireProtocol.TryReadHeader(frame,out var decodedHeader);
                            throw new InvalidOperationException(
                                "creation profile decoder rejected wire frame: len="+frame.Length+
                                " advertised="+BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0,4))+
                                " headerOk="+headerOk+
                                " headerOpcode="+(headerOk ? ((ushort)decodedHeader.Opcode).ToString() : "n/a")+
                                " hex="+Convert.ToHexString(frame));
                        }
                        continue;
                    }
                    Checks.Require(creationProfile.CharacterId==created.CharacterId &&
                        creationProfile.Strength==3 &&
                        creationProfile.Dexterity==2 &&
                        creationProfile.Constitution==2 &&
                        creationProfile.Wisdom==1,
                        "fresh character creation profile baseline did not match persisted authority");
                    sawCreationProfile=true;
                }
            }
            catch(OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "fresh character creation profile baseline timed out; opcodes="+
                    string.Join(",",seenBaselineOpcodes));
            }
            Checks.Require(sawCreationProfile,
                "fresh character did not receive creation profile baseline during authorization");

            await tls.DisposeAsync();
            client.Dispose();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot,true); }
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
            var authorization = new IdentitySessionAuthorization(store, world);
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
            Checks.Require(ended.Reason == AesWireEndReason.AuthorizationRejected &&
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
            var authorization = new IdentitySessionAuthorization(
                store, world, pickupWorldGeneration: generation, shops: catalog);
            var router = new WorldResultRouter();

            var first = await StartRoutedSession(authorization, world, router);
            await first.Stream.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("shop_user", "Shop-Password-123!")));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(first.Stream), out var auth) &&
                auth.Status == AuthWireStatus.Success, "shop auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(first.Stream), out var list) &&
                list.Characters.Single().CharacterId == characterId,
                "shop character list mismatch");
            await CharacterChooseTestHarness.ChooseAsync(first.Stream,characterId);
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await ReadFrameAsync(first.Stream), out var choose) &&
                choose.Status == CharacterChooseWireStatus.Success,
                "shop choose failed");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                    await ReadFrameAsync(first.Stream), out var inventory) &&
                inventory is not null && inventory.Complete &&
                inventory.Items.Count == 1 &&
                inventory.Items[0].ItemUid == seededUid &&
                inventory.Items[0].Quantity == 2,
                "shop login inventory baseline mismatch");

            await first.Stream.WriteAsync(WireProtocol.EncodeShopOpen(
                new(choose.MapEpoch, 1, 99)));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopSnapshot(
                    await ReadFrameAsync(first.Stream), out var opened) &&
                opened is not null && opened.Status == ShopOpenStatus.Opened &&
                opened.Money == 0 && opened.Items.Count == 1 &&
                opened.Items[0] == new ShopItemWire(1001, 10, 5),
                "shop open snapshot mismatch");

            await first.Stream.WriteAsync(WireProtocol.EncodeShopSell(
                new(choose.MapEpoch, 2, 99, seededUid, 1001, 1)));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Stream), out var soldOne) &&
                soldOne.Status == ShopTransactionStatus.Applied &&
                soldOne.Kind == ShopTransactionKind.Sell &&
                soldOne.Money == 5 && soldOne.RemainingQuantity == 1,
                "shop partial sell mismatch");

            await first.Stream.WriteAsync(WireProtocol.EncodeShopBuy(
                new(choose.MapEpoch, 3, 99, 1001, 1)));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Stream), out var insufficient) &&
                insufficient.Status == ShopTransactionStatus.InsufficientMoney &&
                insufficient.Money == 5 && insufficient.ItemUid == 0,
                "shop insufficient money did not fail closed");

            await first.Stream.WriteAsync(WireProtocol.EncodeShopSell(
                new(choose.MapEpoch, 4, 99, seededUid, 1001, 1)));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Stream), out var soldLast) &&
                soldLast.Status == ShopTransactionStatus.Applied &&
                soldLast.Money == 10 && soldLast.RemainingQuantity == 0,
                "shop final sell mismatch");

            var buyFrame = WireProtocol.EncodeShopBuy(
                new(choose.MapEpoch, 5, 99, 1001, 1));
            await first.Stream.WriteAsync(buyFrame);
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Stream), out var bought) &&
                bought.Status == ShopTransactionStatus.Applied &&
                bought.Kind == ShopTransactionKind.Buy &&
                bought.Money == 0 && bought.ItemUid > 0 &&
                bought.ItemUid != seededUid && bought.RemainingQuantity == 1,
                "shop purchase mismatch");
            long boughtUid = bought.ItemUid;

            await first.Stream.WriteAsync(buyFrame); // exact transport retry
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeShopTransactionResult(
                    await ReadFrameAsync(first.Stream), out var replay) &&
                replay.Status == ShopTransactionStatus.AlreadyApplied &&
                replay.ItemUid == boughtUid && replay.Money == 0,
                "shop retry was not idempotent");

            await first.Stream.DisposeAsync();
            first.Client.Dispose();
            var firstEnded = await first.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(firstEnded.Reason == AesWireEndReason.CleanEof,
                "shop first AES session did not close cleanly");

            var persisted = await store.ListInventoryItemsAsync(characterId);
            Checks.Require(persisted.Count == 1 &&
                persisted[0].ItemUid == boughtUid &&
                persisted[0].ItemDefinitionId == 1001 &&
                persisted[0].Quantity == 1 &&
                persisted[0].SourceDropId == 0 &&
                await store.GetCharacterMoneyAsync(characterId) == 0,
                "shop durable inventory/wallet mismatch");

            var second = await StartRoutedSession(authorization, world, router);
            await second.Stream.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("shop_user", "Shop-Password-123!")));
            await second.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(second.Stream), out var auth2) &&
                auth2.Status == AuthWireStatus.Success, "shop relogin auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(second.Stream), out var list2) &&
                list2.Characters.Single().CharacterId == characterId,
                "shop relogin list mismatch");
            await CharacterChooseTestHarness.ChooseAsync(second.Stream,characterId);
            await second.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await ReadFrameAsync(second.Stream), out var choose2) &&
                choose2.Status == CharacterChooseWireStatus.Success,
                "shop relogin choose failed");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                    await ReadFrameAsync(second.Stream), out var inventory2) &&
                inventory2 is not null && inventory2.Complete &&
                inventory2.Items.Count == 1 &&
                inventory2.Items[0].ItemUid == boughtUid &&
                inventory2.Items[0].SourceDropId == 0,
                "shop-origin Item UID did not survive relogin");

            await second.Stream.DisposeAsync();
            second.Client.Dispose();
            var secondEnded = await second.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(secondEnded.Reason == AesWireEndReason.CleanEof,
                "shop relogin session did not close cleanly");
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }


    private static async Task StorageDepositWithdrawAndRelogin(string root)
    {
        string identityRoot = TempRoot();
        try
        {
            string mapDir = Checks.Fixture(root, 77, 5, 5, (_, _) => 0);
            var grid = BlockingGrid.Load(mapDir, 77);
            await using var world = new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store = new SqliteIdentityStore(
                Path.Combine(identityRoot, "identity.db"));
            await store.InitializeAsync();
            long accountId = (await store.CreateAccountAsync(
                "storage_tls_user", "Storage-Tls-Password-123!")).AccountId;
            long characterId = (await store.CreateCharacterAsync(
                accountId, 0, "StorageTlsHero", new CharacterSpawn(77, 8, 8)))
                .Character?.CharacterId ?? throw new Exception("storage AES character missing");

            long generation = await store.AllocateLootWorldGenerationAsync();
            var drop = await store.CreateTrustedGroundDropAsync(new(
                77, 8, 8, 9101,
                SqliteIdentityStore.ComposeLootDeathRevision(generation, 1),
                0, 2701, 3, LootSourceTrust.SyntheticTest, "storage-tls-seed"));
            var grant = await store.ClaimGroundDropAsync(new TrustedPickupContext(
                characterId, 1, 1, 1, 77, 8, 8), drop.DropId);
            Checks.Require(grant.Status == PickupStatus.Granted &&
                grant.ItemUid > 0 && grant.Quantity == 3,
                "storage AES seed inventory grant failed");
            long itemUid = grant.ItemUid;

            var storageCatalog = new NpcStorageCatalog([
                new NpcStorageCatalog.StoragePoint(
                    77, 88, 8, 8, "synthetic-storage-tls")
            ]);
            var authorization = new IdentitySessionAuthorization(
                store, world, pickupWorldGeneration: generation,
                storages: storageCatalog);
            var router = new WorldResultRouter();

            var first = await StartRoutedSession(authorization, world, router);
            await first.Stream.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("storage_tls_user", "Storage-Tls-Password-123!")));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(first.Stream), out var auth) &&
                auth.Status == AuthWireStatus.Success,
                "storage AES auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(first.Stream), out var list) &&
                list.Characters.Single().CharacterId == characterId,
                "storage AES character list mismatch");
            await CharacterChooseTestHarness.ChooseAsync(first.Stream,characterId);
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await ReadFrameAsync(first.Stream), out var choose) &&
                choose.Status == CharacterChooseWireStatus.Success,
                "storage AES choose failed");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                    await ReadFrameAsync(first.Stream), out var inventory) &&
                inventory is not null && inventory.Complete &&
                inventory.Items.Count == 1 &&
                inventory.Items[0].ItemUid == itemUid &&
                inventory.Items[0].SourceDropId == drop.DropId,
                "storage AES login inventory baseline mismatch");

            await first.Stream.WriteAsync(WireProtocol.EncodeStorageOpen(
                new(choose.MapEpoch, 1, 88)));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeStorageSnapshot(
                    await ReadFrameAsync(first.Stream), out var opened) &&
                opened is not null && opened.Status == StorageOpenStatus.Opened &&
                opened.NpcSpawnId == 88 && opened.Complete &&
                opened.Items.Count == 0,
                "storage AES initial empty snapshot mismatch");

            var depositFrame = WireProtocol.EncodeStorageDeposit(
                new(choose.MapEpoch, 2, 88, itemUid));
            await first.Stream.WriteAsync(depositFrame);
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeStorageTransferResult(
                    await ReadFrameAsync(first.Stream), out var deposited) &&
                deposited.Status == StorageTransferStatus.Applied &&
                deposited.Kind == StorageTransferKind.Deposit &&
                deposited.ItemUid == itemUid &&
                deposited.ItemDefinitionId == 2701 &&
                deposited.Quantity == 3 &&
                deposited.SourceDropId == drop.DropId,
                "storage AES deposit receipt mismatch");
            Checks.Require((await store.ListInventoryItemsAsync(characterId)).Count == 0 &&
                (await store.ListStorageItemsAsync(characterId)).Single().ItemUid == itemUid,
                "storage AES deposit did not move durable UID");

            await first.Stream.WriteAsync(depositFrame);
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeStorageTransferResult(
                    await ReadFrameAsync(first.Stream), out var depositReplay) &&
                depositReplay.Status == StorageTransferStatus.AlreadyApplied &&
                depositReplay.ItemUid == itemUid &&
                depositReplay.SourceDropId == drop.DropId,
                "storage AES deposit retry was not idempotent");

            await first.Stream.WriteAsync(WireProtocol.EncodeStorageOpen(
                new(choose.MapEpoch, 3, 88)));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeStorageSnapshot(
                    await ReadFrameAsync(first.Stream), out var stored) &&
                stored is not null && stored.Complete &&
                stored.Items.Count == 1 &&
                stored.Items[0].ItemUid == itemUid &&
                stored.Items[0].Quantity == 3 &&
                stored.Items[0].SourceDropId == drop.DropId,
                "storage AES stored baseline mismatch");

            var withdrawFrame = WireProtocol.EncodeStorageWithdraw(
                new(choose.MapEpoch, 4, 88, itemUid));
            await first.Stream.WriteAsync(withdrawFrame);
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeStorageTransferResult(
                    await ReadFrameAsync(first.Stream), out var withdrawn) &&
                withdrawn.Status == StorageTransferStatus.Applied &&
                withdrawn.Kind == StorageTransferKind.Withdraw &&
                withdrawn.ItemUid == itemUid &&
                withdrawn.SourceDropId == drop.DropId,
                "storage AES withdraw receipt mismatch");

            await first.Stream.WriteAsync(withdrawFrame);
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeStorageTransferResult(
                    await ReadFrameAsync(first.Stream), out var withdrawReplay) &&
                withdrawReplay.Status == StorageTransferStatus.AlreadyApplied &&
                withdrawReplay.ItemUid == itemUid,
                "storage AES withdraw retry was not idempotent");

            await first.Stream.WriteAsync(WireProtocol.EncodeStorageOpen(
                new(choose.MapEpoch, 5, 999)));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeStorageSnapshot(
                    await ReadFrameAsync(first.Stream), out var denied) &&
                denied is not null &&
                denied.Status == StorageOpenStatus.UnknownStorage &&
                denied.Complete && denied.Items.Count == 0,
                "unverified storage NPC did not fail closed");

            await first.Stream.DisposeAsync();
            first.Client.Dispose();
            var ended = await first.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(ended.Reason == AesWireEndReason.CleanEof,
                "storage AES first session did not close cleanly");

            var persisted = await store.ListInventoryItemsAsync(characterId);
            Checks.Require(persisted.Count == 1 &&
                persisted[0].ItemUid == itemUid &&
                persisted[0].ItemDefinitionId == 2701 &&
                persisted[0].Quantity == 3 &&
                persisted[0].SourceDropId == drop.DropId &&
                (await store.ListStorageItemsAsync(characterId)).Count == 0,
                "storage AES durable inventory mismatch after withdraw");

            var second = await StartRoutedSession(authorization, world, router);
            await second.Stream.WriteAsync(WireProtocol.EncodeAuthRequest(
                new("storage_tls_user", "Storage-Tls-Password-123!")));
            await second.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(second.Stream), out var auth2) &&
                auth2.Status == AuthWireStatus.Success,
                "storage AES relogin auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(second.Stream), out var list2) &&
                list2.Characters.Single().CharacterId == characterId,
                "storage AES relogin list mismatch");
            await CharacterChooseTestHarness.ChooseAsync(second.Stream,characterId);
            await second.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
                    await ReadFrameAsync(second.Stream), out var choose2) &&
                choose2.Status == CharacterChooseWireStatus.Success,
                "storage AES relogin choose failed");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                    await ReadFrameAsync(second.Stream), out var inventory2) &&
                inventory2 is not null && inventory2.Complete &&
                inventory2.Items.Count == 1 &&
                inventory2.Items[0].ItemUid == itemUid &&
                inventory2.Items[0].SourceDropId == drop.DropId,
                "storage AES Item UID/provenance did not survive relogin");

            await second.Stream.WriteAsync(WireProtocol.EncodeStorageOpen(
                new(choose2.MapEpoch, 1, 88)));
            await second.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeStorageSnapshot(
                    await ReadFrameAsync(second.Stream), out var reopened) &&
                reopened is not null &&
                reopened.Status == StorageOpenStatus.Opened &&
                reopened.Complete && reopened.Items.Count == 0,
                "storage AES relogin storage baseline should be empty");

            await second.Stream.DisposeAsync();
            second.Client.Dispose();
            var secondEnded = await second.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(secondEnded.Reason == AesWireEndReason.CleanEof,
                "storage AES relogin session did not close cleanly");
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }



    private static async Task PublicAccountRegistration(string root)
    {
        string identityRoot=TempRoot();
        try
        {
            string mapDir=Checks.Fixture(root,78,3,3,(_,_)=>0);
            var grid=BlockingGrid.Load(mapDir,78);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(
                Path.Combine(identityRoot,"identity.db"));
            await store.InitializeAsync();
            const string username="public_user";
            const string password="Public-Registration-Password-123!";

            // Default authorization is closed to public account creation.
            var disabled=new IdentitySessionAuthorization(store,world);
            var first=await StartSession(disabled,world);
            await first.Stream.WriteAsync(
                WireProtocol.EncodeAccountRegisterRequest(new(username,password)));
            await first.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAccountRegisterResult(
                    await ReadFrameAsync(first.Stream),out var disabledResult)&&
                disabledResult.Status==AccountRegisterWireStatus.Unavailable,
                "default-disabled registration did not fail closed");
            var firstEnded=await first.ServerTask.WaitAsync(TimeSpan.FromSeconds(2));
            Checks.Require(firstEnded.Reason==AesWireEndReason.AuthorizationRejected&&
                (await store.AuthenticateAsync(username,password)).Status==
                    AuthenticationStatus.InvalidCredentials,
                "disabled registration mutated identity DB or entered World");
            await first.Stream.DisposeAsync();
            first.Client.Dispose();

            var quota=new AccountRegistrationRateLimiter(new(
                MaxAttemptsPerAddress:2,
                Window:TimeSpan.FromMinutes(1),
                MaxTrackedAddresses:16));
            var enabled=new IdentitySessionAuthorization(
                store,world,registrationRateLimiter:quota);

            // Created credentials do not authenticate implicitly. The same AES
            // connection must issue the ordinary AuthRequest and receive the
            // authoritative CharacterList.
            var second=await StartSession(enabled,world);
            await second.Stream.WriteAsync(
                WireProtocol.EncodeAccountRegisterRequest(new(username,password)));
            await second.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAccountRegisterResult(
                    await ReadFrameAsync(second.Stream),out var created)&&
                created.Status==AccountRegisterWireStatus.Created,
                "enabled registration did not create account");
            await second.Stream.WriteAsync(
                WireProtocol.EncodeAuthRequest(new(username,password)));
            await second.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(
                    await ReadFrameAsync(second.Stream),out var auth)&&
                auth.Status==AuthWireStatus.Success,
                "new account did not pass ordinary AuthRequest");
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                    await ReadFrameAsync(second.Stream),out var list)&&
                list.Characters.Count==0,
                "new account did not receive authoritative empty CharacterList");
            await second.Stream.DisposeAsync();
            second.Client.Dispose();
            var secondEnded=await second.ServerTask.WaitAsync(TimeSpan.FromSeconds(2));
            Checks.Require(secondEnded.Reason==AesWireEndReason.AuthorizationRejected&&
                (await store.AuthenticateAsync(username,password)).Status==
                    AuthenticationStatus.Success,
                "registered account was not durable after normal Auth path");

            // Duplicate is explicit but consumes the same per-source quota.
            var third=await StartSession(enabled,world);
            await third.Stream.WriteAsync(
                WireProtocol.EncodeAccountRegisterRequest(new(username,password)));
            await third.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAccountRegisterResult(
                    await ReadFrameAsync(third.Stream),out var duplicate)&&
                duplicate.Status==AccountRegisterWireStatus.UsernameTaken,
                "duplicate public username status mismatch");
            await third.ServerTask.WaitAsync(TimeSpan.FromSeconds(2));
            await third.Stream.DisposeAsync();
            third.Client.Dispose();

            // A third attempt in the same window is rejected BEFORE CreateAccount.
            const string blockedUsername="quota_blocked_user";
            var fourth=await StartSession(enabled,world);
            await fourth.Stream.WriteAsync(
                WireProtocol.EncodeAccountRegisterRequest(
                    new(blockedUsername,"Quota-Blocked-Password-123!")));
            await fourth.Stream.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAccountRegisterResult(
                    await ReadFrameAsync(fourth.Stream),out var limited)&&
                limited.Status==AccountRegisterWireStatus.RateLimited,
                "registration quota did not reject the third source attempt");
            await fourth.ServerTask.WaitAsync(TimeSpan.FromSeconds(2));
            Checks.Require((await store.AuthenticateAsync(
                    blockedUsername,"Quota-Blocked-Password-123!")).Status==
                    AuthenticationStatus.InvalidCredentials&&
                world.Statistics.ActiveActors==0,
                "rate-limited registration mutated identity DB or entered World");
            await fourth.Stream.DisposeAsync();
            fourth.Client.Dispose();
            await world.StopAsync();
        }
        finally{Directory.Delete(identityRoot,true);}
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
            var authorization = new IdentitySessionAuthorization(store, world);

            var (tls, serverTask, client) = await StartSession(authorization, world);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("wrong_pw", "Incorrect-Password!")));
            await tls.FlushAsync();
            var frame = await ReadFrameAsync(tls);
            Checks.Require(WireProtocol.TryDecodeAuthResult(frame, out var result) &&
                result.Status == AuthWireStatus.InvalidCredentials, "wrong password result");
            var ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            Checks.Require(ended.Reason == AesWireEndReason.AuthorizationRejected &&
                world.Statistics.ActiveActors == 0, "wrong password entered World");
            await tls.DisposeAsync(); client.Dispose(); await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }

    private static async Task RejectedCharacterSelection(string root, bool protectedOwned=false)
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
            long targetId = protectedOwned
                ? (await store.CreateCharacterWithSettingsAsync(ownerB,0,"SecretHero",new(73,8,8),
                    new(new(2,2,2,2),"2468","synthetic/protected-choose"))).Character!.CharacterId
                : (await store.CreateCharacterAsync(ownerA,0,"ForeignHero",new(73,8,8))).Character!.CharacterId;
            if(!protectedOwned) await store.CreateCharacterAsync(ownerB,0,"OwnHero",new(73,8,8));
            var authorization = new IdentitySessionAuthorization(store, world);

            var (tls, serverTask, client) = await StartSession(authorization, world);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("owner_b", "OwnerB-Password-123!")));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeAuthResult(await ReadFrameAsync(tls), out var auth) &&
                auth.Status == AuthWireStatus.Success, "owner B auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(await ReadFrameAsync(tls), out var list) &&
                (protectedOwned ? list.Characters.Any(c=>c.CharacterId==targetId) : list.Characters.All(c=>c.CharacterId!=targetId)), "foreign character leaked into CharacterList");
            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(targetId)));
            await tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(await ReadFrameAsync(tls), out var choose) &&
                choose.Status == (protectedOwned
                    ? CharacterChooseWireStatus.PasswordRequired
                    : CharacterChooseWireStatus.NotOwned),
                protectedOwned
                    ? "protected owned character did not request secondary password"
                    : "foreign CharacterId was not rejected");
            if (protectedOwned)
            {
                Checks.Require(world.Statistics.ActiveActors == 0,
                    "proof-free protected character entered World");
                await tls.DisposeAsync();
                client.Dispose();
                var ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
                Checks.Require(ended.Reason == AesWireEndReason.AuthorizationRejected,
                    "closing password prompt unexpectedly entered gameplay authorization");
            }
            else
            {
                var ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
                Checks.Require(ended.Reason == AesWireEndReason.AuthorizationRejected &&
                    world.Statistics.ActiveActors == 0, "foreign character entered World");
                await tls.DisposeAsync();
                client.Dispose();
            }
            await world.StopAsync();
        }
        finally { Directory.Delete(identityRoot, true); }
    }



    private static async Task<(AesCbcFrameStream Stream, Task<AesWireConnectionResult> ServerTask, TcpClient Client)>
        StartRoutedSession(
            IdentitySessionAuthorization authorization, ServerWorldHost world,
            WorldResultRouter router)
    {
        var settings = AesSocketChecks.CreateSettings();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();
        var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await accepted;
        var serverTask = authorization.RunAuthenticatedAesSocketAsync(
            serverClient, settings, router).ContinueWith(t =>
            {

                listener.Stop();
                return t.GetAwaiter().GetResult();
            }, TaskScheduler.Default);
        var tls = AesSocketChecks.CreateClientStream(client);

        return (tls, serverTask, client);
    }


    private static async Task<(AesCbcFrameStream Stream, Task<AesWireConnectionResult> ServerTask, TcpClient Client)>
        StartSession(IdentitySessionAuthorization authorization, ServerWorldHost world)
    {
        var settings = AesSocketChecks.CreateSettings();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();
        var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await accepted;
        var serverTask = AesWireConnection.RunServerAsync(serverClient, settings,
            (tls,token) => authorization.AuthorizeAndBindAsync(tls,token), world.TryPostMove).ContinueWith(t =>
            {
                 listener.Stop(); return t.GetAwaiter().GetResult();
            }, TaskScheduler.Default);
        var tls = AesSocketChecks.CreateClientStream(client);

        return (tls, serverTask, client);
    }

    private static async Task<byte[]> ReadFrameAsync(
        AesCbcFrameStream tls, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[WireProtocol.HeaderBytes];
        await tls.ReadExactlyAsync(header, cancellationToken);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        Checks.Require(length >= WireProtocol.HeaderBytes && length <= WireProtocol.MaximumFrameBytes,
            "test client received invalid frame length");
        byte[] frame = new byte[length];
        header.CopyTo(frame, 0);
        if (length > header.Length)
            await tls.ReadExactlyAsync(frame.AsMemory(header.Length), cancellationToken);
        return frame;
    }

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "sgtest_tls_identity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return root;
    }
}
