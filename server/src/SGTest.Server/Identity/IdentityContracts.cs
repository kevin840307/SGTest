namespace SGTest.Server.Identity;

// Stable persisted account, character and combat-profile contracts.
public sealed record IdentityStoreOptions(int MaxCharactersPerAccount = 8, int BusyTimeoutSeconds = 5);
public readonly record struct CharacterSpawn(int MapId, double X, double Y);
public readonly record struct CharacterPositionUpdate(long CharacterId, CharacterSpawn Spawn);
public sealed record CharacterRecord(long CharacterId, long AccountId, int Slot, string Name,
    int MapId, double X, double Y, long CreatedUnixMilliseconds,
    CharacterAppearanceState? Appearance = null);

// HairIndex is the presentation index, not an original Hair item-definition ID.
public readonly record struct CharacterAppearance(int Race, int HairIndex, int HairColor);
public sealed record CharacterAppearanceState(CharacterAppearance Value, string SourceReference);

public enum CombatProfileTrust : byte
{
    Verified = 1,
    SyntheticTest = 2
}

public sealed record CharacterCombatProfileData(
    CombatProfileTrust Trust,
    string SourceReference,
    int Race,
    int Career,
    int Strength,
    int Dexterity,
    int Constitution,
    int Wisdom,
    int Level,
    int CurrentHp,
    int MaxHp,
    int Attack,
    int Defense,
    int Hit,
    int Dodge,
    double AttackRange,
    int AttackCooldownTicks,
    int CurrentMp = 0,
    int MaxMp = 0);

public sealed record CharacterCombatProfile(
    long CharacterId,
    CharacterCombatProfileData Data,
    long UpdatedUnixMilliseconds);

public sealed record CharacterDamageSettlement(
    CharacterCombatProfile Profile,
    int DamageApplied,
    bool Died);

public enum CharacterMpConsumeStatus : byte
{
    Consumed = 0,
    MissingProfile = 1,
    Dead = 2,
    InsufficientMp = 3
}

public sealed record CharacterMpConsumeResult(
    CharacterMpConsumeStatus Status,
    CharacterCombatProfile? Profile,
    int MpConsumed);

public sealed record AccountRecord(long AccountId, string Username, bool Disabled,
    IReadOnlyList<CharacterRecord> Characters);

public enum AccountCreateStatus { Created, DuplicateUsername }
public sealed record AccountCreateResult(AccountCreateStatus Status, long AccountId = 0);
public enum AuthenticationStatus { Success, InvalidCredentials, Disabled }
public sealed record AuthenticationResult(AuthenticationStatus Status, AccountRecord? Account = null);
public enum CharacterCreateStatus { Created, UnknownAccount, SlotOccupied, NameTaken, LimitReached }
public sealed record CharacterCreateResult(CharacterCreateStatus Status, CharacterRecord? Character = null);
