using SGTest.Server.World;

internal static class FacingChecks
{
    public static void Verify(string root)
    {
        Checks.Run("World legal eight-way movement sets facing; stop, rejection and transfer preserve it", () =>
        {
            var grid = BlockingGrid.Load(Checks.Fixture(root, 115, 16, 16,
                (x,y) => x == 15 ? (byte)7 : (byte)0), 115);
            var second = BlockingGrid.Load(Checks.Fixture(root, 116, 16, 16, (_,_) => 0), 116);
            int[] dx = [1,1,0,-1,-1,-1,0,1];
            int[] dy = [0,1,1,1,0,-1,-1,-1];
            byte[] facings = [6,7,8,1,2,3,4,5];
            for (int i=0;i<8;i++)
            {
                var world = new ServerWorld([grid,second]);
                Checks.Require(world.TryTrustedRegister(1,115,88,88,out var actor), "facing register");
                world.Tick();
                Checks.Require(world.TryPostMove(new(1,actor.ConnectionEpoch,actor.MapEpoch,1,
                    88+dx[i]*32,88+dy[i]*32)), "facing move");
                bool sawMotion=false;
                for (int tick=0;tick<12;tick++)
                {
                    var result=world.Tick();
                    foreach (var changed in result.Changed)
                    {
                        sawMotion=true;
                        Checks.Require(changed.Facing==facings[i], $"eight-way facing {i}");
                    }
                }
                var stopped=world.GetActor(1)!.Value;
                Checks.Require(sawMotion && !stopped.IsMoving && stopped.Facing==facings[i], "stop facing");
                var idle=world.Tick();
                Checks.Require(idle.Changed.Count==0 && world.GetActor(1)!.Value.Facing==facings[i], "idle facing changed");
                world.TryPostMove(new(1,actor.ConnectionEpoch,actor.MapEpoch,2,248,88)); // blocked column
                var refused=world.Tick();
                Checks.Require(refused.Receipts.Single().Status==MoveReceiptStatus.TargetBlocked &&
                    world.GetActor(1)!.Value.Facing==facings[i], "rejected target rotated actor");
                world.TryPostMove(new(1,actor.ConnectionEpoch,actor.MapEpoch,3,(int)stopped.X,(int)stopped.Y));
                world.Tick();
                Checks.Require(world.GetActor(1)!.Value.Facing==facings[i], "same-cell stop changed facing");
                world.TryPostMove(new(1,actor.ConnectionEpoch,actor.MapEpoch,4,(int)stopped.X,(int)stopped.Y-16));
                world.Tick(); world.Tick(); world.Tick();
                Checks.Require(world.GetActor(1)!.Value.Facing==4 && !world.GetActor(1)!.Value.IsMoving,
                    "turn did not use next traversed segment");
                Checks.Require(world.TryTrustedTransfer(1,actor.ConnectionEpoch,actor.MapEpoch,116,24,24,out var transferred) &&
                    transferred.Facing==4 && !transferred.IsMoving, "transfer forgot facing");
            }
        });
    }
}
