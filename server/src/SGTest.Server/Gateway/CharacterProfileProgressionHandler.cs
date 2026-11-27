using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Gateway;

/// <summary>
/// Authenticated self-profile progression boundary. The client can request only
/// which source-verified primary attribute to increment. Character identity,
/// available points and all derived values remain server-owned.
/// </summary>
public sealed class CharacterProfileProgressionHandler
{
    private readonly SqliteIdentityStore _identity;
    private readonly CharacterProgressionRules _rules;
    private readonly WorldResultRouter _router;

    public CharacterProfileProgressionHandler(
        SqliteIdentityStore identity,CharacterProgressionRules rules,
        WorldResultRouter router)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
        _rules=rules??throw new ArgumentNullException(nameof(rules));
        _router=router??throw new ArgumentNullException(nameof(router));
    }

    public async Task HandleAttributeAsync(TrustedPlayerAttributeAllocate request)
    {
        var expected=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.MapEpoch);
        if(!_router.TryGetCurrentBinding(request.ActorId,out var current)||
           current!=expected)
            return;

        var outcome=await _identity.AllocateCharacterAttributeAsync(
            request.ActorId,request.Attribute,_rules).ConfigureAwait(false);
        if(outcome.Profile is null)
            return;

        var experience=await _identity.GetCharacterExperienceAsync(
            request.ActorId).ConfigureAwait(false);
        var frame=BuildState(outcome.Profile.Data,request.ActorId,
            experience?.Experience??0,_rules);
        var status=_router.QueuePlayerProfileState(expected,frame);
        if(status==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException(
                "Committed attribute allocation profile queue backpressured");
        var vitals=_router.QueuePlayerCombatState(
            expected,outcome.Profile.Data.CurrentHp,outcome.Profile.Data.MaxHp,
            outcome.Profile.Data.CurrentMp,outcome.Profile.Data.MaxMp,
            outcome.Profile.Data.CurrentWp,outcome.Profile.Data.MaxWp);
        if(vitals==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException(
                "Committed attribute allocation vitals queue backpressured");
    }

    public static PlayerProfileStateWire BuildState(
        CharacterCombatProfileData profile,long characterId,long lifetimeExperience,
        CharacterProgressionRules? rules)
    {
        long visibleExperience=rules is null
            ?lifetimeExperience
            :rules.CurrentExperience(profile.Level,lifetimeExperience);
        int experiencePercent=rules is null
            ?-1
            :rules.ExperiencePercent(profile.Level,lifetimeExperience);
        PlayerProfileStatVerification verifiedStats=profile.Trust switch
        {
            CombatProfileTrust.Verified or CombatProfileTrust.SyntheticTest =>
                PlayerProfileStatVerification.All,
            // Old SGOPlayer::UpdateParam + CheckAttrib prove effective values are
            // clamped base + modifier. HK starter candidates have zero Attack/Hit/
            // Dodge modifiers, while Defense candidates are non-zero and their
            // effective application is still unresolved.
            CombatProfileTrust.ProfileOnlyVerified =>
                PlayerProfileStatVerification.Attack |
                PlayerProfileStatVerification.Hit |
                PlayerProfileStatVerification.Dodge,
            _ => PlayerProfileStatVerification.None
        };
        return new(
            characterId,profile.Race,profile.Career,profile.Level,visibleExperience,
            profile.Strength,profile.Dexterity,profile.Constitution,profile.Wisdom,
            profile.Attack,profile.Defense,profile.Hit,profile.Dodge,
            profile.AvailableAttributePoints,verifiedStats,experiencePercent);
    }
}
