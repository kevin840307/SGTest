using SGTest.Server.Npc;
using SGTest.Server.Map;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using SGTest.Server.Gateway;
using SGTest.Server.Transport;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class BankAesChecks
{
    public static void Verify(string root)
    {
        Checks.Run("AES bank open deposit replay relogin and withdraw remain durable",
            () => Lifecycle(root).GetAwaiter().GetResult());
    }

    private static async Task Lifecycle(string root)
    {
        string identityRoot=Path.Combine(Path.GetTempPath(),
            "sgtest_bank_tls_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(identityRoot);
        string db=Path.Combine(identityRoot,"identity.db");
        try
        {
            string mapDir=Checks.Fixture(root,79,5,5,(_,_)=>0);
            var grid=BlockingGrid.Load(mapDir,79);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long accountId=(await store.CreateAccountAsync(
                "bank_tls_user","Bank-Aes-Password-123!")).AccountId;
            long characterId=(await store.CreateCharacterAsync(
                accountId,0,"BankAesHero",new CharacterSpawn(79,8,8)))
                .Character?.CharacterId??throw new Exception("bank AES character missing");
            SeedWallet(db,characterId,1000);
            long generation=await store.AllocateLootWorldGenerationAsync();

            var banks=new NpcBankCatalog([
                new NpcBankCatalog.BankPoint(
                    79,88,8,8,BankScopePolicy.Account,"ci:bank-tls-account")
            ]);
            var authorization=new IdentitySessionAuthorization(
                store,world,pickupWorldGeneration:generation,banks:banks);
            var router=new WorldResultRouter();

            var first=await StartSession(authorization,router);
            await LoginAndChoose(first.Aes,characterId,
                "bank_tls_user","Bank-Aes-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(first.Aes),out var inventory)&&
                inventory is not null&&inventory.Complete&&inventory.Items.Count==0,
                "bank AES initial inventory baseline mismatch");

            long epoch=_lastMapEpoch;
            var openFrame=WireProtocol.EncodeBankOpen(new(epoch,1,88));
            await first.Aes.WriteAsync(openFrame);
            await first.Aes.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeBankSnapshot(
                await ReadFrame(first.Aes),out var opened)&&
                opened.Status==BankOpenStatus.Opened&&opened.NpcSpawnId==88&&
                opened.Scope==BankBalanceScope.Account&&
                opened.WalletMoney==1000&&opened.BankMoney==0,
                "bank AES open snapshot mismatch");

            var depositFrame=WireProtocol.EncodeBankDeposit(new(epoch,2,88,400));
            await first.Aes.WriteAsync(depositFrame);
            await first.Aes.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeBankTransactionResult(
                await ReadFrame(first.Aes),out var deposited)&&
                deposited.Status==BankTransactionStatus.Applied&&
                deposited.Kind==BankTransactionKind.Deposit&&
                deposited.Scope==BankBalanceScope.Account&&
                deposited.Amount==400&&deposited.WalletMoney==600&&
                deposited.BankMoney==400,
                "bank AES deposit mismatch");

            await first.Aes.WriteAsync(depositFrame);
            await first.Aes.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeBankTransactionResult(
                await ReadFrame(first.Aes),out var depositReplay)&&
                depositReplay.Status==BankTransactionStatus.AlreadyApplied&&
                depositReplay.WalletMoney==600&&depositReplay.BankMoney==400,
                "bank AES deposit replay mismatch");

            var withdrawFrame=WireProtocol.EncodeBankWithdraw(new(epoch,3,88,150));
            await first.Aes.WriteAsync(withdrawFrame);
            await first.Aes.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeBankTransactionResult(
                await ReadFrame(first.Aes),out var withdrawn)&&
                withdrawn.Status==BankTransactionStatus.Applied&&
                withdrawn.Kind==BankTransactionKind.Withdraw&&
                withdrawn.WalletMoney==750&&withdrawn.BankMoney==250,
                "bank AES withdraw mismatch");

            await first.Aes.DisposeAsync();
            first.Client.Dispose();
            var firstEnded=await first.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(firstEnded.Reason==AesWireEndReason.CleanEof,
                "bank AES first session did not close cleanly");

            var second=await StartSession(authorization,router);
            await LoginAndChoose(second.Aes,characterId,
                "bank_tls_user","Bank-Aes-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(second.Aes),out var inventory2)&&
                inventory2 is not null&&inventory2.Complete,
                "bank AES relogin inventory baseline missing");

            var reopen=WireProtocol.EncodeBankOpen(new(_lastMapEpoch,1,88));
            await second.Aes.WriteAsync(reopen);
            await second.Aes.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeBankSnapshot(
                await ReadFrame(second.Aes),out var reopened)&&
                reopened.Status==BankOpenStatus.Opened&&
                reopened.Scope==BankBalanceScope.Account&&
                reopened.WalletMoney==750&&reopened.BankMoney==250,
                "bank AES balances did not survive relogin");

            var finalWithdraw=WireProtocol.EncodeBankWithdraw(
                new(_lastMapEpoch,2,88,250));
            await second.Aes.WriteAsync(finalWithdraw);
            await second.Aes.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeBankTransactionResult(
                await ReadFrame(second.Aes),out var emptied)&&
                emptied.Status==BankTransactionStatus.Applied&&
                emptied.WalletMoney==1000&&emptied.BankMoney==0,
                "bank AES final withdraw mismatch");

            await second.Aes.DisposeAsync();
            second.Client.Dispose();
            var secondEnded=await second.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(secondEnded.Reason==AesWireEndReason.CleanEof,
                "bank AES second session did not close cleanly");

            var durable=await store.GetBankBalanceAsync(
                characterId,BankBalanceScope.Account);
            Checks.Require(durable.WalletMoney==1000&&durable.BankMoney==0,
                "bank final durable balance mismatch");
            await world.StopAsync();
        }
        finally
        {
            try{Directory.Delete(identityRoot,true);}catch{}
        }
    }

    private static void SeedWallet(string db,long characterId,long money)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE character_wallets SET money=$money WHERE character_id=$id;";
        command.Parameters.AddWithValue("$money",money);
        command.Parameters.AddWithValue("$id",characterId);
        Checks.Require(command.ExecuteNonQuery()==1,"bank AES wallet seed failed");
    }

    private static async Task<(AesCbcFrameStream Aes,Task<AesWireConnectionResult> ServerTask,TcpClient Client)>
        StartSession(IdentitySessionAuthorization authorization,WorldResultRouter router)
    {
        var settings=AesSocketChecks.CreateSettings();
        var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start(1);
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted=listener.AcceptTcpClientAsync();
        var client=new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback,port);
        var serverClient=await accepted;
        var serverTask=authorization.RunAuthenticatedAesSocketAsync(
            serverClient,settings,router).ContinueWith(t=>
            {

                listener.Stop();
                return t.GetAwaiter().GetResult();
            },TaskScheduler.Default);
        var tls=AesSocketChecks.CreateClientStream(client);

        return(tls,serverTask,client);
    }

    private static async Task LoginAndChoose(
        AesCbcFrameStream tls,long characterId,string username,string password)
    {
        await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new(username,password)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeAuthResult(
            await ReadFrame(tls),out var auth)&&auth.Status==AuthWireStatus.Success,
            "bank AES auth failed");
        Checks.Require(WireProtocol.TryDecodeCharacterList(
            await ReadFrame(tls),out var list)&&
            list.Characters.Single().CharacterId==characterId,
            "bank AES list mismatch");
        await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(characterId)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
            await ReadFrame(tls),out var choose)&&
            choose.Status==CharacterChooseWireStatus.Success,
            "bank AES choose failed");
        _lastMapEpoch=choose.MapEpoch;
    }

    private static long _lastMapEpoch;

    private static async Task<byte[]> ReadFrame(AesCbcFrameStream tls)
    {
        byte[] header=new byte[WireProtocol.HeaderBytes];
        await tls.ReadExactlyAsync(header);
        int length=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0,4));
        Checks.Require(length>=WireProtocol.HeaderBytes&&
            length<=WireProtocol.MaximumFrameBytes,
            "bank AES invalid frame length");
        byte[] frame=new byte[length];
        header.CopyTo(frame,0);
        if(length>header.Length)
            await tls.ReadExactlyAsync(frame.AsMemory(header.Length));
        return frame;
    }
}
