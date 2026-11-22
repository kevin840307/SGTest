using SGTest.Server.Map;
using SGTest.Server.World;

internal static class WorldSimulationChecks
{
    private static void Check(bool condition, string reason) => Checks.Require(condition, reason);

    private static MoveTargetCommand Move(ActorSnapshot actor, uint sequence,
                                          double x, double y) =>
        new(actor.ActorId, actor.ConnectionEpoch, actor.MapEpoch, sequence, x, y);

    private static MoveReceipt HasReceipt(WorldTickResult result, MoveReceiptStatus status)
    {
        var matches = result.Receipts.Where(r => r.Status == status).ToArray();
        Check(matches.Length > 0, "missing command receipt: " + status);
        return matches[0];
    }

    public static void Verify(string root)
    {
        string mapA = Checks.Fixture(root, 80, 8, 8,
            (x, y) => x == 3 && y < 7 ? (byte)7 : (byte)0);
        string mapB = Checks.Fixture(root, 81, 8, 8, (_, _) => 0);
        var a = BlockingGrid.Load(mapA, 80);
        var b = BlockingGrid.Load(mapB, 81);
        var both = new[] { a, b };

        Checks.Run("World constructor requires bounded policy and original map identities", () =>
        {
            try
            {
                _ = new ServerWorld([]);
                throw new Exception("empty maps accepted");
            }
            catch (ArgumentException) { }
            try
            {
                _ = new ServerWorld([a, a]);
                throw new Exception("duplicate map accepted");
            }
            catch (ArgumentException) { }
            try
            {
                _ = new ServerWorld([a], new WorldLimits(MaxPendingCommands: 0));
                throw new Exception("zero capacity accepted");
            }
            catch (ArgumentOutOfRangeException) { }
            try
            {
                _ = new ServerWorld([a], new WorldLimits(MovementPixelsPerTick: double.PositiveInfinity));
                throw new Exception("unbounded movement accepted");
            }
            catch (ArgumentOutOfRangeException) { }
        });

        Checks.Run("trusted register rejects blocked spawn and duplicate identities", () =>
        {
            var world = new ServerWorld(both, new WorldLimits(MaxActors: 1, MaxKnownActorIds: 2));
            Check(!world.TryTrustedRegister(9, 80, 56, 24, out _), "blocked spawn accepted");
            Check(!world.TryTrustedRegister(9, 82, 24, 24, out _), "unknown map accepted");
            Check(!world.TryTrustedRegister(-1, 80, 24, 24, out _), "invalid identity accepted");
            Check(world.TryTrustedRegister(9, 80, 24, 24, out var first), "valid registration failed");
            Check(first.ConnectionEpoch == 1 && first.MapEpoch == 1, "initial epoch");
            Check(!world.TryTrustedRegister(9, 80, 24, 24, out _), "duplicate actor");
            Check(!world.TryTrustedRegister(10, 81, 24, 24, out _), "max active actor");
            Check(world.TryTrustedDisconnect(9, first.ConnectionEpoch, first.MapEpoch), "disconnect");
            Check(world.TryTrustedRegister(10, 81, 24, 24, out _), "second known actor");
            Check(!world.TryTrustedRegister(11, 81, 24, 24, out _),
                  "known actor bound must fail closed");
        });

        Checks.Run("nonblocking bounded MPSC ingress and Tick command budget", () =>
        {
            var limits = new WorldLimits(MaxPendingCommands: 2, MaxCommandsPerTick: 1);
            var world = new ServerWorld([b], limits);
            Check(world.TryTrustedRegister(12, 81, 24, 24, out var session), "register");
            Check(world.TryPostMove(Move(session, 1, 72, 24)), "first command enqueue");
            Check(world.TryPostMove(Move(session, 2, 88, 24)), "second command enqueue");
            Check(!world.TryPostMove(Move(session, 3, 104, 24)), "over-capacity enqueue");
            Check(world.PendingCommands == 2, "capacity tracking");
            var one = world.Tick();
            Check(one.Receipts.Count == 1 && world.PendingCommands == 1,
                  "bounded drain stage 1");
            var two = world.Tick();
            Check(two.Receipts.Count == 1 && world.PendingCommands == 0,
                  "bounded drain stage 2");
        });

        Checks.Run("connection/map generations and anti-replay movement sequencing", () =>
        {
            var world = new ServerWorld([b]);
            Check(world.TryTrustedRegister(13, 81, 24, 24, out var session), "register");
            Check(world.TryPostMove(Move(session with { ConnectionEpoch = 0 }, 1, 72, 24)),
                  "enqueue obsolete connection");
            var oldConnection = HasReceipt(world.Tick(), MoveReceiptStatus.OldConnectionEpoch);
            Check(oldConnection.ActorId == session.ActorId &&
                  oldConnection.ConnectionEpoch == 0 &&
                  oldConnection.MapEpoch == session.MapEpoch,
                  "old connection receipt lost command generations");
            Check(world.TryPostMove(Move(session with { MapEpoch = 0 }, 1, 72, 24)),
                  "enqueue obsolete map");
            var oldMap = HasReceipt(world.Tick(), MoveReceiptStatus.OldMapEpoch);
            Check(oldMap.ConnectionEpoch == session.ConnectionEpoch &&
                  oldMap.MapEpoch == 0,
                  "old map receipt lost command generations");
            Check(world.TryPostMove(Move(session, 0, 72, 24)), "enqueue sequence zero");
            HasReceipt(world.Tick(), MoveReceiptStatus.ReplayedSequence);
            Check(world.TryPostMove(Move(session, 1, 72, 24)), "enqueue move");
            var acceptedReceipt = HasReceipt(world.Tick(), MoveReceiptStatus.Accepted);
            Check(acceptedReceipt.ConnectionEpoch == session.ConnectionEpoch &&
                  acceptedReceipt.MapEpoch == session.MapEpoch,
                  "accepted receipt lost active generations");
            Check(world.TryPostMove(Move(session, 1, 72, 24)), "enqueue replay");
            HasReceipt(world.Tick(), MoveReceiptStatus.ReplayedSequence);
            Check(world.GetActor(13)!.Value.LastMovementSequence == 1,
                  "replay must not advance sequence");
        });

        Checks.Run("bad target consumes sequence but cannot teleport or replace valid route", () =>
        {
            var world = new ServerWorld(both);
            Check(world.TryTrustedRegister(14, 80, 24, 24, out var session), "register");
            Check(world.TryPostMove(Move(session, 1, 24, double.NaN)), "enqueue nan");
            HasReceipt(world.Tick(), MoveReceiptStatus.InvalidTarget);
            Check(world.TryPostMove(Move(session, 2, 56, 24)), "enqueue blocked");
            HasReceipt(world.Tick(), MoveReceiptStatus.TargetBlocked);
            Check(world.TryPostMove(Move(session, 2, 72, 24)), "enqueue old seq");
            HasReceipt(world.Tick(), MoveReceiptStatus.ReplayedSequence);
            var actor = world.GetActor(14)!.Value;
            Check(actor.X == 24 && actor.Y == 24 && !actor.IsMoving &&
                  actor.LastMovementSequence == 2, "invalid commands changed position");
        });

        Checks.Run("same deterministic trace stays independent of presentation reads", () =>
        {
            var options = new WorldLimits(MovementPixelsPerTick: 8);
            var first = new ServerWorld([b], options);
            var second = new ServerWorld([b], options);
            Check(first.TryTrustedRegister(2, 81, 24, 24, out var s1), "world one register");
            Check(second.TryTrustedRegister(2, 81, 24, 24, out var s2), "world two register");
            Check(first.StateHash() == second.StateHash(), "initial deterministic hash");
            for (int step = 0; step < 40; step++)
            {
                if (step == 0 || step == 8 || step == 25)
                {
                    uint seq = step == 0 ? 1u : step == 8 ? 2u : 3u;
                    var target = step == 0 ? (104.0, 104.0) :
                                 step == 8 ? (104.0, 24.0) : (24.0, 104.0);
                    Check(first.TryPostMove(Move(s1, seq, target.Item1, target.Item2)) &&
                          second.TryPostMove(Move(s2, seq, target.Item1, target.Item2)),
                          "same trace post");
                }
                // Simulate distinct render sampling schedules. Neither mutates authority.
                for (int n = 0; n < (step % 4) * 5; n++) _ = first.GetActor(2);
                var left = first.Tick();
                var right = second.Tick();
                Check(left.Receipts.Select(r => r.Status).SequenceEqual(
                      right.Receipts.Select(r => r.Status)), "deterministic receipts");
                Check(first.StateHash() == second.StateHash(), "FPS-independent world hash");
                Check(left.Changed.Count <= 1 && right.Changed.Count <= 1,
                      "at most one entity update per tick");
            }
        });

        Checks.Run("invalid input preserves authorized move; accepted Stop sends revision delta", () =>
        {
            var world = new ServerWorld([b]);
            Check(world.TryTrustedRegister(140, 81, 24, 24, out var session), "register");
            Check(world.TryPostMove(Move(session, 1, 104, 24)), "first Move");
            HasReceipt(world.Tick(), MoveReceiptStatus.Accepted);
            Check(world.GetActor(140)!.Value.X == 32, "first fixed step");
            Check(world.TryPostMove(Move(session, 2, double.NaN, 24)), "invalid Move");
            HasReceipt(world.Tick(), MoveReceiptStatus.InvalidTarget);
            var stillMoving = world.GetActor(140)!.Value;
            Check(stillMoving.IsMoving && stillMoving.X == 40 &&
                  stillMoving.LastMovementSequence == 2 &&
                  stillMoving.MovementActionSequence == 1,
                  "invalid command re-labelled legitimate movement");
            Check(world.TryPostMove(Move(session, 2, 72, 24)), "old Move");
            HasReceipt(world.Tick(), MoveReceiptStatus.ReplayedSequence);
            var before = world.GetActor(140)!.Value;
            Check(before.X == 48 && before.IsMoving, "replayed Move interfered with path");
            Check(world.TryPostMove(Move(session, 3, before.X, before.Y)), "accepted Stop");
            var stopped = world.Tick();
            HasReceipt(stopped, MoveReceiptStatus.Accepted);
            Check(stopped.Changed.Count == 1 && !stopped.Changed[0].IsMoving &&
                  stopped.Changed[0].X == before.X &&
                  stopped.Changed[0].EntityRevision > before.EntityRevision &&
                  stopped.Changed[0].MovementActionSequence == 3,
                  "stationary Stop must produce a revision for remote observers");
        });

        Checks.Run("holding the same destination does not restart or re-budget the route", () =>
        {
            var world = new ServerWorld([b],
                new WorldLimits(MaxPathRequestsPerTick: 1, MovementPixelsPerTick: 8));
            Check(world.TryTrustedRegister(141, 81, 24, 24, out var actor), "register");
            Check(world.TryPostMove(Move(actor, 1, 104, 88)) &&
                  world.TryPostMove(Move(actor, 2, 103, 90)),
                  "repeated target commands accepted by queue");
            var tick = world.Tick();
            Check(tick.Receipts.Count == 2 &&
                  tick.Receipts.All(r => r.Status == MoveReceiptStatus.Accepted),
                  "same collision cell incorrectly repeated A* or consumed route budget");
            var moving = world.GetActor(141)!.Value;
            Check(moving.IsMoving && moving.MovementActionSequence == 1 &&
                  moving.LastMovementSequence == 2 &&
                  Math.Abs(Math.Sqrt(Math.Pow(moving.X - 24, 2) +
                                     Math.Pow(moving.Y - 24, 2)) - 8) < 0.0001,
                  "duplicate destination altered fixed-step movement or ACK sequencing");
        });

        Checks.Run("2D open-path steering uses collision-checked line of sight", () =>
        {
            var world = new ServerWorld([b], new WorldLimits(MovementPixelsPerTick: 8));
            Check(world.TryTrustedRegister(142, 81, 24, 24, out var actor), "register");
            Check(world.TryPostMove(Move(actor, 1, 104, 88)), "move intent");
            HasReceipt(world.Tick(), MoveReceiptStatus.Accepted);
            var first = world.GetActor(142)!.Value;
            Check(first.X > 24 && Math.Abs((first.Y - 24) / (first.X - 24) - 0.8) < 0.06,
                  "open-path movement followed staircase-cell diagonals, not visible endpoint");
            for (int n = 0; n < 30 && world.GetActor(142)!.Value.IsMoving; n++)
            {
                var before = world.GetActor(142)!.Value;
                world.Tick();
                var after = world.GetActor(142)!.Value;
                Check(b.IsDirectClear(before.X, before.Y, after.X, after.Y),
                      "lookahead travelled through blocked grid");
                Check(Math.Sqrt(Math.Pow(after.X - before.X, 2) +
                                     Math.Pow(after.Y - before.Y, 2)) <= 8.000001,
                      "lookahead exceeded authoritative pixels per tick");
            }
            var settled = world.GetActor(142)!.Value;
            Check(!settled.IsMoving && Math.Abs(settled.X - 104) < 0.000001 &&
                  Math.Abs(settled.Y - 88) < 0.000001, "smooth path missed target");
        });

        Checks.Run("8-way heading boundary does not rapidly flip facing during repath", () =>
        {
            var world = new ServerWorld([b], new WorldLimits(MovementPixelsPerTick: 8));
            Check(world.TryTrustedRegister(143, 81, 24, 24, out var actor), "register");
            // Alternating two legal targets makes the actual heading straddle
            // E/SE's 22.5-degree boundary without a meaningful turn.
            foreach (var (sequence, x, y) in new[] {
                (1u, 120.0, 56.0), (2u, 120.0, 72.0),
                (3u, 120.0, 56.0), (4u, 120.0, 72.0) })
            {
                Check(world.TryPostMove(Move(actor, sequence, x, y)), "enqueue repath");
                HasReceipt(world.Tick(), MoveReceiptStatus.Accepted);
                var at = world.GetActor(143)!.Value;
                Check(at.Facing == 6, "small heading fluctuation flipped E/SE sprite");
            }
            // A real sharp turn must not be delayed by the facing dead band.
            Check(world.TryPostMove(Move(actor, 5, 24, 104)), "sharp turn");
            HasReceipt(world.Tick(), MoveReceiptStatus.Accepted);
            Check(world.GetActor(143)!.Value.Facing == 1,
                  "hysteresis prevented deliberate SW turn");

            var upper = new ServerWorld([b], new WorldLimits(MovementPixelsPerTick: 8));
            Check(upper.TryTrustedRegister(144, 81, 24, 72, out var session), "negative heading register");
            Check(upper.TryPostMove(Move(session, 1, 120, 40)), "negative heading east");
            HasReceipt(upper.Tick(), MoveReceiptStatus.Accepted);
            Check(upper.GetActor(144)!.Value.Facing == 6, "negative heading east facing");
            Check(upper.TryPostMove(Move(session, 2, 120, 24)), "negative heading northeast");
            HasReceipt(upper.Tick(), MoveReceiptStatus.Accepted);
            Check(upper.GetActor(144)!.Value.Facing == 6,
                  "small negative heading fluctuation flipped E/NE sprite");
        });

        Checks.Run("A* detour proceeds cell-by-cell without tunnelling through solid wall", () =>
        {
            var world = new ServerWorld(both);
            Check(world.TryTrustedRegister(15, 80, 24, 24, out var session), "register");
            Check(world.TryPostMove(Move(session, 1, 88, 24)), "enqueue legitimate detour");
            HasReceipt(world.Tick(), MoveReceiptStatus.Accepted);
            bool usedDoor = false;
            var prev = world.GetActor(15)!.Value;
            bool reached = false;
            for (int n = 0; n < 200; n++)
            {
                world.Tick();
                var actor = world.GetActor(15)!.Value;
                Check(a.TryWorldCell(actor.X, actor.Y, out var cell) && a.IsWalkable(cell),
                      "actor entered wall");
                if (cell.X == 3 && cell.Y == 7) usedDoor = true;
                double dx = actor.X - prev.X, dy = actor.Y - prev.Y;
                Check(Math.Sqrt(dx * dx + dy * dy) <= 8.000001, "movement exceeded fixed step");
                if (dx != 0 || dy != 0)
                    Check(a.IsDirectClear(prev.X, prev.Y, actor.X, actor.Y), "movement crosses wall");
                prev = actor;
                if (!actor.IsMoving)
                {
                    reached = Math.Abs(actor.X - 88) < 1e-6 &&
                              Math.Abs(actor.Y - 24) < 1e-6;
                    break;
                }
            }
            Check(reached && usedDoor, "detour did not reach target through the only door");
        });

        Checks.Run("bounded A* expansion rejects impossible long route without advancing", () =>
        {
            var world = new ServerWorld(both, new WorldLimits(MaxPathExpandedCells: 1));
            Check(world.TryTrustedRegister(16, 80, 24, 24, out var session), "register");
            Check(world.TryPostMove(Move(session, 1, 88, 24)), "enqueue budget route");
            HasReceipt(world.Tick(), MoveReceiptStatus.PathBudgetExhausted);
            var actor = world.GetActor(16)!.Value;
            Check(!actor.IsMoving && actor.X == 24 && actor.Y == 24, "budget failure teleported actor");
        });

        Checks.Run("bounded route queries per Tick return explicit overload receipt", () =>
        {
            var limits = new WorldLimits(MaxCommandsPerTick: 3, MaxPathRequestsPerTick: 1);
            var world = new ServerWorld([b], limits);
            Check(world.TryTrustedRegister(17, 81, 24, 24, out var first), "register first");
            Check(world.TryTrustedRegister(18, 81, 24, 40, out var second), "register second");
            Check(world.TryPostMove(Move(first, 1, 72, 24)) &&
                  world.TryPostMove(Move(second, 1, 72, 40)), "post two");
            var response = world.Tick();
            Check(response.Receipts.Count == 2 &&
                  response.Receipts[0].Status == MoveReceiptStatus.Accepted &&
                  response.Receipts[1].Status == MoveReceiptStatus.TickRouteBudgetExhausted,
                  "route limit must return explicit failure, not silently queue forever");
            Check(world.GetActor(18)!.Value.X == 24 && !world.GetActor(18)!.Value.IsMoving,
                  "limited actor should remain still");
            Check(world.TryPostMove(Move(second, 2, 72, 40)), "client can re-issue new sequence");
            HasReceipt(world.Tick(), MoveReceiptStatus.Accepted);
        });

        Checks.Run("trusted cross-map transfer rejects old commands, disconnect and reuse", () =>
        {
            var world = new ServerWorld(both);
            Check(world.TryTrustedRegister(19, 80, 24, 24, out var first), "register");
            Check(world.TryPostMove(Move(first, 1, 88, 24)), "enqueue old map");
            Check(!world.TryTrustedTransfer(19, first.ConnectionEpoch, first.MapEpoch,
                    80, 56, 24, out _), "blocked destination accepted");
            Check(world.TryTrustedTransfer(19, first.ConnectionEpoch, first.MapEpoch,
                    81, 24, 24, out var moved), "trusted map change");
            Check(moved.ConnectionEpoch == first.ConnectionEpoch &&
                  moved.MapEpoch > first.MapEpoch && moved.LastMovementSequence == 0,
                  "map epoch must advance and movement be reset");
            HasReceipt(world.Tick(), MoveReceiptStatus.OldMapEpoch);
            Check(world.GetActor(19)!.Value.X == 24 && !world.GetActor(19)!.Value.IsMoving,
                  "old route leaked into new map");
            Check(!world.TryTrustedDisconnect(19, first.ConnectionEpoch, first.MapEpoch),
                  "old map disconnect killed new actor");
            Check(world.TryPostMove(Move(moved, 1, 72, 24)), "enqueue new map move");
            HasReceipt(world.Tick(), MoveReceiptStatus.Accepted);
            Check(world.TryTrustedDisconnect(19, moved.ConnectionEpoch, moved.MapEpoch),
                  "legitimate disconnect");
            Check(world.TryTrustedRegister(19, 80, 24, 24, out var rejoined),
                  "new authenticated session");
            Check(rejoined.ConnectionEpoch > moved.ConnectionEpoch &&
                  rejoined.MapEpoch > moved.MapEpoch, "old generations reused");
            Check(world.TryPostMove(Move(moved, 2, 72, 24)), "queued stale connection");
            HasReceipt(world.Tick(), MoveReceiptStatus.OldConnectionEpoch);
        });

        Checks.Run("concurrent producers cannot exceed bounded commands or mutate world owner", () =>
        {
            var world = new ServerWorld([b], new WorldLimits(
                MaxPendingCommands: 32, MaxCommandsPerTick: 8));
            Check(world.TryTrustedRegister(20, 81, 24, 24, out var session), "register");
            int accepted = 0;
            Parallel.For(0, 256, i =>
            {
                if (world.TryPostMove(Move(session, (uint)(i + 1), 72, 24)))
                    Interlocked.Increment(ref accepted);
            });
            Check(accepted == 32 && world.PendingCommands == 32, "MPSC cap violated");
            bool rejected = Task.Run(() =>
            {
                try { world.Tick(); return false; }
                catch (InvalidOperationException) { return true; }
            }).GetAwaiter().GetResult();
            Check(rejected, "world mutated from non-owner thread");
            int delivered = 0;
            for (int tick = 0; tick < 8; tick++)
            {
                var receipts = world.Tick().Receipts;
                Check(receipts.Count <= 8, "command/tick budget");
                delivered += receipts.Count;
            }
            Check(delivered == accepted && world.PendingCommands == 0, "MPSC draining completeness");
        });
    }

    public static void VerifyOriginal(string assetRoot)
    {
        Checks.Run("real Map13 bounded 20-synthetic-actor path burst with explicit refusals", () =>
        {
            var map13 = BlockingGrid.Load(Path.Combine(assetRoot, "maps", "13"), 13);
            var world = new ServerWorld([map13], new WorldLimits(
                MaxActors: 20, MaxKnownActorIds: 20,
                MaxPendingCommands: 32, MaxCommandsPerTick: 20,
                MaxPathRequestsPerTick: 6, MaxPathExpandedCells: 128));
            var starts = new List<ActorSnapshot>();
            for (int i = 0; i < 20; i++)
            {
                Check(world.TryTrustedRegister(2000 + i, 13, 2560, 2880, out var actor),
                      "synthetic player registration");
                starts.Add(actor);
            }
            var origin = new GridCell(2560 / 16, 2880 / 16);
            GridCell? next = null;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var cell = new GridCell(origin.X + dx, origin.Y + dy);
                if (map13.IsWalkable(cell)) { next = cell; break; }
            }
            Check(next.HasValue, "Map13 original portal walkable neighbor");
            var dest = Checks.Center(next.GetValueOrDefault());
            foreach (var actor in starts)
                Check(world.TryPostMove(Move(actor, 1, dest.X, dest.Y)), "bounded burst post");
            var first = world.Tick();
            Check(first.Receipts.Count == 20 &&
                  first.Receipts.Count(r => r.Status == MoveReceiptStatus.Accepted) == 6 &&
                  first.Receipts.Count(r => r.Status == MoveReceiptStatus.TickRouteBudgetExhausted) == 14 &&
                  world.PendingCommands == 0, "20-command fan-in must respect 6-route tick budget");
            for (int step = 0; step < 30; step++)
            {
                var tick = world.Tick();
                Check(tick.Receipts.Count == 0 && tick.Changed.Count <= 20,
                      "manual Tick output bounded");
            }
            Check(world.ActorCount == 20, "actor count changed");
            int atTarget = 0;
            foreach (var actor in starts)
            {
                var current = world.GetActor(actor.ActorId)!.Value;
                Check(map13.TryWorldCell(current.X, current.Y, out var cell) &&
                      map13.IsWalkable(cell), "original-map synthetic actor through wall");
                if (current.X == dest.X && current.Y == dest.Y) atTarget++;
            }
            Check(atTarget == 6, "only budget-admitted actors may move");
        });

        Checks.Run("real Map13/Map15 authoritative epoch, movement and rejection trace", () =>
        {
            var source13 = BlockingGrid.Load(Path.Combine(assetRoot, "maps", "13"), 13);
            var source15 = BlockingGrid.Load(Path.Combine(assetRoot, "maps", "15"), 15);
            var world = new ServerWorld([source13, source15]);
            Check(world.TryTrustedRegister(1001, 13, 2560, 2880, out var player),
                  "Map13 original destination spawn");
            Check(world.TryTrustedRegister(1002, 15, 7872, 11296, out var city),
                  "Map15 original destination spawn");
            bool posted = false;
            foreach (var (dx, dy) in new[] { (16, 0), (-16, 0), (0, 16), (0, -16) })
            {
                if (source13.TryWorldCell(player.X + dx, player.Y + dy, out var cell) &&
                    source13.IsWalkable(cell))
                {
                    Check(world.TryPostMove(Move(player, 1, player.X + dx + 8,
                                                  player.Y + dy + 8)), "original move queued");
                    posted = true;
                    break;
                }
            }
            Check(posted, "original Map13 portal has no nearby walkable destination");
            HasReceipt(world.Tick(), MoveReceiptStatus.Accepted);
            for (int i = 0; i < 8; i++) world.Tick();
            var moved = world.GetActor(1001)!.Value;
            Check(source13.TryWorldCell(moved.X, moved.Y, out var c) &&
                  source13.IsWalkable(c), "Map13 actor entered invalid cell");
            Check(world.TryPostMove(Move(player, 2, double.NaN, 0)),
                  "invalid original map command queued");
            HasReceipt(world.Tick(), MoveReceiptStatus.InvalidTarget);
            Check(world.TryTrustedTransfer(1001, player.ConnectionEpoch, player.MapEpoch,
                15, 7872, 11296, out var transferred), "trusted Map13->Map15 relocation");
            Check(transferred.MapId == 15 && transferred.MapEpoch > player.MapEpoch &&
                  world.GetActor(1002)!.Value.MapId == 15, "two active actors at Map15");
            Check(world.TryPostMove(Move(player, 3, 2560, 2880)), "old Map13 command queued");
            HasReceipt(world.Tick(), MoveReceiptStatus.OldMapEpoch);
            Check(world.TryTrustedTransfer(1001, transferred.ConnectionEpoch, transferred.MapEpoch,
                13, 2560, 2880, out var returnee), "trusted Map15->Map13 relocation");
            Check(returnee.MapId == 13 && returnee.MapEpoch > transferred.MapEpoch,
                  "second transfer epoch");
            // Merely exercises trusted world lifecycle, not game portal authorization,
            // client AOI/WorldReady or a networked map transition.
            Check(world.ActorCount == 2 && world.PendingCommands == 0,
                  "two-map reducer lifecycle leak");
        });
    }
}
