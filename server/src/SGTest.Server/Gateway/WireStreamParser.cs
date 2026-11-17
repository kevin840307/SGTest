using System.Buffers.Binary;
using SGTest.Server.Wire;

namespace SGTest.Server.Gateway;

public enum WireFrameDisposition
{
    Accepted,
    AcceptedControl,
    Backpressured,
    ProtocolViolation
}

public enum WireFeedStatus
{
    Accepted,
    NeedMoreData,
    Backpressured,
    ProtocolViolation,
    BufferOverflow,
    Closed
}

public delegate WireFrameDisposition WireFrameHandler(ReadOnlySpan<byte> frame);

/// <summary>
/// Fixed-capacity byte-stream framing for one connection.
/// The socket adapter must stop reading when Backpressured is returned and resume
/// only after downstream capacity is available. Complete frames are not consumed
/// until the handler accepts them.
/// </summary>
public sealed class WireStreamParser
{
    private readonly byte[] _buffer;
    private int _count;
    private bool _faulted;

    public WireStreamParser(int maxBufferedBytes = WireProtocol.MaximumFrameBytes)
    {
        if (maxBufferedBytes < WireProtocol.MaximumFrameBytes ||
            maxBufferedBytes > WireProtocol.MaximumFrameBytes * 4)
            throw new ArgumentOutOfRangeException(nameof(maxBufferedBytes),
                "Parser capacity must hold at least one maximum protocol frame and remain bounded");
        _buffer = new byte[maxBufferedBytes];
    }

    public int BufferedBytes => _count;
    public int CapacityBytes => _buffer.Length;
    public bool IsFaulted => _faulted;

    public WireFeedStatus Feed(ReadOnlySpan<byte> bytes, WireFrameHandler handler,
                               out int acceptedFrames)
    {
        ArgumentNullException.ThrowIfNull(handler);
        acceptedFrames = 0;
        if (_faulted) return WireFeedStatus.ProtocolViolation;

        if (bytes.Length > _buffer.Length - _count)
        {
            _faulted = true;
            return WireFeedStatus.BufferOverflow;
        }

        bytes.CopyTo(_buffer.AsSpan(_count));
        _count += bytes.Length;

        while (true)
        {
            if (_count == 0) return WireFeedStatus.Accepted;
            if (_count < sizeof(int)) return WireFeedStatus.NeedMoreData;

            int advertised = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(0, sizeof(int)));
            if (advertised < WireProtocol.HeaderBytes ||
                advertised > WireProtocol.MaximumFrameBytes ||
                advertised > _buffer.Length)
            {
                _faulted = true;
                return WireFeedStatus.ProtocolViolation;
            }

            if (_count < advertised) return WireFeedStatus.NeedMoreData;

            var frame = _buffer.AsSpan(0, advertised);
            if (!WireProtocol.TryReadHeader(frame, out _))
            {
                _faulted = true;
                return WireFeedStatus.ProtocolViolation;
            }

            switch (handler(frame))
            {
                case WireFrameDisposition.Backpressured:
                    return WireFeedStatus.Backpressured;
                case WireFrameDisposition.ProtocolViolation:
                    _faulted = true;
                    return WireFeedStatus.ProtocolViolation;
                case WireFrameDisposition.Accepted:
                    Consume(advertised);
                    acceptedFrames++;
                    break;
                case WireFrameDisposition.AcceptedControl:
                    Consume(advertised);
                    break;
                default:
                    throw new InvalidOperationException("Unknown frame disposition");
            }
        }
    }

    public WireFeedStatus Resume(WireFrameHandler handler, out int acceptedFrames) =>
        Feed(ReadOnlySpan<byte>.Empty, handler, out acceptedFrames);

    private void Consume(int bytes)
    {
        int remaining = _count - bytes;
        if (remaining > 0)
            Buffer.BlockCopy(_buffer, bytes, _buffer, 0, remaining);
        _count = remaining;
    }
}
