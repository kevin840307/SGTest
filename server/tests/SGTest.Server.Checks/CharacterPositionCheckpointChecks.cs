using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class CharacterPositionCheckpointChecks
{
    public static void Verify(string root)
    {
        Checks.Run("position checkpoint coalesces latest revision and final save cannot be regressed",
            () => LatestAndFinal(root).GetAwaiter().GetResult());
        Checks.Run("position checkpoint bounded background loop persists dirty authoritative state",
            () => Background(root).GetAwaiter().GetResult());
    }

    private static async Task LatestAndFinal(string root)
    {
        string dir = Path.Combine(root, "checkpoint_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var store = new SqliteIdentityStore(Path.Combine(dir, "identity.db"));
        await store.InitializeAsync();
        long account = (await store.CreateAccountAsync(
            "checkpoint_user", "Checkpoint-Password-123!")).AccountId;
        long character = (await store.CreateCharacterAsync(account,0,"CheckpointHero",
            new CharacterSpawn(15,8,8))).Character!.CharacterId;
        var positions = new CharacterPositionCheckpoint(store,
            new(IntervalMilliseconds:500, MaxTrackedCharacters:8));

        var first = Actor(character,1,1,2,15,24,8);
        var latest = Actor(character,1,1,3,15,40,8);
        positions.Observe(new WorldChangedBatch(1,[first]));
        positions.Observe(new WorldChangedBatch(2,[latest]));
        await positions.FlushAsync();
        RequirePosition(await store.ListCharactersAsync(account),40,8,"coalesced latest");

        var final = Actor(character,1,1,4,15,56,8);
        await positions.FinalizeAsync(final);
        // A delayed old batch after finalization must be ignored, not dirty the tombstone.
        positions.Observe(new WorldChangedBatch(3,[latest]));
        await positions.FlushAsync();
        RequirePosition(await store.ListCharactersAsync(account),56,8,"stale after final");

        // A strictly newer connection generation is a legitimate reconnect session.
        var reconnected = Actor(character,2,2,1,15,72,8);
        positions.Observe(new WorldChangedBatch(4,[],
            [new ActorLifecycleEvent(ActorLifecycleKind.Joined,reconnected)]));
        await positions.FlushAsync();
        RequirePosition(await store.ListCharactersAsync(account),72,8,"new generation");
        var stats = positions.Statistics;
        Checks.Require(stats.PeriodicRowsWritten == 2 && stats.FinalRowsWritten == 1 &&
            stats.DirtyCharacters == 0, "checkpoint accounting");
    }

    private static async Task Background(string root)
    {
        string dir = Path.Combine(root, "checkpoint_bg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var store = new SqliteIdentityStore(Path.Combine(dir, "identity.db"));
        await store.InitializeAsync();
        long account = (await store.CreateAccountAsync(
            "checkpoint_bg", "Checkpoint-Password-456!")).AccountId;
        long character = (await store.CreateCharacterAsync(account,0,"CheckpointBg",
            new CharacterSpawn(13,8,8))).Character!.CharacterId;
        var positions = new CharacterPositionCheckpoint(store,
            new(IntervalMilliseconds:500, MaxTrackedCharacters:8));
        using var stop = new CancellationTokenSource();
        var task = positions.RunAsync(stop.Token);
        positions.Observe(new WorldChangedBatch(1,
            [Actor(character,1,1,2,13,24,24)]));
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while ((await store.ListCharactersAsync(account))[0].X != 24)
        {
            if (DateTime.UtcNow >= deadline)
                throw new Exception("background checkpoint did not persist within bound");
            await Task.Delay(25);
        }
        stop.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(2));
        Checks.Require(positions.Statistics.PeriodicRowsWritten == 1,
            "background checkpoint wrote unexpected rows");
    }

    private static ActorSnapshot Actor(long id,long connection,long mapEpoch,long revision,
        int map,double x,double y) =>
        new(id,map,connection,mapEpoch,0,0,x,y,revision,false,1);

    private static void RequirePosition(IReadOnlyList<CharacterRecord> rows,
        double x,double y,string name) =>
        Checks.Require(rows.Count==1 && Math.Abs(rows[0].X-x)<0.001 &&
            Math.Abs(rows[0].Y-y)<0.001,name);
}
