using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.World;

internal static class NpcPortalChecks
{
    private static void Require(bool good, string label) => Checks.Require(good, label);

    public static void Verify(string root)
    {
        Checks.Run("original NPC v1 catalog accepts audited static portals only",
            () => CatalogValidation(root));
        Checks.Run("trusted NPC interaction checks map, distance, replay and Blocking atomically",
            () => WorldAuthority(root));
        Checks.Run("real 30Hz host authenticated NPC transfer updates Gateway and AOI generations",
            () => LiveHost(root).GetAwaiter().GetResult());
    }

    private static string Document(string root, int map, bool tampered = false)
    {
        string file = Path.Combine(root, "npcs_" + map + "_" + tampered + ".json");
        int spawnId = map == 15 ? 46 : 1;
        int sourceX = map == 15 ? 24 : 56;
        int target = map == 15 ? 13 : 15;
        int destination = map == 15 ? 56 : 32;
        var entries = new object[]
        {
            new
            {
                spawn_id = spawnId,
                p = new[] { sourceX, sourceX },
                interaction = new
                {
                    mode = "confirm_transfer",
                    choice = 0,
                    dependencies_complete = true,
                    adapter = tampered ? "unknown_script" : "audited_TS_Common_v1",
                    destination_status = "walkable",
                    destination = new { map_id = target, p = new[] { destination, destination } }
                }
            },
            new
            {
                spawn_id = spawnId + 100,
                p = new[] { 8, 8 },
                interaction = new
                {
                    mode = "script_required", dependencies_complete = false
                }
            }
        };
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            schema = "sgtest.npcs/v1", map_id = map,
            coordinate_space = "world_pixels",
            spawn_id_policy = "map-scoped source_index+1; fenced by content hash",
            npcs = entries
        }));
        return file;
    }

    private static PortalCatalog Catalog(string root) =>
        PortalCatalog.LoadNpcDocuments([Document(root, 15), Document(root, 13)]);

    private static void CatalogValidation(string root)
    {
        var portals = Catalog(root);
        Require(portals.Count == 2 && portals.TryGet(15, 46, out var outgoing) &&
                outgoing.DestinationMapId == 13 &&
                outgoing.DestinationX == 56 && outgoing.NpcX == 24 &&
                portals.TryGet(13, 1, out var home) &&
                home.DestinationMapId == 15 && home.DestinationY == 32,
                "static evidence not loaded");
        Require(!portals.TryGet(15, 146, out _) && !portals.TryGet(15, 1, out _) &&
                !portals.TryGet(13, 101, out _),
                "dynamic scripts/other-map spawns became unaudited portals");
        bool bad = false, duplicateMap = false;
        try { PortalCatalog.LoadNpcDocuments([Document(root, 15, tampered: true)]); }
        catch (InvalidDataException) { bad = true; }
        try { PortalCatalog.LoadNpcDocuments([Document(root, 15), Document(root, 15)]); }
        catch (InvalidDataException) { duplicateMap = true; }
        Require(bad && duplicateMap, "tampered or duplicate NPC map accepted");
        bool badRadius = false;
        try { _ = new PortalCatalog([], maximumInteractionDistance: 10000); }
        catch (ArgumentOutOfRangeException) { badRadius = true; }
        Require(badRadius, "unbounded NPC interaction radius accepted");
    }

    private static void WorldAuthority(string root)
    {
        // The source NPC policy maps 15->13; synthetic Blocking values are
        // explicitly fixtures, never private original map data.
        var original15 = BlockingGrid.Load(Checks.Fixture(root, 15, 32, 32, (_, _) => 0), 15);
        var original13 = BlockingGrid.Load(Checks.Fixture(root, 13, 32, 32, (_, _) => 0), 13);
        var world = new ServerWorld([original15, original13]);
        var catalog = Catalog(root);
        Require(world.TryTrustedRegister(900, 15, 200, 200, out var far),
            "far character register");
        var farRequest = world.TryTrustedNpcInteract(900, far.ConnectionEpoch,
            far.MapEpoch, 1, 46, catalog);
        Require(farRequest.Status == NpcPortalStatus.TooFar &&
                world.GetActor(900)?.MapId == 15, "remote NPC click transferred player");
        Require(world.TryTrustedDisconnect(900, far.ConnectionEpoch, far.MapEpoch),
                "far actor logout");
        Require(world.TryTrustedRegister(901, 15, 24, 24, out var a),
            "near NPC actor register");

        var wrong = world.TryTrustedNpcInteract(901, a.ConnectionEpoch, a.MapEpoch,
            1, 146, catalog);
        var replay = world.TryTrustedNpcInteract(901, a.ConnectionEpoch, a.MapEpoch,
            1, 46, catalog);
        Require(wrong.Status == NpcPortalStatus.UnknownNpc &&
                replay.Status == NpcPortalStatus.ReplayedSequence,
                "wrong NPC or replay changed map");
        var accepted = world.TryTrustedNpcInteract(901, a.ConnectionEpoch, a.MapEpoch,
            2, 46, catalog);
        Require(accepted.Status == NpcPortalStatus.Accepted &&
                accepted.TransferredActor is { MapId: 13 } in13 &&
                in13.MapEpoch > a.MapEpoch && in13.X == 56,
                "validated NPC click did not transfer to Map13");
        Require(world.TryTrustedNpcInteract(901, a.ConnectionEpoch, a.MapEpoch,
                3, 46, catalog).Status == NpcPortalStatus.StaleGeneration,
                "old-map request performed a second transfer");
        var returned = world.TryTrustedNpcInteract(901, in13.ConnectionEpoch,
            in13.MapEpoch, 1, 1, catalog);
        Require(returned.Status == NpcPortalStatus.Accepted &&
                returned.TransferredActor is { MapId: 15 } home &&
                home.MapEpoch > in13.MapEpoch && home.X == 32,
                "Map13 audited NPC failed to return to Map15");
        Require(world.GetActor(901) is { MapId: 15, X: 32 },
            "authoritative final position mismatch");
        // Even audited portal metadata cannot override real destination
        // Blocking. The immutable World receives a blocked Map13 fixture.
        var blockedMap13 = BlockingGrid.Load(Checks.Fixture(root, 13, 32, 32,
            (x, y) => x == 3 && y == 3 ? (byte)7 : (byte)0), 13);
        var blockedWorld = new ServerWorld([original15, blockedMap13]);
        Require(blockedWorld.TryTrustedRegister(902, 15, 24, 24, out var blockedActor),
            "blocked-destination source actor registration");
        var rejected = blockedWorld.TryTrustedNpcInteract(902,
            blockedActor.ConnectionEpoch, blockedActor.MapEpoch, 1, 46, catalog);
        Require(rejected.Status == NpcPortalStatus.DestinationRejected &&
                rejected.TransferredActor is null &&
                blockedWorld.GetActor(902)?.MapId == 15,
                "NPC portal bypassed destination Blocking and mutated source actor");

    }

    private static async Task LiveHost(string root)
    {
        string map15 = Checks.Fixture(root, 15, 32, 32, (_, _) => 0);
        string map13 = Checks.Fixture(root, 13, 32, 32, (_, _) => 0);
        await using var host = new ServerWorldHost([
            BlockingGrid.Load(map15, 15), BlockingGrid.Load(map13, 13)]);
        await host.StartAsync();
        var router = new WorldResultRouter();
        var catalog = Catalog(root);
        var pump = new WorldAoiPump(router);
        using var cancel = new CancellationTokenSource();
        var routing = router.RunAsync(host.Results);
        var aoi = pump.RunAsync(cancel.Token);
        try
        {
            var actor = await host.TrustedRegisterAsync(601, 15, 24, 24)
                ?? throw new Exception("NPC host actor registration rejected");
            var connection = new GatewayConnection(actor);
            Require(router.TryAttach(connection), "trusted NPC Gateway attach");
            var rejected = await router.TrustedNpcInteractAsync(
                host, catalog, connection.Binding, 1, 147);
            Require(rejected.Status == NpcPortalStatus.UnknownNpc &&
                    connection.Binding.MapEpoch == actor.MapEpoch,
                "invalid NPC altered live Gateway");
            var go = await router.TrustedNpcInteractAsync(
                host, catalog, connection.Binding, 2, 46);
            Require(go.Status == NpcPortalStatus.Accepted &&
                    go.TransferredActor is { MapId: 13 } moved &&
                    connection.Binding.MapEpoch == moved.MapEpoch,
                "host NPC transfer did not advance trusted Gateway");
            var back = await router.TrustedNpcInteractAsync(
                host, catalog, connection.Binding, 1, 1);
            Require(back.Status == NpcPortalStatus.Accepted &&
                    back.TransferredActor is { MapId: 15 } returned &&
                    returned.MapEpoch > moved.MapEpoch &&
                    connection.Binding.MapEpoch == returned.MapEpoch,
                "host NPC return failed");
            Require(router.TryDetach(connection) &&
                    await host.TrustedDisconnectAsync(
                        returned.ActorId, returned.ConnectionEpoch, returned.MapEpoch),
                "NPC player logout after transfer failed");
            var timeout = DateTime.UtcNow.AddSeconds(3);
            while (host.Statistics.ActiveActors != 0)
            {
                if (DateTime.UtcNow > timeout) throw new Exception("ghost actor after NPC transfer");
                await Task.Delay(5);
            }
        }
        finally
        {
            cancel.Cancel();
            await aoi.WaitAsync(TimeSpan.FromSeconds(3));
            await host.StopAsync();
            await routing.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
}
