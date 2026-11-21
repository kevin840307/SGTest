using SGTest.Server.Visibility;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class PvpCombatVisualChecks
{
    public static void Verify(string root)
    {
        Checks.Run("PvP combat visual compact wire is presentation-only",
            Wire);
        Checks.Run("PvP combat visual reaches only observers seeing both players",
            Aoi);
    }

    private static void Wire()
    {
        var visual=new PvpCombatVisualWire(
            3,44,88,5,7,99,6,2,28,false,3,6);
        byte[] frame=WireProtocol.EncodePvpCombatVisual(visual);
        Checks.Require(frame.Length==WireProtocol.PvpCombatVisualFrameBytes&&
            WireProtocol.TryDecodePvpCombatVisual(frame,out var decoded)&&
            decoded==visual,
            "PvP combat visual wire roundtrip mismatch");
        var malformed=(byte[])frame.Clone();
        malformed[69]=0;
        Checks.Require(!WireProtocol.TryDecodePvpCombatVisual(
                malformed,out _),
            "invalid PvP attacker facing accepted");
    }

    private static void Aoi()
    {
        var attacker=Actor(301,7,4,8,8,3);
        var victim=Actor(302,8,9,24,8,6);
        var observer=Actor(303,9,4,40,8,1);
        var far=Actor(304,10,4,4000,4000,1);
        var aoi=new WorldAoi();
        aoi.Apply(new WorldChangedBatch(50,Array.Empty<ActorSnapshot>(),
            [new(ActorLifecycleKind.Joined,attacker),
             new(ActorLifecycleKind.Joined,victim),
             new(ActorLifecycleKind.Joined,observer),
             new(ActorLifecycleKind.Joined,far)]));
        Drain(aoi,attacker); Drain(aoi,victim); Drain(aoi,observer); Drain(aoi,far);

        var settlement=new PlayerDamageSettlement(
            victim.ActorId,victim.ConnectionEpoch,victim.MapEpoch,
            2,49,0,0,28,72,100,false,
            PlayerDamageSourceKind.PlayerPvp,
            attacker.ActorId,attacker.ConnectionEpoch,7,false,
            attacker.Facing,victim.Facing,
            AttackerMapEpoch:attacker.MapEpoch);
        int queued=aoi.EnqueuePlayerPvpCombatVisual(settlement);
        Checks.Require(queued==3,
            "PvP visual did not fan out to attacker victim and nearby observer");
        foreach(var receiver in new[]{attacker,victim,observer})
        {
            Checks.Require(aoi.TryDequeue(
                    receiver.ActorId,receiver.ConnectionEpoch,receiver.MapEpoch,
                    out var evt)&&evt.Kind==AoiEventKind.PvpCombatVisual&&
                evt.PvpCombat is { } visual&&
                visual.AttackerActorId==attacker.ActorId&&
                visual.VictimActorId==victim.ActorId&&
                visual.AttackSequence==7&&visual.DamageSequence==2&&
                visual.Damage==28&&!visual.Died&&
                visual.AttackerFacing==3&&visual.VictimFacing==6,
                "eligible observer PvP visual mismatch");
        }
        Checks.Require(!aoi.TryDequeue(
                far.ActorId,far.ConnectionEpoch,far.MapEpoch,out _),
            "far observer received PvP combat visual");
        Checks.Require(aoi.EnqueuePlayerPvpCombatVisual(
                settlement with {AttackerMapEpoch=attacker.MapEpoch+1})==0,
            "stale attacker map generation emitted PvP combat visual");
        Checks.Require(aoi.EnqueuePlayerPvpCombatVisual(
                settlement with {MapEpoch=victim.MapEpoch+1})==0,
            "stale victim map generation emitted PvP combat visual");
    }

    private static void Drain(WorldAoi aoi,ActorSnapshot actor)
    {
        while(aoi.TryDequeue(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,out _)){}
    }

    private static ActorSnapshot Actor(
        long id,long connection,long mapEpoch,double x,double y,byte facing)=>
        new(id,13,connection,mapEpoch,0,0,x,y,1,false,facing,false);
}
