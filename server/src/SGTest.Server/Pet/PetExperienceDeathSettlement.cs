using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Monster;
using System.Threading.Channels;

namespace SGTest.Server.Pet;

/// <summary>
/// Source-backed Pet EXP split from the legacy battle settlement.
///
/// Old SGOBattleSys::CheckExpArrange(SGOPlayer*, ushort, ulong, ushort, ushort)
/// first finishes its player/party level adjustment, then shifts that settled
/// character EXP right by one before calling CSGOPetObj::AddExp. Therefore Pet
/// EXP is floor(settled character EXP / 2). Enemy RExp is an upstream battle
/// input and MUST NOT be passed here as if it were final Pet EXP.
/// </summary>
public static class PetExperienceSettlementPolicy
{
    public static long FromSettledCharacterExperience(long settledCharacterExperience)
    {
        if(settledCharacterExperience<0)
            throw new ArgumentOutOfRangeException(nameof(settledCharacterExperience));
        return settledCharacterExperience>>1;
    }
}

public enum PetExperienceDeathPostStatus : byte
{
    Enqueued,
    NoExperience,
    Backpressured
}

public sealed record PetExperienceDeathStatistics(
    long DeathsQueued,long GrantsApplied,long Replays,
    long Overflows,int Pending);

public sealed class PetExperienceDeathSettlementException : Exception
{
    public PetExperienceDeathSettlementException(
        string message,Exception? inner=null):base(message,inner){}
}

/// <summary>
/// Bounded non-World settlement for a Pet-authored lethal monster hit AFTER the
/// authoritative battle reward pipeline has produced the owner's settled
/// character EXP. This class deliberately does not derive rewards from map,
/// enemy id, level or RExp: doing so here would bypass the original battle
/// adjustment order. A per-host generation remains part of the replay key so
/// monster revisions reused after restart cannot suppress a legitimate reward.
///
/// Production must stay disconnected from this sink until the character EXP
/// authority can supply the exact settled value for the same lethal event.
/// </summary>
public sealed class PetExperienceDeathSettlement
{
    private readonly PetCoordinator _pets;
    private readonly WorldResultRouter _router;
    private readonly long _generation;
    private readonly Channel<(PetWorldPresence Pet,MonsterSnapshot Death,
        long PetExperience)> _queue;
    private long _queued,_applied,_replays,_overflows;
    private int _pending,_started;
    private Exception? _fault;

    public PetExperienceDeathSettlement(
        PetCoordinator pets,WorldResultRouter router,long generation,int capacity=256)
    {
        _pets=pets??throw new ArgumentNullException(nameof(pets));
        _router=router??throw new ArgumentNullException(nameof(router));
        _=SqliteIdentityStore.ComposeLootDeathRevision(generation,1);
        if(capacity is <1 or >65536)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _generation=generation;
        _queue=Channel.CreateBounded<(PetWorldPresence,MonsterSnapshot,long)>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader=true,
                SingleWriter=true,
                FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
            });
    }

    public PetExperienceDeathStatistics Statistics=>new(
        Interlocked.Read(ref _queued),Interlocked.Read(ref _applied),
        Interlocked.Read(ref _replays),Interlocked.Read(ref _overflows),
        Volatile.Read(ref _pending));

    public PetExperienceDeathPostStatus TryPostDeath(
        PetWorldPresence pet,MonsterSnapshot monster,long settledCharacterExperience)
    {
        if(pet.OwnerActorId<=0||pet.OwnerConnectionEpoch<=0||
           pet.OwnerMapEpoch<=0||pet.PetUid<=0||
           monster.EntityId<=0||monster.EnemyId<=0||monster.MapId<=0||
           monster.CurrentHp!=0||monster.State!=MonsterAiState.Dead||
           monster.EntityRevision<=0)
            throw new ArgumentOutOfRangeException(nameof(monster));
        if(_fault is { } failure)
            throw new PetExperienceDeathSettlementException(
                "Pet EXP death worker failed",failure);

        long petExperience=
            PetExperienceSettlementPolicy.FromSettledCharacterExperience(
                settledCharacterExperience);
        if(petExperience==0)
            return PetExperienceDeathPostStatus.NoExperience;

        _=SqliteIdentityStore.ComposeLootDeathRevision(
            _generation,monster.EntityRevision);
        if(!_queue.Writer.TryWrite((pet,monster,petExperience)))
        {
            Interlocked.Increment(ref _overflows);
            return PetExperienceDeathPostStatus.Backpressured;
        }
        Interlocked.Increment(ref _pending);
        Interlocked.Increment(ref _queued);
        return PetExperienceDeathPostStatus.Enqueued;
    }

    public void Complete()=>_queue.Writer.TryComplete();

    public async Task RunAsync()
    {
        if(Interlocked.Exchange(ref _started,1)!=0)
            throw new InvalidOperationException(
                "Only one Pet EXP death worker allowed");
        try
        {
            await foreach(var (pet,death,petExperience) in
                _queue.Reader.ReadAllAsync())
            {
                try
                {
                    long deathId=SqliteIdentityStore.ComposeLootDeathRevision(
                        _generation,death.EntityRevision);
                    string grantKey=$"petdeath:{deathId}:{death.EntityId}";
                    var binding=new ConnectionBinding(
                        pet.OwnerActorId,pet.OwnerConnectionEpoch,pet.OwnerMapEpoch);
                    var outcome=await _pets.GrantCommittedExperienceAsync(
                        binding,pet.PetUid,petExperience,grantKey,_router)
                        .ConfigureAwait(false);
                    if(outcome.Status==PetExperienceGrantStatus.Applied)
                        Interlocked.Increment(ref _applied);
                    else if(outcome.Status==PetExperienceGrantStatus.AlreadyProcessed)
                        Interlocked.Increment(ref _replays);
                    else
                        throw new PetExperienceDeathSettlementException(
                            $"Trusted Pet EXP death grant failed: {outcome.Status}");
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        catch(Exception failure)
        {
            _fault=failure;
            throw;
        }
    }
}
