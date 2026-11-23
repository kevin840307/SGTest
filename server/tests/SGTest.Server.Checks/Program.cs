using SGTest.Server.Map;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SGTest.Server.World;

static class Checks
{
    private static int _passes;

    internal static string HostAssemblyPath()
    {
        string? custom = Environment.GetEnvironmentVariable("SGTEST_CHECKS_HOST_DLL");
        if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom);
        string fixture = Path.Combine(AppContext.BaseDirectory, "host-fixture", "SGTest.Server.Host.dll");
        if (File.Exists(fixture)) return fixture;
        throw new Exception("Checks Host fixture was not built: " + fixture);
    }

    internal static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception("SERVER_NAV_FAIL " + name);
    }

    private static void MustReject<T>(Action act, string name) where T : Exception
    {
        bool rejected = false;
        try { act(); }
        catch (T) { rejected = true; }
        Require(rejected, name);
    }

    internal static void Run(string name, Action check)
    {
        check();
        _passes++;
        Console.WriteLine("PASS " + name);
    }

    internal static string Fixture(string root, int mapId, int width, int height,
                                  Func<int, int, byte> flags, int version = 3,
                                  int cell = 16)
    {
        string directory = Path.Combine(root, mapId.ToString());
        Directory.CreateDirectory(directory);
        var raw = new byte[checked(8 + width * height)];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4, 4), height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                raw[8 + y * width + x] = flags(x, y);
        File.WriteAllBytes(Path.Combine(directory, "blocking.dat"), raw);
        var metadata = new
        {
            version,
            map_id = mapId,
            collision_cell = cell,
            size = new[] { width * cell, height * cell },
            collision_sha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant()
        };
        File.WriteAllText(Path.Combine(directory, "world.json"), JsonSerializer.Serialize(metadata));
        return directory;
    }

    internal static (double X, double Y) Center(GridCell cell) =>
        (cell.X * BlockingGrid.CellPixels + 8, cell.Y * BlockingGrid.CellPixels + 8);

    private static void AssertPath(BlockingGrid grid, NavigationPath path, GridCell start, GridCell goal)
    {
        Require(path.Found && path.Cells.Count > 0, "path exists");
        Require(path.Cells[0] == start && path.Cells[^1] == goal, "path endpoints");
        for (int i = 0; i < path.Cells.Count; i++)
        {
            var curr = path.Cells[i];
            Require(grid.IsWalkable(curr), "each waypoint traversable");
            if (i == 0) continue;
            var prev = path.Cells[i - 1];
            var a = Center(prev); var b = Center(curr);
            Require(grid.IsDirectClear(a.X, a.Y, b.X, b.Y),
                    "smoothed waypoint segment must be direct-clear");
        }
    }

    public static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "sgtest_blocking_checks_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (args is ["--character-management-only"])
            {
                CharacterManagementChecks.Verify();
                Console.WriteLine($"CHARACTER_MANAGEMENT_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--clan-only"])
            {
                ClanChecks.Verify();
                Console.WriteLine($"CLAN_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--friend-only"])
            {
                FriendChecks.Verify();
                Console.WriteLine($"FRIEND_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--pet-only"])
            {
                PetChecks.Verify(root);
                Console.WriteLine($"PET_CHECKS PASS total={_passes}");
                return 0;
            }
            if(args is ["--experience-only"])
            {
                CharacterExperienceChecks.Verify(root);
                Console.WriteLine($"CHARACTER_EXPERIENCE_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--identity-admin-only", "--aes"])
            {
                IdentityAdminChecks.Verify(root, tlsOnly: true);
                Console.WriteLine($"IDENTITY_ADMIN_AES_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--identity-admin-only"])
            {
                IdentityAdminChecks.Verify(root);
                Console.WriteLine($"IDENTITY_ADMIN_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--identity-session-only"])
            {
                IdentitySessionAuthorizationChecks.Verify(root);
                IdentitySessionLifecycleChecks.Verify(root);
                Console.WriteLine($"IDENTITY_SESSION_AES_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--movement-only"])
            {
                WorldSimulationChecks.Verify(root);
                FacingChecks.Verify(root);
                Console.WriteLine($"SERVER_MOVEMENT_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--aoi-only"])
            {
                FacingChecks.Verify(root);
                WorldAoiChecks.Verify(root);
                AoiWireChecks.Verify();
                WorldAoiPumpChecks.Verify(root);
                Console.WriteLine($"SERVER_AOI_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--listener-only"])
            {
                AesGatewayListenerChecks.Verify(root);
                Console.WriteLine($"SERVER_LISTENER_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--load-only"])
            {
                WorldAoiPumpChecks.VerifyLoad(root);
                AesGatewayListenerChecks.VerifyLoad(root);
                Console.WriteLine($"SERVER_LOAD_CHECKS PASS total={_passes}");
                return 0;
            }
            if (args is ["--item-snapshot-wire-only"])
            {
                InventorySnapshotChecks.Verify();
                StorageChecks.Verify(root);
                EquipmentChecks.Verify(root);
                StorageWireChecks.Verify();
                EquipmentWireChecks.Verify();
                Console.WriteLine($"SERVER_ITEM_SNAPSHOT_CHECKS PASS total={_passes}");
                return 0;
            }
            string opened = Fixture(root, 31, 5, 5, (_, _) => 0);
            var grid = BlockingGrid.Load(opened, 31);
            var navigation = new MapNavigator(grid);

            Run("original v3 16px header and SHA recognized", () =>
            {
                Require(grid.Width == 5 && grid.Height == 5 && grid.MapId == 31, "metadata identity");
                Require(grid.SourceSha256.Length == 64 && grid.CellCount == 25, "exact cell count");
                Require(grid.FlagAt(new GridCell(4, 4)) == 0, "row-major byte");
                Require(grid.FlagAt(new GridCell(-1, 0)) is null, "out of bounds flags");
            });

            Run("world coords bounded, finite, floor not truncation", () =>
            {
                Require(grid.TryWorldCell(79.999, 79.999, out var edge) &&
                        edge == new GridCell(4, 4), "last in-bounds world pixel");
                foreach (var point in new[] { (-0.01, 0.0), (0.0, -0.01), (80.0, 0.0),
                                               (0.0, 80.0), (double.NaN, 1.0),
                                               (double.PositiveInfinity, 0.0) })
                    Require(!grid.TryWorldCell(point.Item1, point.Item2, out _), "unsafe coordinate accepted");
                Require(navigation.FindPath(-1, 0, 10, 10).Status == PathStatus.InvalidCoordinate,
                        "invalid navigation input");
                Require(!grid.IsDirectClear(8, 8, double.NaN, 24), "unsafe line accepted");
            });

            Run("original flag 0 walks; 6,7 and unknown 3 fail closed", () =>
            {
                string folder = Fixture(root, 32, 3, 3, (x, y) =>
                    (x, y) switch { (1, 0) => 6, (2, 0) => 7, (1, 1) => 3, _ => 0 });
                var collision = BlockingGrid.Load(folder, 32);
                Require(collision.FlagAt(new GridCell(1, 0)) == 6 &&
                        collision.FlagAt(new GridCell(2, 0)) == 7 &&
                        collision.FlagAt(new GridCell(1, 1)) == 3, "raw flags preserved");
                Require(!collision.IsWalkable(new GridCell(1, 0)) &&
                        !collision.IsWalkable(new GridCell(2, 0)) &&
                        !collision.IsWalkable(new GridCell(1, 1)), "unknown/special flags blocked");
                var nav = new MapNavigator(collision);
                Require(nav.FindPath(8, 8, 24, 8).Status == PathStatus.GoalBlocked,
                        "blocked goal rejected");
                Require(nav.FindPath(24, 8, 8, 8).Status == PathStatus.StartBlocked,
                        "blocked start rejected");
            });

            Run("movement step validates local destination without rerunning route supercover", () =>
            {
                Require(grid.IsStepClear(15.9, 8.0, 16.1, 8.0),
                        "adjacent verified step rejected at cell boundary");
                Require(!grid.IsStepClear(8.0, 8.0, 40.0, 8.0),
                        "movement step skipped more than one cell");
                string folder = Fixture(root, 38, 3, 3,
                    (x, y) => (x, y) == (1, 0) ? (byte)7 : (byte)0);
                var blocked = BlockingGrid.Load(folder, 38);
                Require(!blocked.IsStepClear(8.0, 8.0, 24.0, 8.0),
                        "movement step entered blocked target cell");
            });

            Run("open map direct line uses Recovered shortcut deterministically", () =>
            {
                Require(grid.IsDirectClear(8, 8, 72, 72), "empty diagonal direct");
                var route = navigation.FindPath(8, 8, 72, 72);
                AssertPath(grid, route, new GridCell(0, 0), new GridCell(4, 4));
                Require(route.Cells.Count == 2, "direct route should expose only start and goal");
                Require(route.Cells.SequenceEqual(navigation.FindPath(8, 8, 72, 72).Cells),
                        "direct route deterministic");
                Require(navigation.FindPath(8, 8, 8, 8).Cells.Count == 1, "same cell");
                MustReject<ArgumentOutOfRangeException>(() =>
                    navigation.FindPath(8, 8, 72, 72, maxExpanded: 0), "zero budget");
            });

            Run("blocked corner forbids diagonal and direct tunnelling", () =>
            {
                string folder = Fixture(root, 33, 2, 2, (x, y) =>
                    (x, y) is (1, 0) or (0, 1) ? (byte)7 : (byte)0);
                var g = BlockingGrid.Load(folder, 33);
                Require(!g.IsDirectClear(8, 8, 24, 24), "wall corner direct");
                var nav = new MapNavigator(g);
                Require(nav.FindPath(8, 8, 24, 24).Status == PathStatus.NoRoute,
                        "wall corner A*");
            });

            Run("wall with a legitimate door: direct fails, routed A* succeeds", () =>
            {
                string folder = Fixture(root, 34, 7, 7, (x, y) =>
                    x == 3 && y < 6 ? (byte)7 : (byte)0);
                var g = BlockingGrid.Load(folder, 34);
                var nav = new MapNavigator(g);
                var start = new GridCell(1, 2);
                var goal = new GridCell(5, 2);
                var a = Center(start); var b = Center(goal);
                Require(!g.IsDirectClear(a.X, a.Y, b.X, b.Y), "wall chord blocked");
                var path = nav.FindPath(a.X, a.Y, b.X, b.Y);
                AssertPath(g, path, start, goal);
                Require(path.Cells.Count >= 3, "detour must retain at least one verified bend");
                Require(nav.FindPath(a.X, a.Y, b.X, b.Y, 2).Status == PathStatus.BudgetExceeded,
                        "work budget fails closed");
                Require(path.Cells.SequenceEqual(nav.FindPath(a.X, a.Y, b.X, b.Y).Cells),
                        "route deterministic on detour");
            });

            Run("closed wall genuinely prevents movement", () =>
            {
                string folder = Fixture(root, 35, 7, 7, (x, _) => x == 3 ? (byte)6 : (byte)0);
                var g = BlockingGrid.Load(folder, 35);
                var a = Center(new GridCell(1, 2)); var b = Center(new GridCell(5, 2));
                Require(!g.IsDirectClear(a.X, a.Y, b.X, b.Y), "closed wall direct");
                Require(new MapNavigator(g).FindPath(a.X, a.Y, b.X, b.Y).Status == PathStatus.NoRoute,
                        "closed wall A*");
            });

            Run("reject stale schema, mismatched map ID and cell size", () =>
            {
                string dir = Fixture(root, 36, 4, 4, (_, _) => 0, version: 2);
                MustReject<InvalidDataException>(() => BlockingGrid.Load(dir, 36), "world v2 rejected");
                dir = Fixture(root, 36, 4, 4, (_, _) => 0, cell: 32);
                MustReject<InvalidDataException>(() => BlockingGrid.Load(dir, 36), "non16 cell rejected");
                dir = Fixture(root, 36, 4, 4, (_, _) => 0);
                MustReject<InvalidDataException>(() => BlockingGrid.Load(dir, 37), "map identity rejected");
            });

            Run("reject altered SHA, truncated, padded or inconsistent header", () =>
            {
                string dir = Fixture(root, 37, 4, 4, (_, _) => 0);
                string file = Path.Combine(dir, "blocking.dat");
                byte[] baseline = File.ReadAllBytes(file);
                var corrupted = (byte[])baseline.Clone();
                corrupted[8] = 7;
                File.WriteAllBytes(file, corrupted);
                MustReject<InvalidDataException>(() => BlockingGrid.Load(dir, 37), "hash mismatch");
                File.WriteAllBytes(file, baseline[..^1]);
                MustReject<InvalidDataException>(() => BlockingGrid.Load(dir, 37), "truncated payload");
                File.WriteAllBytes(file, baseline.Concat(new byte[] { 0 }).ToArray());
                MustReject<InvalidDataException>(() => BlockingGrid.Load(dir, 37), "padded payload");
                corrupted = (byte[])baseline.Clone();
                BinaryPrimitives.WriteInt32LittleEndian(corrupted.AsSpan(0, 4), 5);
                File.WriteAllBytes(file, corrupted);
                MustReject<InvalidDataException>(() => BlockingGrid.Load(dir, 37), "corrupt dimensions");
            });

            WireCodecChecks.Verify();
            CharacterPositionCheckpointChecks.Verify(root);
            CharacterCombatProfileChecks.Verify(root);
            GroundLootChecks.Verify(root);
            GroundPickupWireChecks.Verify();
            RecoveryItemChecks.Verify(root);
            RecoveryUseWireChecks.Verify();
            InventoryDiscardChecks.Verify(root);
            InventorySnapshotChecks.Verify();
            SkillBookChecks.Verify(root);
            SkillHealChecks.Verify(root);
            StorageChecks.Verify(root);
            EquipmentChecks.Verify(root);
            EnhancementMetadataChecks.Verify(root);
            EnhancementChecks.Verify(root);
            EnhancementCatalogChecks.Verify(root);
            EnhancementWireChecks.Verify();
            EnhancementGatewayChecks.Verify();
            EnhancementAesChecks.Verify(root);
            BankChecks.Verify(root);
            RepairCatalogChecks.Verify(root);
            RepairChecks.Verify(root);
            QuestChecks.Verify(root);
            PvpModeChecks.Verify(root);
            PvpAttackChecks.Verify(root);
            PvpIdentityChecks.Verify(root);
            PvpCombatVisualChecks.Verify(root);
            PvpSkillChecks.Verify(root);
            GmOnlineChecks.Verify(root);
            PartyChecks.Verify(root);
            ClanChecks.Verify();
            FriendChecks.Verify();
            PetChecks.Verify(root);
            CharacterExperienceChecks.Verify(root);
            TradeChecks.Verify(root);
            PlayerShopChecks.Verify(root);
            PlayerShopCoordinatorChecks.Verify(root);
            TradeCoordinatorChecks.Verify(root);
            MonsterLootSettlementChecks.Verify(root);
            NormalAttackChecks.Verify(root);
            SkillCombatChecks.Verify(root);
            PlayerCombatSettlementChecks.Verify(root);
            CombatDeathGateChecks.Verify(root);
            PlayerRespawnChecks.Verify(root);
            IdentityWireChecks.Verify();
            CharacterAppearanceWireChecks.Verify();
            GatewayConnectionChecks.Verify();
            SessionCommandWorkerChecks.Verify();
            WorldResultRouterChecks.Verify(root);
            WorldAoiChecks.Verify(root);
            AoiWireChecks.Verify();
            WorldAoiPumpChecks.Verify(root);
            NpcPortalChecks.Verify(root);
            NpcShopCatalogChecks.Verify(root);
            NpcStorageCatalogChecks.Verify(root);
            NpcBankCatalogChecks.Verify(root);
            QuestCatalogChecks.Verify(root);
            NpcPortalWireChecks.Verify();
            StorageWireChecks.Verify();
            EquipmentWireChecks.Verify();
            BankWireChecks.Verify();
            RepairWireChecks.Verify();
            TradeWireChecks.Verify();
            PlayerShopWireChecks.Verify();
            ChatWireChecks.Verify();
            TradeGatewayChecks.Verify();
            QuestWireChecks.Verify();
            EquipmentGatewayChecks.Verify();
            BankGatewayChecks.Verify();
            RepairGatewayChecks.Verify();
            QuestGatewayChecks.Verify();
            BankAesChecks.Verify(root);
            RepairAesChecks.Verify(root);
            QuestAesChecks.Verify(root);
            EquipmentAesChecks.Verify(root);
            StorageGatewayChecks.Verify();
            AesSocketChecks.Verify();
            IdentityStoreChecks.Verify();
            CharacterAppearanceChecks.Verify();
            CharacterCreationSettingsChecks.Verify();
            CharacterManagementChecks.Verify();
            AuthenticationRateLimiterChecks.Verify();
            IdentitySessionAuthorizationChecks.Verify(root);
            IdentitySessionLifecycleChecks.Verify(root);
            AesGatewayListenerChecks.Verify(root);
            IdentityAdminChecks.Verify(root);
            WorldSimulationChecks.Verify(root);
            MonsterRuntimeChecks.Verify(root);
            FacingChecks.Verify(root);
            WorldHostChecks.Verify(root);

            if (args.Length == 2 && args[0] == "--assets")
            {
                string assets = args[1];
                Run("two TRUE original private Map13/Map15 Blocking.dat manifests", () =>
                {
                    foreach (int mapId in new[] { 13, 15 })
                    {
                        var real = BlockingGrid.Load(Path.Combine(assets, "maps", mapId.ToString()), mapId);
                        Require(real.Width == 960 && real.Height == 960 && real.WorldWidth == 15360,
                                "real geometry Map" + mapId);
                        var counts = new int[256];
                        for (int y = 0; y < real.Height; y++)
                            for (int x = 0; x < real.Width; x++)
                                counts[real.FlagAt(new GridCell(x, y))!.Value]++;
                        int zero = mapId == 13 ? 872218 : 772417;
                        int six = mapId == 13 ? 1146 : 3905;
                        int seven = mapId == 13 ? 48236 : 145278;
                        Require(counts[0] == zero && counts[6] == six && counts[7] == seven &&
                                counts.Sum() == 960 * 960, "original flag histogram Map" + mapId);
                        var portal = mapId == 13 ? (X: 2560, Y: 2880) : (X: 7872, Y: 11296);
                        Require(real.TryWorldCell(portal.X, portal.Y, out var point) &&
                                real.IsWalkable(point), "real portal destination cell Map" + mapId);
                        var nav = new MapNavigator(real);
                        GridCell? neighbor = null;
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            var candidate = new GridCell(point.X + dx, point.Y + dy);
                            if (real.IsWalkable(candidate)) { neighbor = candidate; break; }
                        }
                        Require(neighbor.HasValue, "portal adjacency Map" + mapId);
                        var adjacent = neighbor.GetValueOrDefault();
                        var dest = Center(adjacent);
                        var route = nav.FindPath(portal.X, portal.Y, dest.X, dest.Y, 32);
                        AssertPath(real, route, point, adjacent);
                        Require(real.IsDirectClear(portal.X, portal.Y, dest.X, dest.Y),
                                "real source direct step Map" + mapId);
                        // Reproducible nearby route workload on real Blocking cells.
                        // Query latency/allocations here are NOT World Tick or multiplayer figures.
                        var rng = new Random(20261116 + mapId);
                        var durations = new List<double>();
                        int found = 0, rejected = 0, budgetLimited = 0;
                        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                        var benchmark = Stopwatch.StartNew();
                        for (int trial = 0; trial < 128; trial++)
                        {
                            var target = new GridCell(point.X + rng.Next(-20, 21),
                                                      point.Y + rng.Next(-20, 21));
                            if (!real.IsWalkable(target)) continue;
                            var xy = Center(target);
                            long t0 = Stopwatch.GetTimestamp();
                            var candidate = nav.FindPath(portal.X, portal.Y, xy.X, xy.Y, 1500);
                            durations.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000.0);
                            if (candidate.Found)
                            {
                                AssertPath(real, candidate, point, target);
                                found++;
                            }
                            else
                            {
                                rejected++;
                                if (candidate.Status == PathStatus.BudgetExceeded) budgetLimited++;
                                Require(candidate.Cells.Count == 0, "failed route contains unsafe steps");
                            }
                        }
                        benchmark.Stop();
                        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
                        Require(found >= 10 && durations.Count >= 60, "real route workload too small");
                        durations.Sort();
                        double p95 = durations[Math.Min(durations.Count - 1, (int)Math.Ceiling(durations.Count * 0.95) - 1)];
                        Console.WriteLine($"SOURCE Map{mapId} grid={real.Width}x{real.Height} flags=0:{zero},6:{six},7:{seven} portal_walkable=true neighbor_route={route.Cells.Count} sha_match=PASS");
                        Console.WriteLine($"NAV_PROFILE_REAL Map{mapId} attempted={durations.Count} found={found} refused={rejected} budget_limited={budgetLimited} p95_query_us={p95:F2} alloc_bytes_per_attempt={(double)allocated/durations.Count:F1} total_ms={benchmark.Elapsed.TotalMilliseconds:F2} synthetic_targets=true");

                    }
                });
                WorldSimulationChecks.VerifyOriginal(assets);
                WorldHostChecks.VerifyOriginal(assets);
            }
            else if (args.Length != 0)
            {
                Console.Error.WriteLine(
                    "usage: checks [--assets <PRIVATE_ASSETS_ROOT>] | --load-only");
                return 2;
            }

            Console.WriteLine($"SERVER_WORLD_CHECKS PASS total={_passes} scope=collision_Astar_30Hz_host_wire_auth_character_gateway_bounded_AES_listener_SQLite_identity_no_Godot_clients");
            return 0;
        }
        finally
        {
            // Owned synthetic fixtures ONLY; never delete private game source or outputs.
            Directory.Delete(root, recursive: true);
        }
    }
}
