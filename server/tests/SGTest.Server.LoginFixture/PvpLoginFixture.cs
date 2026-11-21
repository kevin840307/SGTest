using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Monster;
using SGTest.Server.Map;
using SGTest.Server.Combat;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class PvpLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_pvp_login_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string mapDir=Program.CreateMap(root,13);
            var grid=BlockingGrid.Load(mapDir,13);
            string populationPath=Path.Combine(mapDir,"population-pvp-skill.json");
            File.WriteAllText(populationPath,JsonSerializer.Serialize(new {
                version=4,kind="verified_monster_runtime",map_id=13,
                stats_verified=true,gameplay_ready=true,
                definitions=new[]{new {
                    id=1,name="PvpSkillLosOnly",sprite="EN001A",
                    atlas="Enemy/EN001A/atlas.json",
                    stats=new {
                        level=1,max_hp=100,attack_damage=1,move_step=1.0,
                        aggro_radius=1.0,lose_target_radius=2.0,leash_radius=2.0,
                        attack_range=1.0,attack_cooldown_ticks=300,
                        respawn_ticks=300,wander_radius=0.0,wander_interval_ticks=300
                    }}},
                spawns=new[]{new {
                    source_index=0,source_flags=7,definition=1,p=new[]{40,40}}}
            }));
            var runtime=new MonsterRuntime(
                grid,MonsterPopulationCatalog.LoadVerified(populationPath,13,grid),
                new(AiIntervalTicks:30,MaxAiPerStep:1,MaxPathQueriesPerStep:1,
                    MaxPathExpandedCells:64));
            var coordinator=new MonsterWorldCoordinator([runtime],
                new Dictionary<int,VerifiedSkillRule>{
                    [99003]=new(99003,60,48,1,1,
                        SkillTargetKind.SinglePlayer,0,true)
                });
            await using var world=new ServerWorldHost(
                [grid],options:new WorldHostOptions(PublishIdleTicks:true));
            await world.StartAsync().ConfigureAwait(false);
            using var store=new SqliteIdentityStore(Path.Combine(root,"identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);

            long accountA=(await store.CreateAccountAsync(
                "pvp_one",Program.Password)).AccountId;
            long accountB=(await store.CreateAccountAsync(
                "pvp_two",Program.Password)).AccountId;
            long actorA=(await store.CreateCharacterAsync(
                accountA,0,"PvpTlsAttacker",new CharacterSpawn(13,8,8)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("PvP attacker missing");
            long actorB=(await store.CreateCharacterAsync(
                accountB,0,"PvpTlsVictim",new CharacterSpawn(13,40,8)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("PvP victim missing");

            await store.SetCharacterCombatProfileAsync(actorA,
                Profile(100,60,0,2)).ConfigureAwait(false);
            await store.SetCharacterCombatProfileAsync(actorB,
                Profile(100,1,10)).ConfigureAwait(false);
            await store.SetCharacterPvpModeAsync(
                actorA,PvpMode.EnabledWhite).ConfigureAwait(false);
            await store.SetCharacterPvpModeAsync(
                actorB,PvpMode.EnabledWhite).ConfigureAwait(false);

            var router=new WorldResultRouter();
            var settlement=new PlayerCombatSettlement(
                store,new(Capacity:8),router.QueuePlayerDamage,
                committedLifecycle:async result=>
                {
                    if(result.SourceKind is not (
                        PlayerDamageSourceKind.PlayerPvp or
                        PlayerDamageSourceKind.PlayerSkill))
                        throw new PlayerCombatSettlementException(
                            "PvP fixture received non-PvP committed damage");
                    var now=DateTimeOffset.UtcNow;
                    CharacterPvpState pvp=result.Died
                        ? await store.MarkPvpWhiteKillAsync(
                            result.AttackerActorId,result.VictimWasNormalWhite,now)
                            .ConfigureAwait(false)
                        : await store.MarkPvpHostileAttackAsync(
                            result.AttackerActorId,result.VictimWasNormalWhite,now)
                            .ConfigureAwait(false);
                    var published=router.PublishPlayerPvpState(
                        result.AttackerActorId,result.AttackerConnectionEpoch,pvp);
                    if(published==GatewaySendStatus.Backpressured)
                        throw new PlayerCombatSettlementException(
                            "PvP fixture identity publication backpressured");
                    if(result.Died)
                    {
                        var stopped=await world.TrustedSetCombatDeadAsync(
                            result.ActorId,result.ConnectionEpoch,result.MapEpoch,true)
                            .ConfigureAwait(false);
                        if(stopped is null)
                            throw new PlayerCombatSettlementException(
                                "PvP lethal commit did not stop World victim");
                    }
                    if(result.SourceKind==PlayerDamageSourceKind.PlayerPvp)
                    {
                        if(!router.TryPostPlayerPvpCombatVisual(result))
                            throw new PlayerCombatSettlementException(
                                "PvP fixture committed visual queue full");
                    }
                    else if(!router.TryPostPlayerPvpSkillVisual(result))
                        throw new PlayerCombatSettlementException(
                            "PvP fixture committed skill visual queue full");
                });
            var pump=new WorldAoiPump(
                router,monsters:coordinator,playerCombat:settlement);
            var authorization=new IdentityTlsAuthorization(
                store,world,combat:settlement,
                gmAccounts:new HashSet<string>(
                    ["pvp_one"],StringComparer.OrdinalIgnoreCase));
            using var certificate=Program.CreateCertificate();
            string certPath=Path.Combine(root,"fixture-cert.pem");
            Program.WriteCertificatePem(certPath,certificate);
            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start(2);
            using var stop=new CancellationTokenSource();
            var routing=router.RunAsync(world.Results);
            var pumping=pump.RunAsync(stop.Token);
            var settling=settlement.RunAsync(stop.Token);
            try
            {
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"PVP_FIXTURE_READY port={port} cert={certPath} " +
                    $"actor_a={actorA} actor_b={actorB}");
                Console.Out.Flush();

                using var victim=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var victimTask=authorization.RunAuthenticatedSocketAsync(
                    victim,certificate,router,
                    new TlsWireConnectionOptions(
                        AuthorizationTimeoutMilliseconds:15000));

                using var attacker=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var attackerTask=authorization.RunAuthenticatedSocketAsync(
                    attacker,certificate,router,
                    new TlsWireConnectionOptions(
                        AuthorizationTimeoutMilliseconds:15000));

                var ended=await Task.WhenAll(victimTask,attackerTask)
                    .WaitAsync(TimeSpan.FromSeconds(35)).ConfigureAwait(false);
                var attackerPvp=await store.GetCharacterPvpStateAsync(actorA)
                    .ConfigureAwait(false);
                var victimPvp=await store.GetCharacterPvpStateAsync(actorB)
                    .ConfigureAwait(false);
                var attackerProfile=await store.GetCharacterCombatProfileAsync(actorA)
                    .ConfigureAwait(false);
                var victimProfile=await store.GetCharacterCombatProfileAsync(actorB)
                    .ConfigureAwait(false);
                var deadline=DateTime.UtcNow.AddSeconds(3);
                while(world.Statistics.ActiveActors!=0||
                      router.Statistics.ActiveSessions!=0)
                {
                    if(DateTime.UtcNow>=deadline)
                        throw new InvalidOperationException(
                            "PvP fixture left active actor/session");
                    await Task.Delay(5).ConfigureAwait(false);
                }

                if(ended[0].Reason!=TlsWireEndReason.CleanEof||
                   ended[1].Reason!=TlsWireEndReason.CleanEof||
                   ended[0].CommandsAccepted!=1||
                   ended[1].CommandsAccepted!=4||
                   attackerProfile?.Data.CurrentHp!=100||
                   attackerProfile?.Data.CurrentMp!=1||
                   victimProfile?.Data.CurrentHp!=0||
                   settlement.Statistics.HitsCommitted!=2||
                   settlement.Statistics.DamageCommitted!=100||
                   settlement.Statistics.DeathsCommitted!=1||
                   PvpCrimePolicy.NameState(
                       attackerPvp,DateTimeOffset.UtcNow)!=PvpNameState.Red||
                   victimPvp.Mode!=PvpMode.ProtectedBlue||
                   victimPvp.RedMan)
                    throw new InvalidOperationException(
                        $"PvP E2E mismatch victim_hp={victimProfile?.Data.CurrentHp} " +
                        $"attacker_hp={attackerProfile?.Data.CurrentHp} " +
                        $"attacker_mp={attackerProfile?.Data.CurrentMp} " +
                        $"hits={settlement.Statistics.HitsCommitted} " +
                        $"damage={settlement.Statistics.DamageCommitted} " +
                        $"deaths={settlement.Statistics.DeathsCommitted} " +
                        $"attacker_name={PvpCrimePolicy.NameState(attackerPvp,DateTimeOffset.UtcNow)} " +
                        $"victim_mode={victimPvp.Mode} victim_red={victimPvp.RedMan} " +
                        $"commands={ended[0].CommandsAccepted}/{ended[1].CommandsAccepted}");

                Console.WriteLine(
                    $"PVP_FIXTURE_PASS actor_a={actorA} actor_b={actorB} " +
                    $"victim_hp={victimProfile!.Data.CurrentHp}/{victimProfile.Data.MaxHp} " +
                    $"hits={settlement.Statistics.HitsCommitted} " +
                    $"damage={settlement.Statistics.DamageCommitted} " +
                    $"deaths={settlement.Statistics.DeathsCommitted} " +
                    $"attacker_name=Red attacker_mp={attackerProfile!.Data.CurrentMp}/" +
                    $"{attackerProfile.Data.MaxMp} victim_name=Blue " +
                    $"gm_audit=true commands={ended[0].CommandsAccepted}/{ended[1].CommandsAccepted}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await Task.WhenAll(pumping,settling).ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
            }
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("PVP_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
        }
    }

    private static CharacterCombatProfileData Profile(
        int hp,int attack,int defense,int mp=0)=>
        new(CombatProfileTrust.SyntheticTest,"ci:two-client-pvp",
            Race:0,Career:0,Strength:5,Dexterity:5,Constitution:5,Wisdom:5,
            Level:1,CurrentHp:hp,MaxHp:100,Attack:attack,Defense:defense,
            Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15,
            CurrentMp:mp,MaxMp:mp);
}
