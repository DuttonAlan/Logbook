// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text;
using System.Text.Json;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks that events keep to the limits of the server, because one that does not costs its whole batch.
/// </summary>
public sealed class LogEventGuardTests
{
    private const int MaxEventBytes = 256 * 1024;
    private const int MaxMessageLength = 65_536;

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly LogEventLimits Limits = new(new LogbookOptions());

    [Fact]
    public void Check_AnOrdinaryEvent_ReturnsTheSameInstance()
    {
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, "Hello", messageTemplate: "Hello", category: "Shop", propertiesJson: """{"a":1}""");

        var result = LogEventGuard.Check(logEvent, Limits, Now, out var guarded);

        Assert.Equal(LogEventGuardResult.Accepted, result);
        Assert.Same(logEvent, guarded);
    }

    [Theory]
    [InlineData(-7 * 24 * 60 - 1, false)]
    [InlineData(-7 * 24 * 60, false)]
    [InlineData((-7 * 24 * 60) + 4, false)]
    [InlineData((-7 * 24 * 60) + 5, true)]
    [InlineData(0, true)]
    [InlineData((24 * 60) - 5, true)]
    [InlineData((24 * 60) - 4, false)]
    [InlineData(24 * 60, false)]
    public void Check_Timestamp_MustBeWithinTheWindow_WithAMarginForDifferentClocks(int minutesFromNow, bool accepted)
    {
        var logEvent = new LogbookEvent(Now.AddMinutes(minutesFromNow), LogbookLevel.Information, "x");

        Assert.Equal(accepted ? LogEventGuardResult.Accepted : LogEventGuardResult.Expired, LogEventGuard.Check(logEvent, Limits, Now, out _));
    }

    [Fact]
    public void Check_TheTimeWindow_ComesFromTheOptions()
    {
        var limits = new LogEventLimits(new LogbookOptions { MaxEventAge = TimeSpan.FromDays(30), MaxFutureOffset = TimeSpan.FromDays(10) });

        Assert.Equal(LogEventGuardResult.Accepted, LogEventGuard.Check(new LogbookEvent(Now.AddDays(-20), LogbookLevel.Information, "x"), limits, Now, out _));
        Assert.Equal(LogEventGuardResult.Accepted, LogEventGuard.Check(new LogbookEvent(Now.AddDays(5), LogbookLevel.Information, "x"), limits, Now, out _));
    }

    [Fact]
    public void Check_NoAllowedFuture_StillAdmitsACurrentEvent()
    {
        var limits = new LogEventLimits(new LogbookOptions { MaxFutureOffset = TimeSpan.Zero });

        Assert.Equal(LogEventGuardResult.Accepted, LogEventGuard.Check(new LogbookEvent(Now, LogbookLevel.Information, "x"), limits, Now, out _));
        Assert.Equal(LogEventGuardResult.Expired, LogEventGuard.Check(new LogbookEvent(Now.AddSeconds(1), LogbookLevel.Information, "x"), limits, Now, out _));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-29, true)]
    [InlineData(-31, false)]
    public void Check_AWindowShorterThanTheMargin_UsesHalfOfItAsTheMargin(int secondsFromNow, bool accepted)
    {
        var limits = new LogEventLimits(new LogbookOptions { MaxEventAge = TimeSpan.FromMinutes(1) });
        var logEvent = new LogbookEvent(Now.AddSeconds(secondsFromNow), LogbookLevel.Information, "x");

        Assert.Equal(accepted ? LogEventGuardResult.Accepted : LogEventGuardResult.Expired, LogEventGuard.Check(logEvent, limits, Now, out _));
    }

    [Fact]
    public void Check_NumbersTheServerStoresAsStrings_AreCountedWithTheirQuotes()
    {
        // About 210 KB as sent, and about 280 KB as the server stores it, which is over its limit of 256 KiB.
        var properties = "{\"a\":[" + string.Join(',', Enumerable.Repeat("1e999", 35_000)) + "]}";
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, "x", propertiesJson: properties);

        Assert.True(properties.Length < MaxEventBytes);
        Assert.Equal(LogEventGuardResult.TooLarge, LogEventGuard.Check(logEvent, Limits, Now, out _));
    }

    [Fact]
    public void Check_TextTheServerStoresEscaped_IsCountedEscaped()
    {
        // U+007F takes 1 byte as sent, and 6 bytes as the server stores it.
        var properties = "{\"a\":\"" + new string('\u007F', 85_000) + "\"}";
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, "x", propertiesJson: properties);

        Assert.Equal(LogEventGuardResult.Truncated, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));

        using var stored = JsonDocument.Parse(guarded!.PropertiesJson!);
        Assert.True(stored.RootElement.GetProperty("a").GetString()!.Length * 6 <= MaxEventBytes);
    }

    [Fact]
    public void Check_AMessageAtTheLimit_IsNotCut()
    {
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, new string('x', MaxMessageLength));

        Assert.Equal(LogEventGuardResult.Accepted, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));
        Assert.Equal(MaxMessageLength, guarded!.Message.Length);
        Assert.Null(guarded.PropertiesJson);
    }

    [Fact]
    public void Check_AMessageOverTheLimit_IsCutAndMarked_AndThePropertiesStay()
    {
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, new string('x', MaxMessageLength + 1), propertiesJson: """{"OrderId":42}""");

        Assert.Equal(LogEventGuardResult.Truncated, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));

        Assert.Equal(MaxMessageLength, guarded!.Message.Length);
        using var properties = JsonDocument.Parse(guarded.PropertiesJson!);
        Assert.True(properties.RootElement.GetProperty(LogEventGuard.TruncatedProperty).GetBoolean());
        Assert.Equal(42, properties.RootElement.GetProperty("OrderId").GetInt32());
    }

    [Fact]
    public void CutLongMessages_MarksTheEvent_AndCheckKeepsTheMark()
    {
        var cut = LogEventGuard.CutLongMessages(new LogbookEvent(Now, LogbookLevel.Information, new string('x', 10_000_000)), Limits);

        Assert.Equal(MaxMessageLength, cut.Message.Length);
        Assert.Equal(LogEventGuardResult.Truncated, LogEventGuard.Check(cut, Limits, Now, out var guarded));
        Assert.Contains(LogEventGuard.TruncatedProperty, guarded!.PropertiesJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CutLongMessages_AnOrdinaryEvent_ReturnsTheSameInstance()
    {
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, "Hello");

        Assert.Same(logEvent, LogEventGuard.CutLongMessages(logEvent, Limits));
    }

    [Fact]
    public void Check_ATextCutInTheMiddleOfASurrogatePair_RemainsValid()
    {
        var limits = new LogEventLimits(new LogbookOptions { MaxMessageLength = 101 });
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, new string('x', 100) + "🎉🎉");

        Assert.Equal(LogEventGuardResult.Truncated, LogEventGuard.Check(logEvent, limits, Now, out var guarded));

        Assert.Equal(new string('x', 100), guarded!.Message);
    }

    [Fact]
    public void Check_MultiByteTextOverTheByteLimit_IsCutUntilItFits()
    {
        var text = new string('日', MaxMessageLength);
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, text, messageTemplate: text);

        Assert.Equal(LogEventGuardResult.Truncated, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));

        Assert.True(ContentBytes(guarded!) <= MaxEventBytes);
        Assert.NotEmpty(guarded!.Message);
    }

    [Fact]
    public void Check_TextJustWithinTheByteLimit_IsNotCut_ThoughThePessimisticEstimateIsOverIt()
    {
        var text = new string('x', MaxMessageLength);
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, text, messageTemplate: text);

        Assert.Equal(LogEventGuardResult.Accepted, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));
        Assert.Equal(MaxMessageLength, guarded!.Message.Length);
    }

    [Fact]
    public void Check_TheNullCharacter_IsCountedAsTheServerCountsIt()
    {
        // The server replaces each U+0000 with U+FFFD, which takes three bytes, so this would be over its limit.
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, new string('\0', MaxMessageLength), messageTemplate: new string('\0', MaxMessageLength));

        Assert.Equal(LogEventGuardResult.Truncated, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));

        Assert.True(ContentBytes(guarded!) <= MaxEventBytes);
    }

    [Fact]
    public void Check_AHugeStringInAnException_IsCut_AndTheStructureAndShortTextStay()
    {
        var exception = JsonSerializer.Serialize(new
        {
            type = "System.Exception",
            stackTrace = new string('s', 1024 * 1024),
            inner = new { type = "System.IO.IOException", code = 7, handled = true, data = (string?)null, list = new[] { 1, 2 } }
        });
        var logEvent = new LogbookEvent(Now, LogbookLevel.Error, "Failed", exceptionJson: exception);

        Assert.Equal(LogEventGuardResult.Truncated, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));

        Assert.True(ContentBytes(guarded!) <= MaxEventBytes);
        Assert.Equal("Failed", guarded!.Message);

        using var stored = JsonDocument.Parse(guarded.ExceptionJson!);
        var root = stored.RootElement;
        Assert.Equal("System.Exception", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("stackTrace").GetString()!.Length > 1000);
        Assert.Equal("System.IO.IOException", root.GetProperty("inner").GetProperty("type").GetString());
        Assert.Equal(7, root.GetProperty("inner").GetProperty("code").GetInt32());
        Assert.True(root.GetProperty("inner").GetProperty("handled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("inner").GetProperty("data").ValueKind);
        Assert.Equal(2, root.GetProperty("inner").GetProperty("list").GetArrayLength());
    }

    [Fact]
    public void Check_EventWithoutProperties_GetsAnObjectJustForTheMark()
    {
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, "x", category: new string('c', 501));

        Assert.Equal(LogEventGuardResult.Truncated, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));

        Assert.Equal($"{{\"{LogEventGuard.TruncatedProperty}\":true}}", guarded!.PropertiesJson);
    }

    [Fact]
    public void Check_ThatStillDoesNotFitAfterCutting_IsTooLarge()
    {
        // Only short values, so there is nothing to cut.
        var properties = JsonSerializer.Serialize(Enumerable.Range(0, 50_000).ToDictionary(index => $"property{index}", index => index));
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, "x", propertiesJson: properties);

        Assert.Equal(LogEventGuardResult.TooLarge, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));
        Assert.Null(guarded);
    }

    [Theory]
    [InlineData("category", 500)]
    [InlineData("environment", 100)]
    [InlineData("traceId", 64)]
    [InlineData("spanId", 64)]
    public void Check_FieldsOverTheFixedLimitsOfTheServer_AreCutAndMarked(string field, int limit)
    {
        var atLimit = Event(field, new string('x', limit));
        var overLimit = Event(field, new string('x', limit + 1));

        Assert.Equal(LogEventGuardResult.Accepted, LogEventGuard.Check(atLimit, Limits, Now, out _));
        Assert.Equal(LogEventGuardResult.Truncated, LogEventGuard.Check(overLimit, Limits, Now, out var guarded));

        var value = field switch
        {
            "category" => guarded!.Category,
            "environment" => guarded!.Environment,
            "traceId" => guarded!.TraceId,
            _ => guarded!.SpanId
        };

        Assert.Equal(limit, value!.Length);
    }

    [Fact]
    public void Check_AValueThatIsNotAnObject_IsLeftOut_WithoutMarkingTheEvent()
    {
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, "x", exceptionJson: "not json", propertiesJson: "[1]");

        Assert.Equal(LogEventGuardResult.Accepted, LogEventGuard.Check(logEvent, Limits, Now, out var guarded));

        Assert.Null(guarded!.ExceptionJson);
        Assert.Null(guarded.PropertiesJson);
    }

    [Fact]
    public void Check_AGivenEvent_IsNotChanged()
    {
        var logEvent = new LogbookEvent(Now, LogbookLevel.Information, new string('x', 100_000));

        LogEventGuard.Check(logEvent, Limits, Now, out _);

        Assert.Equal(100_000, logEvent.Message.Length);
        Assert.False(logEvent.IsTruncated);
    }

    private static LogbookEvent Event(string field, string value)
    {
        return new LogbookEvent(
            Now,
            LogbookLevel.Information,
            "x",
            category: field == "category" ? value : null,
            environment: field == "environment" ? value : null,
            traceId: field == "traceId" ? value : null,
            spanId: field == "spanId" ? value : null);
    }

    private static long ContentBytes(LogbookEvent logEvent)
    {
        return (long)Encoding.UTF8.GetByteCount(logEvent.Message)
            + Encoding.UTF8.GetByteCount(logEvent.MessageTemplate ?? string.Empty)
            + Encoding.UTF8.GetByteCount(logEvent.ExceptionJson ?? string.Empty)
            + Encoding.UTF8.GetByteCount(logEvent.PropertiesJson ?? string.Empty);
    }
}
