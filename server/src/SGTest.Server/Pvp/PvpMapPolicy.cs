namespace SGTest.Server.Pvp;

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
