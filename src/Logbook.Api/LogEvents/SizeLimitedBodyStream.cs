// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.LogEvents;

/// <summary>
///     Reads a request body and fails with a request error once it exceeds a size limit or turns out not to be valid gzip.
/// </summary>
/// <remarks>
///     The size of a body is not known in advance: it can be sent in chunks, and a small gzip body can expand to many
///     gigabytes, so the limit has to be checked while reading.
/// </remarks>
/// <param name="inner">The stream to read from.</param>
/// <param name="maxBytes">The maximum number of bytes that can be read.</param>
internal sealed class SizeLimitedBodyStream(Stream inner, long maxBytes) : Stream
{
    private long _bytesRead;

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            return Count(inner.Read(buffer, offset, count));
        }
        catch (InvalidDataException exception)
        {
            throw InvalidGzip(exception);
        }
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            return Count(await inner.ReadAsync(buffer, cancellationToken));
        }
        catch (InvalidDataException exception)
        {
            throw InvalidGzip(exception);
        }
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private static BadHttpRequestException InvalidGzip(InvalidDataException exception)
    {
        return new BadHttpRequestException("The request body is not valid gzip.", StatusCodes.Status400BadRequest, exception);
    }

    private int Count(int bytesRead)
    {
        _bytesRead += bytesRead;

        if (_bytesRead > maxBytes)
        {
            throw new BadHttpRequestException(
                $"The request body must not be larger than {maxBytes} bytes.",
                StatusCodes.Status413PayloadTooLarge);
        }

        return bytesRead;
    }
}
