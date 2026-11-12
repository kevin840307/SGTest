using SGTest.Domain;

int checks = 0;
void Check(bool condition, string description)
{
    checks++;
    if (!condition)
        throw new Exception($"FAIL #{checks}: {description}");
}

var world = new WorldSimulation();
Check(world.TryJoin(101, "Alice", out var spawn) && spawn is not null, "first join");
Check(world.TryJoin(102, "Bob", out _) && world.PlayerCount == 2, "shared world join");
Check(world.Snapshot().Count == 2, "two spawn snapshots");
Check(world.TryGetPosition(101, out var initial), "initial position");
var target = new Point2(initial.X + 180, initial.Y);
Check(world.TryMove(101, target, out var move) && move is not null, "move");
Check(move!.Type == "move" && move.Facing == (int)Facing.East, "facing east");
Check(move.DurationTicks == 30, "180px at 180px/s takes 30 ticks");
for (int i = 0; i < 15; i++)
    Check(world.Step().Count == 0, "no premature stop");
Check(world.TryGetPosition(101, out var half) &&
      Math.Abs(half.X - (initial.X + 90)) < 0.001, "server position advances");
Check(world.Snapshot().Count == 3, "joining observer receives ongoing segment");
Check(world.TryStop(101, out var stop) && stop is not null, "explicit stop");
Check(stop!.MovementSeq > move.MovementSeq, "stop sequence barrier");
Check(world.TryGetPosition(101, out var stopped), "stopped position");
for (int i = 0; i < 50; i++)
    Check(world.Step().Count == 0, "no unwanted movement after stop");
Check(world.TryGetPosition(101, out var still) && still == stopped, "position stays");
Check(!world.TryMove(101, new Point2(double.NaN, 80), out _),
    "reject NaN destination");
Check(!world.TryMove(101, new Point2(1e9, 80), out _),
    "reject out of map");
Check(world.TryMove(101, target, out var moveAgain) && moveAgain is not null,
    "second movement");
bool completed = false;
for (int i = 0; i < 50; i++)
{
    var events = world.Step();
    if (events.Count > 0)
    {
        Check(events.Count == 1 && events[0].Type == "stop", "arrival event");
        completed = true;
        break;
    }
}
Check(completed, "movement completed");
Check(world.TryGetPosition(101, out var reached) && reached == target, "final exact position");
Check(world.TryLeave(101, out var left) && left is not null &&
      left.EntityRevision > 1 && world.PlayerCount == 1, "despawn revision");
Check(!world.TryGetPosition(101, out _), "no ghost player");
Check(FacingMath.FromDelta(new Point2(0, -1), Facing.East) == Facing.North,
    "eight-direction north");
Check(FacingMath.FromDelta(new Point2(0, 0), Facing.West) == Facing.West,
    "stationary keeps facing");
Console.WriteLine($"SGTest.Domain.Specs PASS: {checks} checks (30Hz movement, stop, snapshot, direction, bounds, leave)");
