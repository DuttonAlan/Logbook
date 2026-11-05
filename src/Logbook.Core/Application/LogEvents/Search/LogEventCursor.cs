// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Buffers.Binary;
using System.Buffers.Text;

namespace Logbook.Core.Application.LogEvents.Search;

/// <summary>
///     The position of a Log Event in the search results, which are ordered by <see cref="Timestamp"/> and then
///     <see cref="Id"/>. A page continues after the Log Event its cursor points to.
/// </summary>
/// <remarks>
///     A position instead of an offset keeps the pages stable while new Log Events arrive, and does not get slower with
///     every page. Senders must treat the encoded form as opaque, so it can change.
/// </remarks>
/// <param name="Timestamp">The point in time (UTC) of the Log Event, as stored in the database.</param>
/// <param name="Id">The Id of the Log Event, which separates Log Events with the same point in time.</param>
public sealed record LogEventCursor(DateTime Timestamp, Guid Id)
{
    private const int TicksLength = sizeof(long);
    private const int GuidLength = 16;

    /// <summary>
    ///     Encodes the cursor to pass it to a client.
    /// </summary>
    /// <returns>The encoded cursor, which is safe to use in a URL.</returns>
    public string Encode()
    {
        Span<byte> bytes = stackalloc byte[TicksLength + GuidLength];

        BinaryPrimitives.WriteInt64BigEndian(bytes, Timestamp.Ticks);
        Id.TryWriteBytes(bytes[TicksLength..], bigEndian: true, out _);

        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    ///     Decodes a cursor from a client.
    /// </summary>
    /// <param name="value">The encoded cursor.</param>
    /// <param name="cursor">The decoded cursor, if it is valid.</param>
    /// <returns><see langword="true"/> if the value is a valid cursor.</returns>
    public static bool TryDecode(string value, out LogEventCursor? cursor)
    {
        ArgumentNullException.ThrowIfNull(value);

        cursor = null;

        Span<byte> bytes = stackalloc byte[TicksLength + GuidLength];

        if (!Base64Url.TryDecodeFromChars(value, bytes, out var length) || length != bytes.Length)
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);

        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        cursor = new LogEventCursor(new DateTime(ticks, DateTimeKind.Utc), new Guid(bytes[TicksLength..], bigEndian: true));

        return true;
    }
}
