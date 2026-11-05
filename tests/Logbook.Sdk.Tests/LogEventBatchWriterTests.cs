// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks the format of the request body and how events are split by size.
/// </summary>
public sealed class LogEventBatchWriterTests
{
    private const int NoLimit = int.MaxValue;

    private static readonly DateTimeOffset Timestamp = new(2026, 11, 4, 10, 15, 0, 123, TimeSpan.Zero);

    [Fact]
    public void Write_AllFields_InTheFormatOfTheLogApi()
    {
        var logEvent = new LogbookEvent(
            Timestamp,
            LogbookLevel.Error,
            "Order 42 failed",
            messageTemplate: "Order {OrderId} failed",
            category: "Shop.Orders",
            environment: "Production",
            eventId: 4001,
            exceptionJson: """{"type":"System.Exception","inner":null}""",
            traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
            spanId: "00f067aa0ba902b7",
            propertiesJson: """{"OrderId":42,"Nested":{"a":[1,2]}}""");

        var root = Decompress(LogEventBatchWriter.Write(Events(logEvent), NoLimit));

        var element = Assert.Single(root.EnumerateArray());
        Assert.Equal("2026-11-04T10:15:00.123+00:00", element.GetProperty("timestamp").GetString());
        Assert.Equal("Error", element.GetProperty("level").GetString());
        Assert.Equal("Order 42 failed", element.GetProperty("message").GetString());
        Assert.Equal("Order {OrderId} failed", element.GetProperty("messageTemplate").GetString());
        Assert.Equal("Shop.Orders", element.GetProperty("category").GetString());
        Assert.Equal("Production", element.GetProperty("environment").GetString());
        Assert.Equal(4001, element.GetProperty("eventId").GetInt32());
        Assert.Equal("System.Exception", element.GetProperty("exception").GetProperty("type").GetString());
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", element.GetProperty("traceId").GetString());
        Assert.Equal("00f067aa0ba902b7", element.GetProperty("spanId").GetString());
        Assert.Equal(42, element.GetProperty("properties").GetProperty("OrderId").GetInt32());
        Assert.Equal(2, element.GetProperty("properties").GetProperty("Nested").GetProperty("a")[1].GetInt32());
    }

    [Fact]
    public void Write_LeavesOutFieldsWithoutAValue()
    {
        var root = Decompress(LogEventBatchWriter.Write(Events(new LogbookEvent(Timestamp, LogbookLevel.Debug, null)), NoLimit));

        var element = Assert.Single(root.EnumerateArray());
        Assert.Equal(["timestamp", "level", "message"], element.EnumerateObject().Select(property => property.Name));
        Assert.Equal(string.Empty, element.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(LogbookLevel.Trace, "Trace")]
    [InlineData(LogbookLevel.Debug, "Debug")]
    [InlineData(LogbookLevel.Information, "Information")]
    [InlineData(LogbookLevel.Warning, "Warning")]
    [InlineData(LogbookLevel.Error, "Error")]
    [InlineData(LogbookLevel.Critical, "Critical")]
    [InlineData((LogbookLevel)99, "Information")]
    public void Write_Level_UsesTheNameTheServerAccepts(LogbookLevel level, string expected)
    {
        var root = Decompress(LogEventBatchWriter.Write(Events(new LogbookEvent(Timestamp, level, "x")), NoLimit));

        Assert.Equal(expected, root[0].GetProperty("level").GetString());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("42")]
    [InlineData("""{"a":1""")]
    [InlineData("""{"a":"\uD800"}""")]
    [InlineData("""{"a":[{"b":"\uDC00 text"}]}""")]
    [InlineData("""{"\uD800":1}""")]
    public void Write_AValueThatIsNotAJsonObject_IsLeftOut_ButTheEventIsKept(string json)
    {
        var logEvent = new LogbookEvent(Timestamp, LogbookLevel.Error, "kept", exceptionJson: json, propertiesJson: json);

        var root = Decompress(LogEventBatchWriter.Write(Events(logEvent), NoLimit));

        var element = Assert.Single(root.EnumerateArray());
        Assert.Equal("kept", element.GetProperty("message").GetString());
        Assert.False(element.TryGetProperty("exception", out _));
        Assert.False(element.TryGetProperty("properties", out _));
    }

    [Fact]
    public void Write_AnEscapedSurrogatePair_IsKept()
    {
        var logEvent = new LogbookEvent(Timestamp, LogbookLevel.Information, "x", propertiesJson: """{"a":"\uD83C\uDF89"}""");

        var root = Decompress(LogEventBatchWriter.Write(Events(logEvent), NoLimit));

        Assert.Equal("🎉", root[0].GetProperty("properties").GetProperty("a").GetString());
    }

    [Fact]
    public void Write_AnObjectNestedDeeperThanTheServerReads_IsLeftOut()
    {
        var accepted = NestedObject(29);
        var tooDeep = NestedObject(30);

        var root = Decompress(LogEventBatchWriter.Write(
            Events(
                new LogbookEvent(Timestamp, LogbookLevel.Error, "accepted", propertiesJson: accepted),
                new LogbookEvent(Timestamp, LogbookLevel.Error, "too deep", propertiesJson: tooDeep)),
            NoLimit));

        Assert.True(root[0].TryGetProperty("properties", out _));
        Assert.False(root[1].TryGetProperty("properties", out _));
    }

    [Fact]
    public void Write_TextWithSpecialCharacters_ArrivesUnchanged()
    {
        const string Message = "äöü 日本語 🎉 \"quoted\" back\\slash <tag> line\nbreak \u0001";

        var root = Decompress(LogEventBatchWriter.Write(Events(new LogbookEvent(Timestamp, LogbookLevel.Information, Message)), NoLimit));

        Assert.Equal(Message, root[0].GetProperty("message").GetString());
    }

    [Fact]
    public void Write_NonAsciiText_IsSentAsUtf8_SoItTakesLessOfTheRequestLimit()
    {
        var batch = LogEventBatchWriter.Write(Events(new LogbookEvent(Timestamp, LogbookLevel.Information, "äöü")), NoLimit);

        Assert.Contains("\"message\":\"äöü\"", DecompressToText(batch));
    }

    [Fact]
    public void Write_ALoneSurrogate_DoesNotFailTheBatch()
    {
        var batch = LogEventBatchWriter.Write(
            Events(
                new LogbookEvent(Timestamp, LogbookLevel.Information, "broken \uD800 text"),
                new LogbookEvent(Timestamp, LogbookLevel.Information, "fine")),
            NoLimit);

        var root = Decompress(batch);

        Assert.Equal(2, batch.Count);
        Assert.Equal("broken � text", root[0].GetProperty("message").GetString());
        Assert.Equal("fine", root[1].GetProperty("message").GetString());
    }

    [Fact]
    public void Write_ALoneSurrogateInAJsonObject_LeavesOutTheObject_ButKeepsTheEvent()
    {
        var logEvent = new LogbookEvent(Timestamp, LogbookLevel.Information, "x", propertiesJson: "{\"a\":\"\uD800\"}");

        var root = Decompress(LogEventBatchWriter.Write(Events(logEvent), NoLimit));

        Assert.Equal("x", root[0].GetProperty("message").GetString());
        Assert.False(root[0].TryGetProperty("properties", out _));
    }

    [Fact]
    public void Write_StopsBeforeTheRequestLimit_AndReportsTheLastEventItCovers()
    {
        var events = Events(Enumerable.Range(0, 10).Select(index => new LogbookEvent(Timestamp, LogbookLevel.Information, new string('x', 1000))).ToArray());

        var batch = LogEventBatchWriter.Write(events, 3500);

        var text = DecompressToText(batch);
        Assert.True(Encoding.UTF8.GetByteCount(text) <= 3500);
        Assert.Equal(3, batch.Count);
        Assert.Equal(3, JsonDocument.Parse(text).RootElement.GetArrayLength());
        Assert.Equal(events[2].Sequence, batch.LastSequence);
    }

    [Fact]
    public void Write_AnEventThatExactlyFillsTheRequest_IsKept()
    {
        var events = Events(new LogbookEvent(Timestamp, LogbookLevel.Information, "exact"));
        var size = Encoding.UTF8.GetByteCount(DecompressToText(LogEventBatchWriter.Write(events, NoLimit)));

        Assert.Equal(1, LogEventBatchWriter.Write(events, size).Count);
        Assert.Equal(0, LogEventBatchWriter.Write(events, size - 1).Count);
    }

    [Fact]
    public void Write_AFirstEventTooLargeForARequest_ReturnsNoEventsAndItsSequence()
    {
        var events = Events(new LogbookEvent(Timestamp, LogbookLevel.Information, new string('x', 1000)), new LogbookEvent(Timestamp, LogbookLevel.Information, "small"));

        var batch = LogEventBatchWriter.Write(events, 500);

        Assert.Equal(0, batch.Count);
        Assert.Equal(events[0].Sequence, batch.LastSequence);
    }

    private static string NestedObject(int depth)
    {
        return string.Concat(Enumerable.Repeat("""{"a":""", depth - 1)) + "{}" + new string('}', depth - 1);
    }

    private static BufferedEvent[] Events(params LogbookEvent[] logEvents)
    {
        return [.. logEvents.Select((logEvent, index) => new BufferedEvent(logEvent, index + 1, logEvent.GetMemoryBytes(), 0))];
    }

    private static JsonElement Decompress(LogEventBatch batch)
    {
        using var document = JsonDocument.Parse(DecompressToText(batch));

        return document.RootElement.Clone();
    }

    private static string DecompressToText(LogEventBatch batch)
    {
        using var gzip = new GZipStream(new MemoryStream(batch.Body), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}
