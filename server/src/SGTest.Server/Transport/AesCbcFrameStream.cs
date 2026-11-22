using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SGTest.Server.Transport;

/// <summary>
/// Deployment-configured, fixed AES-256 key and CBC IV shared with the Godot client.
/// WARNING: static IV reuse and unauthenticated CBC do not provide secure transport.
/// Do not use the key as proof of account or command identity.
/// </summary>
public sealed class AesCbcSettings
{
    public byte[] Key { get; }
    public byte[] Iv { get; }

    public AesCbcSettings(byte[] key, byte[] iv)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(iv);
        if (key.Length != 32 || iv.Length != 16)
            throw new ArgumentException("AES-256-CBC requires a 32-byte key and a 16-byte IV");
        Key = (byte[])key.Clone();
        Iv = (byte[])iv.Clone();
    }

    public static AesCbcSettings FromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable("SGTEST_AES_KEY_HEX");
        var iv = Environment.GetEnvironmentVariable("SGTEST_AES_IV_HEX");
        if (key?.Length != 64 || iv?.Length != 32)
            throw new ArgumentException("Set SGTEST_AES_KEY_HEX (64 hex digits) and SGTEST_AES_IV_HEX (32 hex digits)");
        try { return new AesCbcSettings(Convert.FromHexString(key), Convert.FromHexString(iv)); }
        catch (FormatException e) { throw new ArgumentException("AES configuration must be hexadecimal", e); }
    }
}

/// <summary>
/// One independent PKCS7-padded AES-CBC record per binary game frame:
/// uint32 little-endian ciphertext_length | ciphertext. Never sends IV on wire.
/// Presents decrypted frame bytes as a Stream to the existing auth and gateway.
/// The fixed-IV/unauthenticated design is a consciously accepted legacy tradeoff.
/// </summary>
public sealed class AesCbcFrameStream : Stream
{
    public const int MaxPlaintextBytes = 65536;
    public const int MaxCiphertextBytes = MaxPlaintextBytes + 16;
    private readonly Stream _inner;
    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private byte[] _pending = [];
    private int _pendingOffset;

    public AesCbcFrameStream(Stream inner, AesCbcSettings settings)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(settings);
        _key = (byte[])settings.Key.Clone();
        _iv = (byte[])settings.Iv.Clone();
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanWrite => _inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty) return 0;
        if (_pendingOffset == _pending.Length)
        {
            var header = new byte[4];
            int first = await _inner.ReadAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (first == 0) return 0;
            if (first < header.Length)
                await _inner.ReadExactlyAsync(header.AsMemory(first), cancellationToken).ConfigureAwait(false);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (length < 16 || length > MaxCiphertextBytes || length % 16 != 0)
                throw new IOException("Invalid encrypted record length");
            var cipher = new byte[(int)length];
            await _inner.ReadExactlyAsync(cipher, cancellationToken).ConfigureAwait(false);
            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                _pending = aes.DecryptCbc(cipher, _iv, PaddingMode.PKCS7);
                if (_pending.Length is < 1 or > MaxPlaintextBytes)
                    throw new IOException("Invalid encrypted record");
            }
            catch (CryptographicException)
            {
                // Do not give a remote peer details of the padding error.
                throw new IOException("Invalid encrypted record");
            }
            _pendingOffset = 0;
        }
        int n = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
        _pending.AsMemory(_pendingOffset, n).CopyTo(buffer);
        _pendingOffset += n;
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty || buffer.Length > MaxPlaintextBytes)
            throw new IOException("Invalid game frame size");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            var cipher = aes.EncryptCbc(buffer.Span, _iv, PaddingMode.PKCS7);
            var prefix = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)cipher.Length);
            await _inner.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
            await _inner.WriteAsync(cipher, cancellationToken).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writeGate.Dispose();
            CryptographicOperations.ZeroMemory(_key);
            CryptographicOperations.ZeroMemory(_iv);
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
