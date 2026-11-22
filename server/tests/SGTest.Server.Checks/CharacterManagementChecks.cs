using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class CharacterManagementChecks
{
    public static void Verify()
    {
        Checks.Run("character manage wire binds owned id action and redacts passwords",Wire);
        Checks.Run("secondary password rotation and deletion require account-owned proof",
            ()=>Persistence().GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var change=new CharacterManageWire(CharacterManageAction.ChangePassword,97,"1234","5678");
        var frame=WireProtocol.EncodeCharacterManage(change);
        Checks.Require(frame.Length==27 && frame[8]==1 && frame[17]==4 && frame[18]==4,
            "character manage wire shape mismatch");
        Checks.Require(WireProtocol.TryDecodeCharacterManage(frame,out var decoded)
            && decoded==change,"character manage wire roundtrip mismatch");
        Checks.Require(!decoded.ToString().Contains("1234") &&
            !decoded.ToString().Contains("5678"),"secondary credentials leaked through DTO");
        var delete=new CharacterManageWire(CharacterManageAction.Delete,97,"5678",null);
        var deletion=WireProtocol.EncodeCharacterManage(delete);
        Checks.Require(WireProtocol.TryDecodeCharacterManage(deletion,out var parsed) && parsed==delete,
            "character deletion wire roundtrip failed");
        deletion[17]=0;
        Checks.Require(!WireProtocol.TryDecodeCharacterManage(deletion,out _),
            "deletion accepted absent second password");
        var result=new CharacterManageResultWire(CharacterManageAction.Delete,CharacterManageStatus.HasHistory,97);
        Checks.Require(WireProtocol.TryDecodeCharacterManageResult(
            WireProtocol.EncodeCharacterManageResult(result),out var returned) &&
            returned==result,"character manage result roundtrip mismatch");
    }

    private static async Task Persistence()
    {
        string root=Path.Combine(Path.GetTempPath(),"sgtest_char_manage_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long first=(await store.CreateAccountAsync("char_manage_a","A-Secret-Pass-2026!")).AccountId;
            long second=(await store.CreateAccountAsync("char_manage_b","B-Secret-Pass-2026!")).AccountId;
            var settings=new CharacterCreationSettings(new(2,2,2,2),"1234","tests/char-manage");
            long protectedId=(await store.CreateCharacterWithSettingsAsync(
                first,0,"Protected",new(15,100,100),settings)).Character!.CharacterId;
            long legacyId=(await store.CreateCharacterAsync(first,1,"Legacy",new(15,100,100))).Character!.CharacterId;
            Checks.Require(await store.ChangeCharacterPasswordAsync(second,protectedId,"1234","7777")
                ==CharacterManagementStatus.NotOwned,"cross-account password mutation succeeded");
            Checks.Require(await store.DeleteCharacterWithPasswordAsync(second,protectedId,"1234")
                ==CharacterManagementStatus.NotOwned,"cross-account deletion succeeded");
            Checks.Require(await store.ChangeCharacterPasswordAsync(first,protectedId,"9999","7777")
                ==CharacterManagementStatus.InvalidPassword,"wrong old password accepted");
            Checks.Require(await store.VerifyCharacterPasswordAsync(first,protectedId,"1234")
                ==CharacterPasswordStatus.Success,"wrong management proof modified old credential");
            Checks.Require(await store.ChangeCharacterPasswordAsync(first,protectedId,"1234","7777")
                ==CharacterManagementStatus.Success,"password change failed");
            Checks.Require(await store.VerifyCharacterPasswordAsync(first,protectedId,"1234")
                ==CharacterPasswordStatus.InvalidPassword &&
                await store.VerifyCharacterPasswordAsync(first,protectedId,"7777")
                ==CharacterPasswordStatus.Success,"new credential did not replace old one");
            Checks.Require(await store.ChangeCharacterPasswordAsync(first,legacyId,null,"8888")
                ==CharacterManagementStatus.Success,"legacy character could not establish secondary credential");
            Checks.Require((await store.ListCharactersAsync(first)).Single(c=>c.CharacterId==legacyId)
                .RequiresCharacterPassword,"legacy password indicator not persisted");
            Checks.Require(await store.DeleteCharacterWithPasswordAsync(first,legacyId,"1111")
                ==CharacterManagementStatus.InvalidPassword,"wrong deletion proof accepted");
            Checks.Require(await store.DeleteCharacterWithPasswordAsync(first,legacyId,"8888")
                ==CharacterManagementStatus.Success,"authorized legacy deletion failed");
            Checks.Require((await store.ListCharactersAsync(first)).All(c=>c.CharacterId!=legacyId),
                "deleted character remains in server-owned list");
            Checks.Require(await store.DeleteCharacterWithPasswordAsync(first,protectedId,"7777")
                ==CharacterManagementStatus.Success,"authorized created character deletion failed");
            Checks.Require((await store.ListCharactersAsync(first)).Count==0,
                "post-delete list not empty");
            using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{
                DataSource=db,Pooling=false}.ToString());
            connection.Open();
            using var command=connection.CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM character_password_overrides;";
            Checks.Require(Convert.ToInt32(command.ExecuteScalar())==0,
                "secondary password hash survived character deletion");
        }
        finally {Directory.Delete(root,true);}
    }
}
