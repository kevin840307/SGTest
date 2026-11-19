using SGTest.Server.Identity;

namespace SGTest.Server.World;

public enum PvpMode : byte
{
    ProtectedBlue = 0,
    EnabledWhite = 1
}

/// <summary>
/// Verified from Old/服務端/map.cfg [NonPKMap]. This policy says only whether
/// player PvP is permitted on a map; it does not invent damage, crime timers,
/// faction rules, or red/pink transitions.
/// </summary>
public static class PvpMapPolicy
{
    private static readonly HashSet<int> NonPvpMaps =
        [7, 9, 15, 17, 22, 25, 38, 40];

    public static bool AllowsPlayerPvp(int mapId) =>
        mapId > 0 && !NonPvpMaps.Contains(mapId);

    public static IReadOnlyCollection<int> VerifiedNonPvpMaps => NonPvpMaps;
}


public enum PvpNameState : byte
{
    ProtectedBlue = 0,
    EnabledWhite = 1,
    Red = 2,
    Pink = 3,
    PinkFlashing = 4
}

/// <summary>
/// Crime/name-color timing recovered from the private reference implementation:
/// hostile attack against a normal white player => pink for 30s; final 3s flash;
/// killing a normal white player => red. Pink/red targets do not escalate crime.
/// </summary>
public static class PvpCrimePolicy
{
    public const int PinkDurationSeconds = 30;
    public const int PinkFlashSeconds = 3;

    public static bool IsPink(CharacterPvpState state, DateTimeOffset now) =>
        state.PinkUntilUnixMilliseconds > now.ToUnixTimeMilliseconds();

    public static bool IsNormalWhite(CharacterPvpState state, DateTimeOffset now) =>
        state.Mode == PvpMode.EnabledWhite && !state.RedMan && !IsPink(state,now);

    public static bool IsCombatEligible(CharacterPvpState state, DateTimeOffset now) =>
        state.Mode == PvpMode.EnabledWhite || state.RedMan || IsPink(state,now);

    public static PvpNameState NameState(CharacterPvpState state, DateTimeOffset now)
    {
        if(state.RedMan) return PvpNameState.Red;
        long remaining=state.PinkUntilUnixMilliseconds-now.ToUnixTimeMilliseconds();
        if(remaining>0)
            return remaining<=PinkFlashSeconds*1000L
                ? PvpNameState.PinkFlashing:PvpNameState.Pink;
        return state.Mode==PvpMode.ProtectedBlue
            ? PvpNameState.ProtectedBlue:PvpNameState.EnabledWhite;
    }
}
