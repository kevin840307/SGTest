using SGTest.Server.Visibility;
using SGTest.Server.Npc;
using SGTest.Server.Map;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Transport;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class BankLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_bank_login_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            var grid=BlockingGrid.Load(Program.CreateMap(root,15),15);
            await using var world=new ServerWorldHost(
                [grid],options:new WorldHostOptions(PublishIdleTicks:true));
            await world.StartAsync().ConfigureAwait(false);
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId=(await store.CreateAccountAsync(
                Program.Username,Program.Password)).AccountId;
            long characterId=(await store.CreateCharacterAsync(
                accountId,0,"BankHero",new CharacterSpawn(15,24,24)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("bank character create failed");
            SeedWallet(db,characterId,1000);

            long generation=await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var banks=new NpcBankCatalog([
                new NpcBankCatalog.BankPoint(
                    15,88,24,24,BankScopePolicy.Account,
                    "ci:godot-tls-bank-account")
            ]);
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            var authorization=new IdentitySessionAuthorization(
                store,world,pickupWorldGeneration:generation,banks:banks);
            var settings = AesCbcSettings.FromEnvironment();
                                    var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start(1);
            using var stop=new CancellationTokenSource();
            var routing=router.RunAsync(world.Results);
            var pumping=pump.RunAsync(stop.Token);
            try
            {
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"BANK_FIXTURE_READY port={port} username={Program.Username} " +
                    $"character={characterId} npc=88 scope=account wallet=1000");
                Console.Out.Flush();

                using var client=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended=await authorization.RunAuthenticatedAesSocketAsync(
                    client,settings,router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000))
                    .WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);

                var balance=await store.GetBankBalanceAsync(
                    characterId,BankBalanceScope.Account).ConfigureAwait(false);
                if(balance.WalletMoney!=1000||balance.BankMoney!=0||
                   ended.CommandsAccepted!=4||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"bank E2E mismatch wallet={balance.WalletMoney} bank={balance.BankMoney} " +
                        $"commands={ended.CommandsAccepted} sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"BANK_FIXTURE_PASS aes=true scope=account " +
                    $"wallet={balance.WalletMoney} bank={balance.BankMoney} " +
                    $"commands={ended.CommandsAccepted}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await pumping.ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("BANK_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
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
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("bank fixture wallet seed failed");
    }
}
