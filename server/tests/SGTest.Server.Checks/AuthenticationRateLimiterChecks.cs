using System.Net;
using SGTest.Server.Gateway;

internal static class AuthenticationRateLimiterChecks
{
    public static void Verify()
    {
        Checks.Run("authentication limiter scopes address account lockout and success reset",Scope);
        Checks.Run("authentication limiter bounds hostile address and username tracking",Bounded);
        Checks.Run("authentication limiter never evicts active lockouts under capacity pressure",SaturationFailClosed);
    }

    private static void Scope()
    {
        var now=new DateTimeOffset(2026,11,20,0,0,0,TimeSpan.Zero);
        var limiter=new AuthenticationRateLimiter(new(
            MaxFailuresPerAddress:2,
            MaxFailuresPerAccount:3,
            FailureWindow:TimeSpan.FromMinutes(1),
            Lockout:TimeSpan.FromSeconds(30),
            MaxTrackedAddresses:16,
            MaxTrackedAccounts:16),()=>now);
        var a=IPAddress.Parse("192.0.2.10");
        var b=IPAddress.Parse("192.0.2.11");
        var c=IPAddress.Parse("192.0.2.12");

        Checks.Require(limiter.IsAllowed(a,"User_One"),
            "first auth attempt unexpectedly blocked");
        limiter.RecordFailure(a,"User_One");
        Checks.Require(limiter.IsAllowed(a,"user_one"),
            "second auth attempt unexpectedly blocked");
        limiter.RecordFailure(a,"USER_ONE");
        Checks.Require(!limiter.IsAllowed(a,"other_user"),
            "address lockout did not apply across usernames");

        Checks.Require(limiter.IsAllowed(b,"user_one"),
            "account locked before configured threshold");
        limiter.RecordFailure(b,"user_one");
        Checks.Require(!limiter.IsAllowed(c,"USER_ONE"),
            "case-insensitive account lockout did not apply across addresses");

        limiter.RecordCredentialSuccess("UsEr_OnE");
        Checks.Require(limiter.IsAllowed(c,"user_one"),
            "successful credential verification did not clear account failures");
        Checks.Require(!limiter.IsAllowed(a,"unrelated"),
            "account success incorrectly cleared hostile address failures");

        now+=TimeSpan.FromSeconds(31);
        Checks.Require(limiter.IsAllowed(a,"unrelated"),
            "address lockout did not expire");
        var stats=limiter.Statistics;
        Checks.Require(stats.RecordedFailures==3&&stats.BlockedChecks>=3&&
            stats.AccountSuccessResets==1,
            "authentication limiter statistics mismatch");
    }


    private static void SaturationFailClosed()
    {
        var now=new DateTimeOffset(2026,11,20,0,0,0,TimeSpan.Zero);
        var limiter=new AuthenticationRateLimiter(new(
            MaxFailuresPerAddress:1,
            MaxFailuresPerAccount:1,
            FailureWindow:TimeSpan.FromMinutes(1),
            Lockout:TimeSpan.FromSeconds(30),
            MaxTrackedAddresses:16,
            MaxTrackedAccounts:16),()=>now);

        for(int i=0;i<16;i++)
            limiter.RecordFailure(
                IPAddress.Parse($"203.0.113.{i+1}"),$"locked_{i:00}");

        var before=limiter.Statistics;
        Checks.Require(before.TrackedAddresses==16&&
            before.TrackedAccounts==16&&before.Evictions==0,
            "lockout saturation fixture did not fill bounded tables");

        limiter.RecordFailure(IPAddress.Parse("203.0.113.200"),"new_locked");
        var saturated=limiter.Statistics;
        Checks.Require(saturated.TrackedAddresses==16&&
            saturated.TrackedAccounts==16&&saturated.Evictions==0&&
            saturated.CapacitySaturationBlocks==2,
            "capacity pressure evicted an active lockout instead of failing closed");
        Checks.Require(!limiter.IsAllowed(
                IPAddress.Parse("198.51.100.200"),"fresh_account"),
            "fresh key bypassed saturated active lockout tables");

        now+=TimeSpan.FromSeconds(31);
        Checks.Require(limiter.IsAllowed(
                IPAddress.Parse("198.51.100.200"),"fresh_account"),
            "scope-wide capacity gate did not expire with configured lockout");
    }

    private static void Bounded()
    {
        var now=new DateTimeOffset(2026,11,20,0,0,0,TimeSpan.Zero);
        var limiter=new AuthenticationRateLimiter(new(
            MaxFailuresPerAddress:100,
            MaxFailuresPerAccount:100,
            FailureWindow:TimeSpan.FromMinutes(5),
            Lockout:TimeSpan.FromMinutes(1),
            MaxTrackedAddresses:16,
            MaxTrackedAccounts:16),()=>now);
        for(int i=0;i<64;i++)
        {
            var address=IPAddress.Parse($"198.51.100.{1+i%250}");
            limiter.RecordFailure(address,$"bot_{i:000}");
            now+=TimeSpan.FromMilliseconds(1);
        }
        var stats=limiter.Statistics;
        Checks.Require(stats.TrackedAddresses<=16&&stats.TrackedAccounts<=16&&
            stats.Evictions>0,
            "authentication limiter tracking exceeded configured bound");
    }
}
