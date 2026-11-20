using System.Net;

namespace SGTest.Server.Gateway;

public sealed record AccountRegistrationRateLimitOptions(
    int MaxAttemptsPerAddress=3,
    TimeSpan? Window=null,
    int MaxTrackedAddresses=4096)
{
    public TimeSpan EffectiveWindow=>Window??TimeSpan.FromMinutes(10);
}

public readonly record struct AccountRegistrationRateLimitStatistics(
    int TrackedAddresses,long AllowedAttempts,long BlockedAttempts,
    long ExpiredBuckets,long CapacityBlocks);

/// <summary>
/// Bounded per-source quota for PUBLIC account-creation attempts. Every request
/// consumes quota regardless of CreateAccount result so successful creations,
/// duplicate probes and invalid inputs cannot bypass the expensive-work gate.
/// </summary>
public sealed class AccountRegistrationRateLimiter
{
    private sealed class Bucket
    {
        public required DateTimeOffset WindowStart;
        public required DateTimeOffset LastSeen;
        public int Attempts;
    }

    private readonly object _gate=new();
    private readonly AccountRegistrationRateLimitOptions _options;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<IPAddress,Bucket> _addresses=new();
    private long _allowed,_blocked,_expired,_capacityBlocks;

    public AccountRegistrationRateLimiter(
        AccountRegistrationRateLimitOptions? options=null,
        Func<DateTimeOffset>? utcNow=null)
    {
        _options=options??new();
        if(_options.MaxAttemptsPerAddress is <1 or >100||
           _options.EffectiveWindow<=TimeSpan.Zero||
           _options.EffectiveWindow>TimeSpan.FromDays(1)||
           _options.MaxTrackedAddresses is <16 or >1_000_000)
            throw new ArgumentOutOfRangeException(
                nameof(options),"Invalid account-registration rate-limit policy");
        _utcNow=utcNow??(()=>DateTimeOffset.UtcNow);
    }

    public AccountRegistrationRateLimitStatistics Statistics
    {
        get
        {
            lock(_gate)return new(
                _addresses.Count,_allowed,_blocked,_expired,_capacityBlocks);
        }
    }

    public bool TryAcquire(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        address=Canonical(address);
        DateTimeOffset now=_utcNow();
        lock(_gate)
        {
            PruneExpiredLocked(now);
            if(_addresses.TryGetValue(address,out var bucket))
            {
                bucket.LastSeen=now;
                if(bucket.Attempts>=_options.MaxAttemptsPerAddress)
                {
                    _blocked++;
                    return false;
                }
                bucket.Attempts=checked(bucket.Attempts+1);
                _allowed++;
                return true;
            }
            if(_addresses.Count>=_options.MaxTrackedAddresses)
            {
                _blocked++;
                _capacityBlocks++;
                return false;
            }
            _addresses.Add(address,new Bucket
            {
                WindowStart=now,LastSeen=now,Attempts=1
            });
            _allowed++;
            return true;
        }
    }

    private void PruneExpiredLocked(DateTimeOffset now)
    {
        foreach(var key in _addresses
            .Where(pair=>now-pair.Value.WindowStart>=_options.EffectiveWindow)
            .Select(pair=>pair.Key).ToArray())
            if(_addresses.Remove(key))_expired++;
    }

    private static IPAddress Canonical(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)return address.MapToIPv4();
        return address;
    }
}
