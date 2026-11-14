using System.Numerics;
using System.Text.Json;
using SGTest.Server.Domain;
using SGTest.Server.Transport;

int count=0;
void Check(bool ok,string why) { count++; if(!ok) throw new Exception(why); }
var world=new World(); var p=world.Join(1); var start=p.Position;
for(uint i=1;i<=120;i++)
{
    Check(world.QueueInput(1,1,i,Vector2.UnitX),"ordered input accepted");
    Check(!world.QueueInput(1,1,i,Vector2.UnitX),"duplicate input rejected");
    world.Step(1.0/30);
}
Check(Math.Abs(p.Position.X-start.X-920)<.01,"authoritative speed unchanged");
Check(!world.QueueInput(1,0,121,Vector2.UnitX),"old life rejected");
Check(!world.QueueInput(1,1,100000,Vector2.UnitX),"future input bounded");
Check(!world.QueueInput(1,1,121,new(float.NaN,0)),"nonfinite input rejected");
Check(!world.QueueInput(1,1,121,new(9,0)),"oversized axis rejected");
for(int i=0;i<20;i++) world.Step(1.0/30);
var stopped=p.Position; world.Step(.1); Check(stopped==p.Position,"input timeout stops player");
Check(World.Newer(0,uint.MaxValue) && !World.Newer(uint.MaxValue,0),"uint32 wrap ordering");
int[] headings=[6,7,8,1,2,3,4,5];
for(int i=0;i<8;i++) Check(World.Heading(new Vector2(MathF.Cos(i*MathF.PI/4),MathF.Sin(i*MathF.PI/4)))==headings[i],"eight facing mapping");

var grid=new CollisionGrid(3,3,[0,1,0,0,1,0,0,1,0]);
Check(!grid.Allows(new(8,24),new(40,24)),"no wall tunneling");
Check(grid.Slide(new(8,24),new(40,32)).X==8,"wall slide");
Check(!grid.Walkable(new(float.PositiveInfinity,0)),"invalid position blocked");
Check(!new CollisionGrid(3,3,[0,1,0,1,0,0,0,0,0]).Allows(new(8,8),new(24,24)),"no diagonal corner cut");
var blocked=new World(grid); blocked.Populate(1,0); var blockedPlayer=blocked.Join(1);
blockedPlayer.Position=new(8,24); blockedPlayer.ProtectedUntil=0; blocked.Monsters[1].Position=new(40,24);
blocked.Drops[1]=new(new(40,24),1001);
Check(!blocked.Attack(1,1),"attack cannot cross wall");
Check(!blocked.Pickup(1,1),"pickup cannot cross wall");
blocked.Step(.3); blocked.Step(.3); Check(blockedPlayer.Hp==100,"monster cannot hit through wall");

var combat=new World(); combat.Populate(1,0); var player=combat.Join(1); var monster=combat.Monsters[1];
monster.Position=player.Position+new Vector2(50,0); monster.Damage=50;
combat.Step(.2); Check(player.Hp==100,"spawn protection");
combat.Step(2); combat.Step(.2); Check(player.Hp==100,"windup does not immediately hit");
combat.Step(.21); Check(player.Hp==50,"windup resolved by server");
uint revision=player.Revision; combat.Step(1); combat.Step(.21);
Check(player.Hp==0 && player.Pose==4 && player.Revision>revision,"death revision and pose");
Check(!combat.QueueInput(1,1,1,Vector2.UnitX),"dead player cannot move");
Check(!combat.Attack(1,1),"dead player cannot attack");
player.Inventory[1001]=4; uint epoch=player.LifeEpoch; combat.Step(3.1);
Check(player.Hp==100 && player.LifeEpoch==epoch+1 && player.Inventory[1001]==4,"respawn retains inventory and changes epoch");

var loot=new World(); loot.Populate(1,0); var looter=loot.Join(1); var enemy=loot.Monsters[1]; enemy.Position=looter.Position+new Vector2(100,0);
for(int i=0;i<3;i++) { Check(loot.Attack(1,1),"attack accepted"); loot.Step(.36); }
Check(enemy.Hp==0 && loot.Drops.Count==3,"monster death generates three drops");
int drop=loot.Drops.Keys.First(); looter.Position=loot.Drops[drop].Position;
Check(loot.Pickup(1,drop) && !loot.Pickup(1,drop),"atomic pickup once only");
Check(looter.Inventory.Values.Sum()==1,"inventory increment once");

var npcs=new World(); npcs.Populate(2,0); npcs.Join(1);
npcs.Monsters[1].Position=new(1000,800); npcs.Monsters[2].Position=new(1400,800);
for(int i=0;i<6;i++) npcs.Step(1.0/30);
for(int i=0;i<30;i++)
{
    var a=npcs.Monsters[1].Position; var b=npcs.Monsters[2].Position; npcs.Step(1.0/30);
    Check(Math.Abs(Vector2.Distance(a,npcs.Monsters[1].Position)-65f/30)<.001,"continuous 30Hz NPC movement");
    Check(npcs.Monsters[1].Position.X>a.X && npcs.Monsters[2].Position.X<b.X,"independent opposite directions");
}
Check(npcs.Monsters[1].ActionTime!=npcs.Monsters[2].ActionTime,"independent animation phase");

object?[] fixture=[4294967295L,-1,1.25,"方向",new byte[]{1,254},new object?[]{0,true,null}];
byte[] packet=Wire.Packet(99,fixture); var decoded=Wire.Decode(packet);
Check(Wire.Integer(decoded[1])==99,"MessagePack envelope");
Check(Wire.Integer(((object?[])decoded[2]!)[0])==4294967295L,"uint32 identity survives wire");
foreach(var bad in new[]{packet[..^1],new byte[]{0xdd,0xff,0xff,0xff,0xff},new byte[70000]})
{
    bool rejected=false; try { Wire.Decode(bad); } catch(Exception e) when(e is InvalidDataException or OverflowException) { rejected=true; }
    Check(rejected,"malformed/truncated/oversized wire rejected");
}
Directory.CreateDirectory("reports");
File.WriteAllText("reports/wire_fixture.json",JsonSerializer.Serialize(new { hex=Convert.ToHexString(packet).ToLowerInvariant() }));
if(File.Exists("server/content/maps/13/blocking.bin"))
{
    var actual=CollisionGrid.Load("server/content/maps/13/blocking.bin");
    Check(actual.Width==960 && actual.Height==960 && actual.Walkable(new(1200,800)),"actual Map13 collision");
}
else Console.WriteLine("Original Map13 collision: NOT VERIFIED (private content absent)");
Console.WriteLine($"C# server checks: {count} passed");
