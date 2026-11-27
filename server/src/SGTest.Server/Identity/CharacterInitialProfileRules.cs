namespace SGTest.Server.Identity;

/// <summary>
/// Source-backed level-one profile derivation for the four player races loaded by
/// Old SGOPlayerSys::LoadLvUpTable. The mapping was cross-checked against a live
/// Paradise character profile. This intentionally does not invent attack range,
/// attack cadence, starter-equipment bonuses, or Yas values.
/// </summary>
public sealed class CharacterInitialProfileRules
{
    public const string SourceReference =
        "Old SGOPlayer::UpdateParam + Dat/Player level/race tables; Paradise profile A/B";

    private static readonly int[][] Strength =
    [
        [2,4,6,8,10],
        [2,4,6,8,10],
        [2,4,6,8,10],
        [3,5,7,9,11]
    ];

    private static readonly int[][] Constitution =
    [
        [46,52,59,67,75],
        [44,48,53,59,65],
        [41,42,44,47,50],
        [41,42,44,47,50]
    ];

    private static readonly (int Hit,int Dodge)[][] Dexterity =
    [
        [(1,1),(2,2),(3,3),(4,4),(5,5)],
        [(1,2),(2,3),(3,4),(4,5),(5,6)],
        [(2,2),(3,3),(4,4),(5,5),(6,6)],
        [(1,1),(2,2),(3,3),(4,4),(5,5)]
    ];

    private static readonly int[][] WisdomMp =
    [
        [21,21,21,22,23],
        [21,21,21,22,23],
        [22,23,24,26,28],
        [21,21,21,22,23]
    ];

    public static CharacterInitialProfileRules OldParadiseLevelOne { get; } = new();

    private CharacterInitialProfileRules() { }

    public bool TryBuild(int race, CharacterCreationAttributes attributes,
        out CharacterCombatProfileData? profile)
    {
        CharacterCreationRules.Validate(attributes);
        if (race is < 0 or > 3)
        {
            profile = null;
            return false;
        }

        int str = attributes.Strength;
        int dex = attributes.Dexterity;
        int con = attributes.Constitution;
        int wis = attributes.Wisdom;
        var dexEffect = Dexterity[race][dex - 1];

        // AttBase_Newbie.csv level-one row:
        // HP 101, MP 101, physical attack 4, physical defense 3,
        // magic attack 4, magic defense 3, hit 6, dodge 6, will 101.
        // The current combat contract carries only the fields below.
        int maxHp = checked(101 + Constitution[race][con - 1]);
        int maxMp = checked(101 + WisdomMp[race][wis - 1]);
        // Old SGOPlayer WORD data exposes Will + WillMAX separately. The ninth
        // AttBase_Newbie value is the level base for WillMAX; level one is 101.
        int maxWp = 101;
        int attack = checked(4 + Strength[race][str - 1]);
        int defense = 3; // base defense only; starter-equipment bonus remains NOT VERIFIED.
        int hit = checked(6 + dexEffect.Hit);
        int dodge = checked(6 + dexEffect.Dodge);

        profile = new CharacterCombatProfileData(
            CombatProfileTrust.ProfileOnlyVerified,
            SourceReference,
            race,
            Career: 0,
            str,
            dex,
            con,
            wis,
            Level: 1,
            CurrentHp: maxHp,
            MaxHp: maxHp,
            Attack: attack,
            Defense: defense,
            Hit: hit,
            Dodge: dodge,
            AttackRange: 0,
            AttackCooldownTicks: 0,
            CurrentMp: maxMp,
            MaxMp: maxMp,
            CurrentWp: maxWp,
            MaxWp: maxWp);
        return true;
    }
}
