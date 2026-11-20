using System.Net;

namespace SGTest.Server.Gateway;

public sealed record AuthenticationRateLimitOptions(
    int MaxFailuresPerAddress = 12,
    int MaxFailuresPerAccount = 6,
    TimeSpan? FailureWindow = null,
    TimeSpan? Lockout = null,
    int MaxTrackedAddresses = 4096,
    int MaxTrackedAccounts = 16384)
{
    public TimeSpan EffectiveFailureWindow => FailureWindow ?? TimeSpan.FromMinutes(1);
    public TimeSpan EffectiveLockout => Lockout ?? TimeSpan.FromSeconds(30);
}

public readonly record struct AuthenticationRateLimitStatistics(
    int TrackedAddresses,int TrackedAccounts,long AllowedChecks,long BlockedChecks,
    long RecordedFailures,long AccountSuccessResets,long Evictions,
    long CapacitySaturationBlocks);

/// <summary>
/// In-memory brute-force limiter for the pre-gameplay TLS authorization boundary.
/// It is deliberately not an account database and does not persist lockouts across
/// process restart. Tables are bounded so hostile usernames/addresses cannot turn
/// the limiter itself into an unbounded-memory attack.
/// </summary>
public sealed class AuthenticationRateLimiter
{
    private sealed class Bucket
    {
        public DateTimeOffset WindowStart;
        public DateTimeOffset LastSeen;
        public DateTimeOffset BlockedUntil;
        public int Failures;
    }

    private readonly AuthenticationRateLimitOptions _options;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _gate=new();
    private readonly Dictionary<IPAddress,Bucket> _addresses=[];
    private readonly Dictionary<string,Bucket> _accounts=
        new(StringComparer.Ordinal);
    private DateTimeOffset _addressCapacityBlockedUntil=DateTimeOffset.MinValue;
    private DateTimeOffset _accountCapacityBlockedUntil=DateTimeOffset.MinValue;
    private long _allowed,_blocked,_failures,_successResets,_evictions,
        _capacitySaturationBlocks;

    public AuthenticationRateLimiter(
        AuthenticationRateLimitOptions? options=null,
        Func<DateTimeOffset>? utcNow=null)
    {
        _options=options??new();
        _utcNow=utcNow??(()=>DateTimeOffset.UtcNow);
        if(_options.MaxFailuresPerAddress is <1 or >10000||
           _options.MaxFailuresPerAccount is <1 or >10000||
           _options.EffectiveFailureWindow<TimeSpan.FromSeconds(1)||
           _options.EffectiveFailureWindow>TimeSpan.FromHours(24)||
           _options.EffectiveLockout<TimeSpan.FromSeconds(1)||
           _options.EffectiveLockout>TimeSpan.FromHours(24)||
           _options.MaxTrackedAddresses is <16 or >1_000_000||
           _options.MaxTrackedAccounts is <16 or >1_000_000)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public AuthenticationRateLimitStatistics Statistics
    {
        get
        {
            lock(_gate)return new(
                _addresses.Count,_accounts.Count,_allowed,_blocked,
                _failures,_successResets,_evictions,_capacitySaturationBlocks);
        }
    }

    public bool IsAllowed(IPAddress address,string username)
    {
        ArgumentNullException.ThrowIfNull(address);
        address=Canonical(address);
        string account=NormalizeAccountKey(username);
        var now=_utcNow();
        lock(_gate)
        {
            PruneExpiredLocked(now);
            bool blocked=_addressCapacityBlockedUntil>now||
                _accountCapacityBlockedUntil>now||
                IsBlockedLocked(_addresses,address,now)||
                IsBlockedLocked(_accounts,account,now);
            if(blocked)_blocked++; else _allowed++;
            return !blocked;
        }
    }

    public void RecordFailure(IPAddress address,string username)
    {
        ArgumentNullException.ThrowIfNull(address);
        address=Canonical(address);
        string account=NormalizeAccountKey(username);
        var now=_utcNow();
        lock(_gate)
        {
            PruneExpiredLocked(now);
            if(!RecordFailureLocked(
                _addresses,address,now,_options.MaxFailuresPerAddress,
                _options.MaxTrackedAddresses))
            {
                _addressCapacityBlockedUntil=Max(
                    _addressCapacityBlockedUntil,
                    now+_options.EffectiveLockout);
                _capacitySaturationBlocks++;
            }
            if(!RecordFailureLocked(
                _accounts,account,now,_options.MaxFailuresPerAccount,
                _options.MaxTrackedAccounts))
            {
                _accountCapacityBlockedUntil=Max(
                    _accountCapacityBlockedUntil,
                    now+_options.EffectiveLockout);
                _capacitySaturationBlocks++;
            }
            _failures++;
        }
    }

    /// <summary>
    /// Successful credential verification clears only that account's failure
    /// bucket. Address failures intentionally remain until the normal window/
    /// lockout expires so one valid account cannot launder a hostile source IP.
    /// </summary>
    public void RecordCredentialSuccess(string username)
    {
        string account=NormalizeAccountKey(username);
        lock(_gate)
        {
            if(_accounts.Remove(account))_successResets++;
        }
    }

    public static string NormalizeAccountKey(string? username)
    {
        if(username is null||username.Length is <3 or >32)
            return "<invalid>";
        foreach(char c in username)
            if(!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or
                 >= '0' and <= '9' or '_'))
                return "<invalid>";
        return username.ToLowerInvariant();
    }

    private bool IsBlockedLocked<TKey>(
        Dictionary<TKey,Bucket> buckets,TKey key,DateTimeOffset now)
        where TKey:notnull
    {
        if(!buckets.TryGetValue(key,out var bucket))return false;
        ResetWindowIfExpired(bucket,now);
        bucket.LastSeen=now;
        return bucket.BlockedUntil>now;
    }

    private bool RecordFailureLocked<TKey>(
        Dictionary<TKey,Bucket> buckets,TKey key,DateTimeOffset now,
        int threshold,int capacity) where TKey:notnull
    {
        if(!buckets.TryGetValue(key,out var bucket))
        {
            if(!EnsureCapacityLocked(buckets,capacity,now))
                return false;
            bucket=new Bucket
            {
                WindowStart=now,LastSeen=now,BlockedUntil=DateTimeOffset.MinValue
            };
            buckets.Add(key,bucket);
        }
        ResetWindowIfExpired(bucket,now);
        bucket.LastSeen=now;
        bucket.Failures=checked(bucket.Failures+1);
        if(bucket.Failures>=threshold)
            bucket.BlockedUntil=Max(bucket.BlockedUntil,
                now+_options.EffectiveLockout);
        return true;
    }

    private void ResetWindowIfExpired(Bucket bucket,DateTimeOffset now)
    {
        if(now-bucket.WindowStart<_options.EffectiveFailureWindow)return;
        bucket.WindowStart=now;
        bucket.Failures=0;
        if(bucket.BlockedUntil<=now)
            bucket.BlockedUntil=DateTimeOffset.MinValue;
    }

    private void PruneExpiredLocked(DateTimeOffset now)
    {
        var horizon=Max(_options.EffectiveFailureWindow,_options.EffectiveLockout);
        foreach(var key in _addresses
            .Where(x=>x.Value.BlockedUntil<=now&&now-x.Value.LastSeen>=horizon)
            .Select(x=>x.Key).ToArray())
            _addresses.Remove(key);
        foreach(var key in _accounts
            .Where(x=>x.Value.BlockedUntil<=now&&now-x.Value.LastSeen>=horizon)
            .Select(x=>x.Key).ToArray())
            _accounts.Remove(key);
    }

    private bool EnsureCapacityLocked<TKey>(
        Dictionary<TKey,Bucket> buckets,int capacity,DateTimeOffset now)
        where TKey:notnull
    {
        if(buckets.Count<capacity)return true;
        var candidate=buckets
            .Where(x=>x.Value.BlockedUntil<=now)
            .OrderBy(x=>x.Value.LastSeen)
            .Select(x=>x.Key)
            .Take(1)
            .ToArray();
        // Never evict an active lockout merely to admit a new hostile key.
        // If every bounded slot is protected, the caller activates a short
        // scope-wide fail-closed gate instead of turning capacity pressure
        // into a lockout-bypass primitive.
        if(candidate.Length==0)return false;
        if(buckets.Remove(candidate[0]))
        {
            _evictions++;
            return true;
        }
        return false;
    }

    private static IPAddress Canonical(IPAddress address)=>
        address.IsIPv4MappedToIPv6?address.MapToIPv4():address;

    private static DateTimeOffset Max(DateTimeOffset a,DateTimeOffset b)=>
        a>=b?a:b;

    private static TimeSpan Max(TimeSpan a,TimeSpan b)=>a>=b?a:b;
}
