using System.Buffers;

namespace ConduitSharp.Core.Pipeline;

/// <summary>
/// Bounded write-through tee over a response body: forwards every byte to the stream underneath
/// while keeping a copy of the first <c>maxBytes</c> in a pooled buffer, and flags whether anything
/// was cut. A plugin that needs to read what the upstream sent swaps this in for
/// <c>HttpResponse.Body</c>, calls the rest of the pipeline, then reads <see cref="Captured"/>.
///
/// <para>Write-through rather than hold-back: the client keeps receiving bytes as they arrive, so a
/// plugin can inspect a response without turning a streaming reply into a buffered one. Swapping
/// <c>HttpResponse.Body</c> also reroutes <c>BodyWriter</c> through it, so YARP's forward is captured
/// too.</para>
///
/// <para>The buffer comes from <see cref="ArrayPool{T}"/>. Ownership has two exits: leave it to
/// <see cref="Dispose(bool)"/>, or take it with <see cref="DetachBuffer"/> when it has to outlive the
/// stream (handed to a background writer, say). Both are idempotent, because returning one array to
/// the pool twice corrupts every later renter and is far worse than leaking it once.</para>
/// </summary>
public sealed class BoundedTeeStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    private readonly int _max;
    private byte[]? _buffer;
    private int _length;

    /// <param name="inner">The stream every write is forwarded to.</param>
    /// <param name="maxBytes">Ceiling on what is kept. Writes past it still pass through, and set
    /// <see cref="Truncated"/>.</param>
    /// <param name="leaveOpen">Default true, because the stream handed in is normally
    /// <c>HttpResponse.Body</c>: the server owns it, and closing it here would cut off a response the
    /// client is still reading. Ownership is a parameter rather than a comment, the way
    /// <c>StreamWriter</c> and <c>GZipStream</c> state the same thing.</param>
    public BoundedTeeStream(Stream inner, int maxBytes, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        _inner = inner;
        _max = maxBytes;
        _leaveOpen = leaveOpen;
        _buffer = ArrayPool<byte>.Shared.Rent(maxBytes);
    }

    /// <summary>What was kept, up to <c>maxBytes</c>. Empty once the buffer has been detached or
    /// returned.</summary>
    public ReadOnlyMemory<byte> Captured => _buffer.AsMemory(0, _length);

    /// <summary>How many bytes were kept. Still readable after <see cref="DetachBuffer"/>, which is
    /// what a caller needs to hand the array on with its length.</summary>
    public int CapturedLength => _length;

    /// <summary>True when the body ran past <c>maxBytes</c> and the copy is a prefix.</summary>
    public bool Truncated { get; private set; }

    /// <summary>
    /// Hands the pooled array to the caller, who now owns returning it. The stream stops tracking it,
    /// so <see cref="Dispose(bool)"/> will not return it and <see cref="Captured"/> reads empty;
    /// <see cref="CapturedLength"/> and <see cref="Truncated"/> still describe what was written.
    /// Returns an empty array if the buffer is already gone.
    /// </summary>
    public byte[] DetachBuffer()
    {
        var buffer = _buffer;
        _buffer = null;
        return buffer ?? [];
    }

    /// <summary>Hands the pooled array back. Safe to call more than once, and safe to call after
    /// <see cref="DetachBuffer"/>, in which case it does nothing.</summary>
    public void ReturnBuffer()
    {
        if (_buffer is null)
            return;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = null;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReturnBuffer();
            if (!_leaveOpen)
                _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Capture(ReadOnlySpan<byte> data)
    {
        var take = Math.Min(_max - _length, data.Length);
        if (take > 0 && _buffer is not null)
        {
            data[..take].CopyTo(_buffer.AsSpan(_length));
            _length += take;
        }
        if (data.Length > take)
            Truncated = true;
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Capture(buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Capture(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override void Flush() => _inner.Flush();
    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override bool CanWrite => true;
    /// <inheritdoc/>
    public override bool CanRead => false;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
