using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class SkillBookChecks
{
    private const string Golden =
        "21000000010068000B0000000000000000000000010200C56D000003211C000001";

    public static void Verify(string root)
    {
        Checks.Run("skill book V1 wire is bounded Server-only SkillID plus level",Wire);
        Checks.Run("skill book SQLite v23 persists original SkillID and Server-owned level",
            ()=>Persistence(root).GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var expected=new SkillBookSnapshotWire(11,0,true,[
            new SkillBookEntryWire(28101,3),
            new SkillBookEntryWire(7201,1)
        ]);
        byte[] frame=WireProtocol.EncodeSkillBookSnapshot(expected);
        Checks.Require(Convert.ToHexString(frame)==Golden&&
            WireProtocol.TryDecodeSkillBookSnapshot(frame,out var decoded)&&
            decoded is not null&&decoded.CharacterId==11&&decoded.Complete&&
            decoded.Skills.SequenceEqual(expected.Skills),
            "skill book cross-language golden mismatch");
        var empty=WireProtocol.EncodeSkillBookSnapshot(
            new(11,0,true,Array.Empty<SkillBookEntryWire>()));
        Checks.Require(empty.Length==23&&
            WireProtocol.TryDecodeSkillBookSnapshot(empty,out var emptyBack)&&
            emptyBack is not null&&emptyBack.Complete&&emptyBack.Skills.Count==0,
            "empty skill book final baseline rejected");

        byte[] bad=(byte[])frame.Clone();
        bad[20]=2;
        Checks.Require(!WireProtocol.TryDecodeSkillBookSnapshot(bad,out _),
            "non-boolean skill book finality accepted");
        bad=(byte[])frame.Clone();
        bad[27]=0;
        Checks.Require(!WireProtocol.TryDecodeSkillBookSnapshot(bad,out _),
            "zero learned skill level accepted");
        bad=(byte[])frame.Clone();
        frame.AsSpan(23,4).CopyTo(bad.AsSpan(28,4));
        Checks.Require(!WireProtocol.TryDecodeSkillBookSnapshot(bad,out _),
            "duplicate learned SkillID accepted");
    }

    private static async Task Persistence(string root)
    {
        string dir=Path.Combine(root,"skill-book-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long character;
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "skill_book","Skill-Book-Password-123!")).AccountId;
            character=(await store.CreateCharacterAsync(
                account,0,"SkillBookHero",new(15,8,8))).Character!.CharacterId;
            await store.SetCharacterSkillLevelAsync(
                character,28101,3,"old:SKC28J101");
            await store.SetCharacterSkillLevelAsync(
                character,7201,1,"old:Learn_Skill_common_203");
            await store.SetCharacterSkillLevelAsync(
                character,28101,4,"old:SKC28J101");
            var rows=await store.ListCharacterSkillsAsync(character);
            Checks.Require(rows.Count==2&&
                rows[0].SkillId==7201&&rows[0].Level==1&&
                rows[1].SkillId==28101&&rows[1].Level==4,
                "skill book upsert/order did not preserve original SkillID and level");
        }
        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var rows=await reopened.ListCharacterSkillsAsync(character);
            Checks.Require(rows.Count==2&&rows.Single(x=>x.SkillId==28101).Level==4,
                "skill book level did not survive SQLite reopen");
            bool unknown=false;
            try
            {
                await reopened.SetCharacterSkillLevelAsync(
                    character+99999,28101,1,"test:unknown");
            }
            catch(KeyNotFoundException){unknown=true;}
            Checks.Require(unknown,"unknown character acquired a learned skill");
        }
        bool invalid=false;
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            try{await store.SetCharacterSkillLevelAsync(
                character,28101,0,"test:invalid");}
            catch(ArgumentOutOfRangeException){invalid=true;}
        }
        Checks.Require(invalid,"invalid learned skill level accepted");
    }
}
