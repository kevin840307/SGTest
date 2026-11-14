using System.Numerics;
using System.Text.Json;
using SGTest.Server.Application;
using SGTest.Server.Domain;
using SGTest.Server.Infrastructure;
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
Check(WorldHost.InInterest(Vector2.Zero,new(1099,0),false),"AOI enter covers maximum viewport edge");
Check(!WorldHost.InInterest(Vector2.Zero,new(1101,0),false),"AOI enter remains bounded");
Check(WorldHost.InInterest(Vector2.Zero,new(1200,0),true),"AOI exit hysteresis prevents edge churn");
Check(!WorldHost.InInterest(Vector2.Zero,new(1251,0),true),"AOI exit remains bounded");

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


var population=new Population(new Dictionary<uint,MonsterDefinition>{
    [1]=new(1,45,7,80,600,75,42,.8,3),
    [2]=MonsterDefinition.Default(2)
},[new(1,new(900,800)),new(2,new(1500,800))]);
var original=new World(); original.Populate(-1,0,population);
Check(original.Monsters.Count==2 && original.Monsters[1].Definition==1 && original.Monsters[2].Position==new Vector2(1500,800),"source population preserves identity and position");
Check(original.Monsters[1].Hp==45 && original.Monsters[1].MaxHp==45 && original.Monsters[1].Damage==7 &&
      original.Monsters[1].MoveSpeed==80 && original.Monsters[1].AggroRange==600 &&
      original.Monsters[1].AttackRange==75 && original.Monsters[1].ChaseStopRange==42 &&
      original.Monsters[1].AttackIntervalSeconds==.8 && original.Monsters[1].RespawnSeconds==3,
      "population v2 definition stats configure monster runtime");
original.Monsters[1].Hp=0; original.Monsters[1].Respawn=0; original.Monsters[1].Position=Vector2.Zero; original.Step(.3);
Check(original.Monsters[1].Position==new Vector2(900,800) && original.Monsters[1].Definition==1 && original.Monsters[1].Hp==45,
      "respawn keeps definition/home and restores definition max hp");
original.Populate(5,0,population);
Check(original.Monsters[5].Definition==1 && original.Monsters[5].MaxHp==45 &&
      original.Monsters[4].Definition==2 && original.Monsters[4].MaxHp==30,
      "stress cycles original definition palette and stats");
if(File.Exists("server/content/maps/13/population.json"))
{
    var actualPopulation=Population.Load("server/content/maps/13/population.json",13);
    Check(actualPopulation.Spawns.Length==78 && actualPopulation.Definitions.Count==9 &&
          actualPopulation.Definitions.Values.All(d=>d.Hp==30 && d.MoveSpeed==65),
          "original Map13 population v2 loads with explicit legacy-equivalent stats");
}
string legacyPopulation=Path.GetTempFileName();
File.WriteAllText(legacyPopulation,"""{"version":1,"map_id":13,"definitions":[{"id":1}],"spawns":[{"definition":1,"p":[100,100]}]}""");
var legacyLoaded=Population.Load(legacyPopulation,13);
Check(legacyLoaded.Definitions[1].Hp==30 && legacyLoaded.Definitions[1].MoveSpeed==65,
      "population v1 remains backward compatible with default stats");
File.WriteAllText(legacyPopulation,"""{"version":2,"map_id":13,"definitions":[{"id":1,"stats":{"hp":0,"damage":1,"move_speed":65,"aggro_range":500,"attack_range":70,"chase_stop_range":45,"attack_interval_seconds":1,"respawn_seconds":2}}],"spawns":[{"definition":1,"p":[100,100]}]}""");
bool badDefinition=false; try { Population.Load(legacyPopulation,13); } catch(InvalidDataException) { badDefinition=true; }
Check(badDefinition,"population v2 rejects invalid definition stats");
File.Delete(legacyPopulation);
var identityStore=new MemoryAccountStore();
var identities=new AccountService(identityStore,1000);
var registered=identities.Register("Kevin_01","password-123");
Check(registered.Success && registered.AccountId>0,"account registration");
Check(!identities.Register("kevin_01","password-456").Success,"account username is case-insensitive unique");
Check(!identities.Login("Kevin_01","wrong-pass").Success,"wrong password rejected");
var authenticated=identities.Login("KEVIN_01","password-123");
Check(authenticated.Success && authenticated.AccountId==registered.AccountId,"PBKDF2 account login");
Check(!identities.Register("x","password-123").Success && !identities.Register("valid_user","short").Success,
      "account credential validation");
var firstCharacter=identities.CreateCharacter(authenticated.AccountId,"星夢01");
Check(firstCharacter.Success && firstCharacter.CharacterId>0,"unicode character creation");
Check(!identities.CreateCharacter(authenticated.AccountId,"星夢01").Success,"duplicate character name rejected");
var secondAccount=identities.Register("Second_1","password-456");
Check(secondAccount.Success && !identities.CreateCharacter(secondAccount.AccountId,"星夢01").Success,
      "character names are globally unique");
foreach(string name in new[]{"Alpha2","Alpha3","Alpha4"})
    Check(identities.CreateCharacter(authenticated.AccountId,name).Success,"up to four characters allowed");
Check(!identities.CreateCharacter(authenticated.AccountId,"Alpha5").Success,"fifth character rejected");
Check(identities.Characters(authenticated.AccountId).Length==4 &&
      identities.Character(authenticated.AccountId,firstCharacter.CharacterId)?.Name=="星夢01",
      "character list and ownership lookup");
var raceIdentities=new AccountService(new MemoryAccountStore(),1000);
var registerRace=Enumerable.Range(0,8).AsParallel()
    .Select(_=>raceIdentities.Register("Race_User","password-789")).ToArray();
Check(registerRace.Count(r=>r.Success)==1,"concurrent duplicate registration creates exactly one account");
var raceAccount=registerRace.Single(r=>r.Success).AccountId;
var characterRace=Enumerable.Range(0,8).AsParallel()
    .Select(_=>raceIdentities.CreateCharacter(raceAccount,"RaceHero")).ToArray();
Check(characterRace.Count(r=>r.Success)==1,"concurrent duplicate character creation creates exactly one character");

string sqliteFolder=Path.Combine(Path.GetTempPath(),"sgtest-sqlite-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sqliteFolder);
string sqlitePath=Path.Combine(sqliteFolder,"identity.db");
try
{
    var sqliteStore=new SqliteAccountStore(sqlitePath);
    Check(sqliteStore.JournalMode().Equals("wal",StringComparison.OrdinalIgnoreCase),"SQLite account store uses WAL");
    Check(sqliteStore.ForeignKeysEnabled(),"SQLite account store enables foreign keys");
    var sqliteIdentities=new AccountService(sqliteStore,1000);
    var sqliteAccount=sqliteIdentities.Register("Persist_1","password-abc");
    Check(sqliteAccount.Success,"SQLite account registration");
    var sqliteCharacter=sqliteIdentities.CreateCharacter(sqliteAccount.AccountId,"持久角");
    Check(sqliteCharacter.Success,"SQLite character creation");
    var reopened=new AccountService(new SqliteAccountStore(sqlitePath),1000);
    var relogin=reopened.Login("persist_1","password-abc");
    Check(relogin.Success && relogin.AccountId==sqliteAccount.AccountId,"SQLite account survives provider reopen");
    var reopenedCharacters=reopened.Characters(relogin.AccountId);
    Check(reopenedCharacters.Length==1 && reopenedCharacters[0].Id==sqliteCharacter.CharacterId,
          "SQLite character survives provider reopen");
    long persistedCharacter=reopenedCharacters.Single().Id;
    Check(reopened.Character(relogin.AccountId,persistedCharacter)?.Name=="持久角",
          "SQLite character ownership survives provider reopen");
    var sqliteRace=Enumerable.Range(0,8).AsParallel()
        .Select(_=>reopened.CreateCharacter(relogin.AccountId,"同名角")).ToArray();
    Check(sqliteRace.Count(r=>r.Success)==1,"SQLite transaction allows one concurrent duplicate character");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(sqliteFolder,true);
}

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
var snapshot=SnapshotCodec.Encode([new(42,new Monster{Definition=99,Position=new(1.25f,-2.5f),Revision=uint.MaxValue})]);
File.WriteAllText("reports/wire_fixture.json",JsonSerializer.Serialize(new { hex=Convert.ToHexString(packet).ToLowerInvariant(),snapshot_hex=Convert.ToHexString(snapshot).ToLowerInvariant() }));
if(File.Exists("server/content/maps/13/blocking.bin"))
{
    var actual=CollisionGrid.Load("server/content/maps/13/blocking.bin");
    Check(actual.Width==960 && actual.Height==960 && actual.Walkable(new(1200,800)),"actual Map13 collision");
}
else Console.WriteLine("Original Map13 collision: NOT VERIFIED (private content absent)");
Console.WriteLine($"C# server checks: {count} passed");
