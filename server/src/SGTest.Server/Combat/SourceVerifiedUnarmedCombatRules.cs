using SGTest.Server.Identity;

namespace SGTest.Server.Combat;

/// <summary>
/// Narrow source-backed admission for an authoritative character that has no
/// equipped items. Equipment modifiers stay fail-closed until the original
/// EquipItem modifier mapping is fully audited.
/// </summary>
public static class SourceVerifiedUnarmedCombatRules
{
    public const double UnarmedAttackRange = 45.0;
    public const int OriginalLogicHz = 25;
    public const int OriginalAttackIntervalLogicTicks = 52;

    public static int AttackCooldownTicks(int worldTickHz)
    {
        if (worldTickHz is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(worldTickHz));

        // Paradise.exe.lst CSGOPlayer::SubLogic_Attacking:
        // an attack clears +8E9 and +8EC, then the disabled branch increments
        // +8EC until it is > 0x32. The next logic pass may attack: 52 passes.
        // Old service battle.cfg/complex.cfg/skill.cfg explicitly document a
        // 25 FPS base, so round up when translating into ServerWorldHost ticks.
        return checked((OriginalAttackIntervalLogicTicks * worldTickHz +
                        OriginalLogicHz - 1) / OriginalLogicHz);
    }

    public static bool TryPromoteEmptyEquipment(
        CharacterCombatProfileData profile,
        int equippedItemCount,
        int worldTickHz,
        out CharacterCombatProfileData effective)
    {
        ArgumentNullException.ThrowIfNull(profile);
        effective = profile;

        if (equippedItemCount != 0 ||
            profile.Trust != CombatProfileTrust.ProfileOnlyVerified ||
            profile.AttackRange != 0 ||
            profile.AttackCooldownTicks != 0 ||
            profile.Level is < 1 or > ushort.MaxValue ||
            profile.CurrentHp < 0 ||
            profile.MaxHp < 1 ||
            profile.CurrentHp > profile.MaxHp ||
            profile.Attack < 0 ||
            profile.Defense < 0 ||
            profile.Hit < 0 ||
            profile.Dodge < 0)
            return false;

        // Paradise.exe.lst CSGOPlayer::SyncAttackRange stores 0x7E9 (45^2)
        // when no weapon item is present. This promotion is intentionally
        // limited to an authoritative empty equipment snapshot so unresolved
        // armor/weapon modifiers can never leak into trusted combat.
        effective = profile with
        {
            Trust = CombatProfileTrust.Verified,
            SourceReference =
                profile.SourceReference +
                "|Paradise.exe.lst:CSGOPlayer::SyncAttackRange/SubLogic_Attacking" +
                "|Old service cfg:25fps",
            AttackRange = UnarmedAttackRange,
            AttackCooldownTicks = AttackCooldownTicks(worldTickHz)
        };
        return true;
    }
}
