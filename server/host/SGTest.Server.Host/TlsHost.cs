using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class TlsHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var options = Parse(args);
            string root = Path.GetFullPath(Required(options, "--content"));
            string database = Path.GetFullPath(Required(options, "--identity"));
            string certificatePath = Path.GetFullPath(Required(options, "--certificate"));
            if (!IPAddress.TryParse(options.GetValueOrDefault("--bind", "127.0.0.1"), out var address))
                throw new ArgumentException("--bind must be an IP address");
            int port = Number(options, "--port", 7777, 0, 65535);
            int maximum = Number(options, "--max-connections", 128, 1, 4096);
            int perAddress = Number(options, "--max-per-address", Math.Min(8, maximum), 1, maximum);
            int checkpointMs = Number(options, "--position-checkpoint-ms", 5000, 500, 60000);
            if (options.ContainsKey("--seconds"))
                stop.CancelAfter(TimeSpan.FromSeconds(Number(options, "--seconds", 1, 1, 86400)));
            using var certificate = new X509Certificate2(certificatePath,
                Environment.GetEnvironmentVariable("SGTEST_TLS_PASSWORD"),
                // Schannel needs an accessible user-key container. Do not mark
                // it Exportable/PersistKeySet; disposal releases the temporary
                // imported key. Linux can use an ephemeral in-memory key.
                OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
            var maps = new[] { 13, 15 }.Select(id =>
                BlockingGrid.Load(Path.Combine(root, "maps", id.ToString()), id)).ToArray();

            string[] characterSpawnKeys =
                ["--character-spawn-map", "--character-spawn-x", "--character-spawn-y"];
            bool anyCharacterSpawn =
                characterSpawnKeys.Any(options.ContainsKey);
            if (anyCharacterSpawn &&
                characterSpawnKeys.Any(key => !options.ContainsKey(key)))
                throw new ArgumentException(
                    "Character creation spawn requires map, x and y together");
            CharacterCreationPolicy? characterCreation = null;
            if (anyCharacterSpawn)
            {
                int mapId = Number(options, "--character-spawn-map", 0, 1, 65535);
                double x = Real(options, "--character-spawn-x");
                double y = Real(options, "--character-spawn-y");
                var map = maps.SingleOrDefault(candidate => candidate.MapId == mapId)
                    ?? throw new ArgumentException(
                        "--character-spawn-map must be one of the served maps");
                if (!map.TryWorldCell(x, y, out var cell) || !map.IsWalkable(cell))
                    throw new ArgumentException(
                        "Character creation spawn must be inside a walkable Blocking cell");
                characterCreation = new(new CharacterSpawn(mapId, x, y));
            }

            var monsterRuntimes = new List<MonsterRuntime>();
            foreach (var map in maps)
            {
                string populationPath = Path.Combine(
                    root, "maps", map.MapId.ToString(), "population.json");
                if (!File.Exists(populationPath))
                    continue;
                MonsterPopulation population;
                try
                {
                    population = MonsterPopulationCatalog.LoadVerified(
                        populationPath, map.MapId, map);
                }
                catch (InvalidDataException error)
                {
                    throw new ArgumentException(
                        $"Map {map.MapId} monster population is not verified gameplay content: {error.Message}",
                        error);
                }
                if (population.Spawns.Count > 0)
                    monsterRuntimes.Add(new MonsterRuntime(map, population));
            }
            string? skillManifest=options.GetValueOrDefault("--skill-rules");
            IReadOnlyDictionary<int,VerifiedSkillRule>? skillRules=
                skillManifest is null ? null :
                SkillRuleCatalog.LoadVerified(Path.GetFullPath(skillManifest));
            MonsterWorldCoordinator? monsters = monsterRuntimes.Count == 0
                ? null : new MonsterWorldCoordinator(monsterRuntimes,skillRules);
            // No original source-verified manifest means no fabricated drops.
            string? lootManifest = options.GetValueOrDefault("--loot-rules");
            IReadOnlyList<MonsterDropRule>? dropRules = lootManifest is null
                ? null : MonsterDropCatalog.LoadVerified(
                    Path.GetFullPath(lootManifest),monsterRuntimes);
            // Explicit audited content only. If omitted, no HP recovery item
            // can be used; client-side names or .tres never grant authority.
            string? recoveryManifest=options.GetValueOrDefault("--recovery-rules");
            IReadOnlyDictionary<int,RecoveryItemRule>? recoveryRules=
                recoveryManifest is null ? null :
                RecoveryItemCatalog.LoadVerified(Path.GetFullPath(recoveryManifest));
            var npcDocuments = new[] { 13, 15 }.Select(id =>
                Path.Combine(root, "maps", id.ToString(), "npcs.json")).ToArray();
            var portals = PortalCatalog.LoadNpcDocuments(npcDocuments);
            string? shopManifest=options.GetValueOrDefault("--shop-rules");
            NpcShopCatalog? shops=shopManifest is null ? null :
                NpcShopCatalog.LoadVerified(
                    Path.GetFullPath(shopManifest),npcDocuments);
            string? storageManifest=options.GetValueOrDefault("--storage-rules");
            NpcStorageCatalog? storages=storageManifest is null ? null :
                NpcStorageCatalog.LoadVerified(
                    Path.GetFullPath(storageManifest),npcDocuments);
            string? equipmentManifest=options.GetValueOrDefault("--equipment-rules");
            IReadOnlyDictionary<int,EquipmentRule>? equipmentRules=
                equipmentManifest is null ? null :
                EquipmentItemCatalog.LoadVerified(Path.GetFullPath(equipmentManifest));
            string? bankManifest=options.GetValueOrDefault("--bank-rules");
            NpcBankCatalog? banks=bankManifest is null ? null :
                NpcBankCatalog.LoadVerified(
                    Path.GetFullPath(bankManifest),npcDocuments);
            string? repairManifest=options.GetValueOrDefault("--repair-rules");
            string? repairItemManifest=options.GetValueOrDefault("--repair-item-rules");
            if((repairManifest is null)!=(repairItemManifest is null))
                throw new ArgumentException(
                    "--repair-rules and --repair-item-rules must be supplied together");
            NpcRepairCatalog? repairs=repairManifest is null ? null :
                NpcRepairCatalog.LoadVerified(
                    Path.GetFullPath(repairManifest),npcDocuments);
            IReadOnlyDictionary<int,RepairItemRule>? repairItemRules=
                repairItemManifest is null ? null :
                RepairItemCatalog.LoadVerified(Path.GetFullPath(repairItemManifest));
            string? questManifest=options.GetValueOrDefault("--quest-rules");
            NpcQuestCatalog? quests=questManifest is null ? null :
                NpcQuestCatalog.LoadVerified(
                    Path.GetFullPath(questManifest),npcDocuments);
            string? tradeItemManifest=options.GetValueOrDefault("--trade-item-rules");
            IReadOnlyDictionary<int,TradeItemRule>? tradeItemRules=
                tradeItemManifest is null ? null :
                TradeItemCatalog.LoadVerified(Path.GetFullPath(tradeItemManifest));
            using var identity = new SqliteIdentityStore(database);
            await identity.InitializeAsync(stop.Token);
            // Reserve even when no drop rule table is enabled, preventing
            // pick-up replay-key collisions after a process restart.
            long lootGeneration = await identity.AllocateLootWorldGenerationAsync(
                stop.Token);
            MonsterLootSettlement? loot = dropRules is null
                ? null : new MonsterLootSettlement(identity,lootGeneration,dropRules);
            // One startup-only SQLite snapshot. Map transfers, reconnect and
            // WorldReady resync rebuild from the SAME map-bucketed AOI state.
            var activeDrops = new List<GroundDropRecord>();
            foreach (var map in maps)
            {
                var entries = await identity.ListActiveGroundDropsAsync(
                    map.MapId,10000,stop.Token);
                // Saturated snapshot can hide records; reject instead of
                // silently forgetting items. Production never publishes
                // manually injected synthetic test merchandise.
                if (entries.Count >= 10000 ||
                    entries.Any(x=>x.Trust!=LootSourceTrust.Verified))
                    throw new InvalidDataException(
                        "Ground drop startup snapshot saturated or synthetic test loot found");
                activeDrops.AddRange(entries);
            }
            await using var world = new ServerWorldHost(
                maps,
                options: monsters is null
                    ? null
                    : new WorldHostOptions(PublishIdleTicks: true));
            var positions = new CharacterPositionCheckpoint(identity,
                new(IntervalMilliseconds: checkpointMs));
            var router = new WorldResultRouter(changedObserver: positions.Observe);
            var combat = new PlayerCombatSettlement(
                identity,
                delivery: router.QueuePlayerDamage,
                committedLifecycle: async result =>
                {
                    if(result.SourceKind==PlayerDamageSourceKind.PlayerPvp)
                    {
                        var now=DateTimeOffset.UtcNow;
                        if(result.Died)
                            await identity.MarkPvpWhiteKillAsync(
                                result.AttackerActorId,result.VictimWasNormalWhite,now)
                                .ConfigureAwait(false);
                        else
                            await identity.MarkPvpHostileAttackAsync(
                                result.AttackerActorId,result.VictimWasNormalWhite,now)
                                .ConfigureAwait(false);
                    }
                    if (result.Died)
                    {
                        var stopped = await world.TrustedSetCombatDeadAsync(
                            result.ActorId, result.ConnectionEpoch, result.MapEpoch, true)
                            .ConfigureAwait(false);
                        if (stopped is null)
                            throw new PlayerCombatSettlementException(
                                "Committed lethal damage could not stop authoritative World actor");
                    }
                    if(result.SourceKind==PlayerDamageSourceKind.Monster&&
                       !router.TryPostPlayerDamageVisual(result))
                        throw new PlayerCombatSettlementException(
                            "Committed player damage visual queue full");
                });
            PlayerTradeCoordinator? trades=tradeItemRules is null
                ? null:new PlayerTradeCoordinator(identity,world,tradeItemRules,combat);
            var aoi = new WorldAoiPump(
                router, monsters: monsters, playerCombat: combat, loot: loot,
                initialGroundDrops: activeDrops);
            var authorization = new IdentityTlsAuthorization(
                identity, world, portals, positions, combat,
                pickupWorldGeneration: lootGeneration,
                recoveryRules: recoveryRules,
                characterCreation: characterCreation,
                shops: shops,
                storages: storages,
                equipmentRules: equipmentRules,
                banks: banks,
                repairs: repairs,
                repairItemRules: repairItemRules,
                quests: quests,
                trades: trades);
            await using var listener = new TlsGatewayListener(address, port, authorization,
                certificate, router, new(MaxConnections: maximum, MaxConnectionsPerAddress: perAddress));
            await world.StartAsync();
            using var pumps = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results, pumps.Token);
            var visibility = aoi.RunAsync(pumps.Token);
            var checkpointing = positions.RunAsync(pumps.Token);
            var combatSettlement = combat.RunAsync(pumps.Token);
            // Unlike other continuously-polled pumps, drain queued loot on
            // controlled shutdown before releasing the shared SQLite store.
            var lootSettlement = loot?.RunAsync() ??
                Task.Delay(Timeout.Infinite,pumps.Token);
            try
            {
                var accepting = listener.RunAsync(stop.Token);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    result = "TLS_HOST_READY", endpoint = listener.LocalEndpoint.ToString(),
                    max_connections = maximum, max_per_address = perAddress,
                    maps = maps.Select(m => m.MapId), audited_portals = portals.Count,
                    monster_maps = monsterRuntimes.Select(m => m.MapId),
                    monsters = monsterRuntimes.Sum(m => m.Count),
                    position_checkpoint_ms = checkpointMs,
                    loot_rule_count = dropRules?.Count ?? 0,
                    verified_recovery_rule_count = recoveryRules?.Count ?? 0,
                    verified_skill_rule_count = skillRules?.Count ?? 0,
                    verified_shop_count = shops?.Count ?? 0,
                    verified_storage_count = storages?.Count ?? 0,
                    verified_equipment_rule_count = equipmentRules?.Count ?? 0,
                    verified_bank_count = banks?.Count ?? 0,
                    verified_repair_count = repairs?.Count ?? 0,
                    verified_repair_item_rule_count = repairItemRules?.Count ?? 0,
                    verified_quest_count = quests?.Count ?? 0,
                    verified_trade_item_rule_count = tradeItemRules?.Count ?? 0,
                    unpicked_ground_drops = activeDrops.Count,
                    character_creation_enabled = characterCreation is not null,
                    character_creation_map = characterCreation?.Spawn.MapId
                }));
                var stopping = Task.Delay(Timeout.Infinite, stop.Token);
                var completed = await Task.WhenAny(
                    accepting, routing, visibility, checkpointing, combatSettlement,
                    lootSettlement, stopping);
                if (completed != stopping && !stop.IsCancellationRequested)
                {
                    await completed; // propagate faults; never serve after a result/AOI pump failed
                    throw new InvalidOperationException("A live server pipeline stopped unexpectedly");
                }
            }
            finally
            {
                stop.Cancel();
                try
                {
                    // World and pumps remain live until authenticated sockets have
                    // completed their trusted Disconnect/Router detach cleanup.
                    await listener.DisposeAsync();
                }
                finally
                {
                    await world.StopAsync();
                    pumps.Cancel();
                    try { await Task.WhenAll(
                        routing, visibility, checkpointing, combatSettlement); }
                    catch (OperationCanceledException) when (pumps.IsCancellationRequested) { }
                    finally
                    {
                        loot?.Complete();
                        if (loot is not null) await lootSettlement;
                    }
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result = "TLS_HOST_STOPPED", listener = listener.Statistics,
                world = world.Statistics, router = router.Statistics,
                monster_coordinator = monsters?.Statistics,
                positions = positions.Statistics,
                combat = combat.Statistics,
                loot = loot?.Statistics,
                verified_recovery_rule_count = recoveryRules?.Count ?? 0,
                verified_skill_rule_count = skillRules?.Count ?? 0,
                verified_shop_count = shops?.Count ?? 0,
                verified_storage_count = storages?.Count ?? 0,
                verified_equipment_rule_count = equipmentRules?.Count ?? 0,
                verified_bank_count = banks?.Count ?? 0,
                verified_repair_count = repairs?.Count ?? 0,
                verified_repair_item_rule_count = repairItemRules?.Count ?? 0,
                verified_quest_count = quests?.Count ?? 0,
                verified_trade_item_rule_count = tradeItemRules?.Count ?? 0
            }));
            return 0;
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine("TLS_HOST_CONFIGURATION: " + error.Message);
            Console.Error.WriteLine("Usage: serve --content <PRIVATE_ROOT> --identity <DB> --certificate <PFX> " +
                "[--bind 127.0.0.1] [--port 7777] [--max-connections 128] [--max-per-address 8] " +
                "[--position-checkpoint-ms 5000] [--loot-rules <VERIFIED_JSON>] " +
                "[--recovery-rules <VERIFIED_JSON>] [--skill-rules <VERIFIED_JSON>] " +
                "[--shop-rules <VERIFIED_JSON>] [--storage-rules <VERIFIED_JSON>] " +
                "[--equipment-rules <VERIFIED_JSON>] [--bank-rules <VERIFIED_JSON>] " +
                "[--repair-rules <VERIFIED_JSON> --repair-item-rules <VERIFIED_JSON>] " +
                "[--quest-rules <VERIFIED_JSON>] [--trade-item-rules <VERIFIED_JSON>] " +
                "[--character-spawn-map <13|15> --character-spawn-x <X> --character-spawn-y <Y>] " +
                "[--seconds N]");
            return 2;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception error) when (error is IOException or InvalidOperationException or
                                     CryptographicException or System.Net.Sockets.SocketException or
                                     WorldResultRoutingException or AoiBackpressureException or
                                     CharacterPositionCheckpointException or
                                     PlayerCombatSettlementException or MonsterLootSettlementException or InvalidDataException)
        {
            Console.Error.WriteLine("TLS_HOST_FAIL: " + error.GetType().Name + ": " + error.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        string[] known = ["--content", "--identity", "--certificate", "--bind", "--port",
                          "--max-connections", "--max-per-address", "--position-checkpoint-ms",
                          "--loot-rules", "--recovery-rules", "--skill-rules",
                          "--shop-rules", "--storage-rules", "--equipment-rules", "--bank-rules",
                          "--repair-rules", "--repair-item-rules", "--quest-rules",
                          "--trade-item-rules",
                          "--character-spawn-map", "--character-spawn-x",
                          "--character-spawn-y", "--seconds"];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0) throw new ArgumentException("Every option requires a value");
        for (int i = 0; i < args.Length; i += 2)
            if (!known.Contains(args[i]) || string.IsNullOrWhiteSpace(args[i + 1]) ||
                !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown, duplicate or empty option: " + args[i]);
        return values;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Missing " + name);

    private static int Number(Dictionary<string, string> options, string name, int fallback, int min, int max)
    {
        if (!options.TryGetValue(name, out var value)) return fallback;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) &&
               number >= min && number <= max
            ? number : throw new ArgumentException("Invalid " + name);
    }

    private static double Real(Dictionary<string, string> options, string name)
    {
        string value = Required(options, name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double number) && double.IsFinite(number)
            ? number : throw new ArgumentException("Invalid " + name);
    }
}
