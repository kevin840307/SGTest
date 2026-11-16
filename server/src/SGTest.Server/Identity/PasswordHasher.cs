using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace SGTest.Server.Identity;

public sealed record PasswordHashPolicy(
    int MemoryKiB = 9216,
    int Iterations = 4,
    int Parallelism = 1,
    int SaltBytes = 16,
    int HashBytes = 32,
    int MaxConcurrentOperations = 4);

/// <summary>
/// Argon2id password hashing using an explicit PHC-like string. The default
/// m=9216,t=4,p=1 is one of the current OWASP equivalent minimum profiles and
/// was selected after a local target-machine comparison. Authentication work is
/// bounded so a login burst cannot allocate unbounded KDF memory.
/// </summary>
public sealed class PasswordHasher : IDisposable
{
    private const int Version = 19;
    private readonly PasswordHashPolicy _policy;
    private readonly SemaphoreSlim _slots;

    public PasswordHasher(PasswordHashPolicy? policy = null)
    {
        _policy = policy ?? new PasswordHashPolicy();
        ValidatePolicy(_policy);
        _slots = new SemaphoreSlim(_policy.MaxConcurrentOperations, _policy.MaxConcurrentOperations);
    }

    public PasswordHashPolicy Policy => _policy;

    public async Task<string> HashAsync(string password, CancellationToken cancellationToken = default)
    {
        ValidateNewPassword(password);
        byte[] salt = RandomNumberGenerator.GetBytes(_policy.SaltBytes);
        byte[] hash = await DeriveBoundedAsync(password, salt, _policy.MemoryKiB,
            _policy.Iterations, _policy.Parallelism, _policy.HashBytes, cancellationToken)
            .ConfigureAwait(false);
        return Encode(_policy.MemoryKiB, _policy.Iterations, _policy.Parallelism, salt, hash);
    }

    public async Task<bool> VerifyAsync(string password, string encoded,
        CancellationToken cancellationToken = default)
    {
        if (Encoding.UTF8.GetByteCount(password) > 1024 || !TryParse(encoded, out var parsed))
            return false;
        byte[] actual = await DeriveBoundedAsync(password, parsed.Salt, parsed.MemoryKiB,
            parsed.Iterations, parsed.Parallelism, parsed.Hash.Length, cancellationToken)
            .ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(actual, parsed.Hash);
    }

    public bool NeedsRehash(string encoded) =>
        !TryParse(encoded, out var parsed) ||
        parsed.MemoryKiB != _policy.MemoryKiB ||
        parsed.Iterations != _policy.Iterations ||
        parsed.Parallelism != _policy.Parallelism ||
        parsed.Salt.Length != _policy.SaltBytes ||
        parsed.Hash.Length != _policy.HashBytes;

    internal string CreateDeterministicDummyHash()
    {
        byte[] salt = SHA256.HashData(Encoding.UTF8.GetBytes("SGTest-auth-missing-account-dummy"))[.._policy.SaltBytes];
        byte[] hash = Derive("dummy-password-never-stored", salt, _policy.MemoryKiB,
            _policy.Iterations, _policy.Parallelism, _policy.HashBytes);
        return Encode(_policy.MemoryKiB, _policy.Iterations, _policy.Parallelism, salt, hash);
    }

    private async Task<byte[]> DeriveBoundedAsync(string password, byte[] salt,
        int memoryKiB, int iterations, int parallelism, int hashBytes,
        CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Derive(password, salt, memoryKiB, iterations,
                parallelism, hashBytes), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }

    private static byte[] Derive(string password, byte[] salt, int memoryKiB,
        int iterations, int parallelism, int hashBytes)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        return argon.GetBytes(hashBytes);
    }

    private static string Encode(int memoryKiB, int iterations, int parallelism,
        byte[] salt, byte[] hash) =>
        $"$argon2id$v={Version}$m={memoryKiB},t={iterations},p={parallelism}${B64(salt)}${B64(hash)}";

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private readonly record struct ParsedHash(
        int MemoryKiB, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);

    private static bool TryParse(string encoded, out ParsedHash parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > 512) return false;
        string[] parts = encoded.Split('$');
        if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id" || parts[2] != "v=19")
            return false;
        string[] costs = parts[3].Split(',');
        if (costs.Length != 3 || !TryCost(costs[0], "m=", out int memory) ||
            !TryCost(costs[1], "t=", out int iterations) ||
            !TryCost(costs[2], "p=", out int parallelism)) return false;
        if (memory is < 7168 or > 65536 || iterations is < 1 or > 10 || parallelism is < 1 or > 4)
            return false;
        try
        {
            byte[] salt = FromB64(parts[4]);
            byte[] hash = FromB64(parts[5]);
            if (salt.Length is < 16 or > 64 || hash.Length is < 16 or > 64) return false;
            parsed = new ParsedHash(memory, iterations, parallelism, salt, hash);
            return true;
        }
        catch (FormatException) { return false; }
    }

    private static bool TryCost(string value, string prefix, out int result)
    {
        result = 0;
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(value.AsSpan(prefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out result);
    }

    private static byte[] FromB64(string value)
    {
        int pad = (4 - value.Length % 4) % 4;
        return Convert.FromBase64String(value + new string('=', pad));
    }

    private static void ValidatePolicy(PasswordHashPolicy policy)
    {
        if (policy.MemoryKiB is < 7168 or > 65536 || policy.Iterations is < 1 or > 10 ||
            policy.Parallelism is < 1 or > 4 || policy.SaltBytes is < 16 or > 64 ||
            policy.HashBytes is < 16 or > 64 || policy.MaxConcurrentOperations is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(policy), "Password KDF policy exceeds safe bounds");
    }

    public static void ValidateNewPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        int bytes = Encoding.UTF8.GetByteCount(password);
        if (bytes is < 8 or > 256)
            throw new ArgumentException("Password must encode to 8..256 UTF-8 bytes", nameof(password));
    }

    public void Dispose() => _slots.Dispose();
}
