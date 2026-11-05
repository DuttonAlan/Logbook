// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks what a call to <see cref="ILogger"/> turns into.
/// </summary>
public sealed partial class LogbookLoggerTests
{
    private enum Color
    {
        Red
    }

    [Theory]
    [InlineData(LogLevel.Trace, "Trace")]
    [InlineData(LogLevel.Debug, "Debug")]
    [InlineData(LogLevel.Information, "Information")]
    [InlineData(LogLevel.Warning, "Warning")]
    [InlineData(LogLevel.Error, "Error")]
    [InlineData(LogLevel.Critical, "Critical")]
    public void Level_IsMapped(LogLevel level, string expected)
    {
        var sent = LoggerHarness.SendOne(logger => logger.Log(level, "x"));

        Assert.Equal(expected, sent.GetProperty("level").GetString());
    }

    [Fact]
    public void Message_IsSentRendered_WithItsTemplateAndCategory()
    {
        var sent = LoggerHarness.SendOne(logger => logger.LogInformation("Order {OrderId} by {User}", 42, "abc"));

        Assert.Equal("Order 42 by abc", sent.GetProperty("message").GetString());
        Assert.Equal("Order {OrderId} by {User}", sent.GetProperty("messageTemplate").GetString());
        Assert.Equal("Test", sent.GetProperty("category").GetString());
        Assert.Equal(42, sent.GetProperty("properties").GetProperty("OrderId").GetInt32());
        Assert.Equal("abc", sent.GetProperty("properties").GetProperty("User").GetString());
        Assert.False(sent.GetProperty("properties").TryGetProperty("{OriginalFormat}", out _));
    }

    [Fact]
    public void AMessageWithoutValues_HasNoProperties()
    {
        var sent = LoggerHarness.SendOne(logger => logger.LogInformation("Plain"));

        Assert.Equal("Plain", sent.GetProperty("messageTemplate").GetString());
        Assert.False(sent.TryGetProperty("properties", out _));
    }

    [Fact]
    public void ASourceGeneratedMessage_IsStructuredToo()
    {
        var sent = LoggerHarness.SendOne(logger => LogOrderShipped(logger, 42));

        Assert.Equal("Order 42 shipped", sent.GetProperty("message").GetString());
        Assert.Equal("Order {OrderId} shipped", sent.GetProperty("messageTemplate").GetString());
        Assert.Equal(1234, sent.GetProperty("eventId").GetInt32());
        Assert.Equal(42, sent.GetProperty("properties").GetProperty("OrderId").GetInt32());
        Assert.Equal("OrderShipped", sent.GetProperty("properties").GetProperty("EventName").GetString());
    }

    [Fact]
    public void AStateWithoutValues_SendsOnlyTheFormattedMessage()
    {
        var sent = LoggerHarness.SendOne(logger =>
            logger.Log(LogLevel.Information, default, new object(), null, (_, _) => "From the formatter"));

        Assert.Equal("From the formatter", sent.GetProperty("message").GetString());
        Assert.False(sent.TryGetProperty("messageTemplate", out _));
        Assert.False(sent.TryGetProperty("properties", out _));
    }

    [Fact]
    public void EventId_IsSent_AndZeroIsLeftOut()
    {
        var events = LoggerHarness.Send(factory =>
        {
            var logger = factory.CreateLogger("Test");
            logger.LogInformation(new EventId(4001), "With");
            logger.LogInformation("Without");
        });

        Assert.Equal(4001, events[0].GetProperty("eventId").GetInt32());
        Assert.False(events[0].TryGetProperty("properties", out _));
        Assert.False(events[1].TryGetProperty("eventId", out _));
    }

    [Fact]
    public void TheNameOfAnEventId_DoesNotReplaceAValueOfTheApplication()
    {
        var sent = LoggerHarness.SendOne(logger => logger.LogInformation(new EventId(1, "FromId"), "{EventName}", "FromCall"));

        Assert.Equal("FromCall", sent.GetProperty("properties").GetProperty("EventName").GetString());
    }

    [Fact]
    public void Values_KeepTheirTypes()
    {
        var guid = Guid.NewGuid();
        var dateTime = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(1));

        var properties = SendValues(
            ("Text", "abc"),
            ("Flag", true),
            ("Int", 42),
            ("Short", (short)7),
            ("Long", long.MaxValue),
            ("Decimal", 1.5m),
            ("Double", 2.5d),
            ("Float", 3.5f),
            ("DateTime", dateTime),
            ("Guid", guid),
            ("Enum", Color.Red),
            ("Span", TimeSpan.FromMinutes(90)),
            ("Uri", new Uri("https://example.com/a")),
            ("Char", 'x'),
            ("Null", null));

        Assert.Equal("abc", properties.GetProperty("Text").GetString());
        Assert.True(properties.GetProperty("Flag").GetBoolean());
        Assert.Equal(42, properties.GetProperty("Int").GetInt32());
        Assert.Equal(7, properties.GetProperty("Short").GetInt32());
        Assert.Equal(long.MaxValue, properties.GetProperty("Long").GetInt64());
        Assert.Equal(1.5m, properties.GetProperty("Decimal").GetDecimal());
        Assert.Equal(2.5d, properties.GetProperty("Double").GetDouble());
        Assert.Equal(3.5f, properties.GetProperty("Float").GetSingle());
        Assert.Equal(dateTime, properties.GetProperty("DateTime").GetDateTimeOffset());
        Assert.Equal(guid, properties.GetProperty("Guid").GetGuid());
        Assert.Equal("Red", properties.GetProperty("Enum").GetString());
        Assert.Equal("01:30:00", properties.GetProperty("Span").GetString());
        Assert.Equal("https://example.com/a", properties.GetProperty("Uri").GetString());
        Assert.Equal("x", properties.GetProperty("Char").GetString());
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("Null").ValueKind);
    }

    [Fact]
    public void NumbersJsonCannotExpress_AreSentAsText()
    {
        var properties = SendValues(("NaN", double.NaN), ("Infinity", float.PositiveInfinity));

        Assert.Equal(JsonValueKind.String, properties.GetProperty("NaN").ValueKind);
        Assert.Equal(JsonValueKind.String, properties.GetProperty("Infinity").ValueKind);
    }

    [Fact]
    public void Numbers_AreIndependentOfTheCultureOfTheApplication()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            var properties = SendValues(("Nan", double.NaN), ("Other", new FormattableValue()));

            Assert.Equal("invariant", properties.GetProperty("Other").GetString());
            Assert.Equal("NaN", properties.GetProperty("Nan").GetString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Collections_BecomeArraysAndObjects()
    {
        var properties = SendValues(
            ("List", new[] { 1, 2 }),
            ("Dictionary", new Dictionary<string, object?> { ["a"] = 1, ["b"] = new[] { "x" } }),
            ("IntKeys", new Dictionary<int, string> { [7] = "seven" }));

        Assert.Equal(2, properties.GetProperty("List")[1].GetInt32());
        Assert.Equal(1, properties.GetProperty("Dictionary").GetProperty("a").GetInt32());
        Assert.Equal("x", properties.GetProperty("Dictionary").GetProperty("b")[0].GetString());
        Assert.Equal("seven", properties.GetProperty("IntKeys").GetProperty("7").GetString());
    }

    [Fact]
    public void ALongCollection_IsLimited_AndMarked()
    {
        var properties = SendValues(("Items", Enumerable.Range(0, 1000)));

        Assert.Equal(100, properties.GetProperty("Items").GetArrayLength());
        Assert.True(properties.GetProperty("logbook.truncated").GetBoolean());
    }

    [Fact]
    public void AnEndlessEnumerable_DoesNotHang()
    {
        var properties = SendStateValue("Items", Endless());

        Assert.Equal(100, properties.GetProperty("Items").GetArrayLength());

        static IEnumerable<int> Endless()
        {
            while (true)
            {
                yield return 1;
            }
        }
    }

    [Fact]
    public void DeeplyNestedCollections_BecomeText_InsteadOfLosingTheValue()
    {
        object nested = new[] { 1 };

        for (var level = 0; level < 50; level++)
        {
            nested = new[] { nested };
        }

        var properties = SendValues(("Nested", nested));

        Assert.Equal(JsonValueKind.Array, properties.GetProperty("Nested").ValueKind);
    }

    [Fact]
    public void OtherObjects_AreSentWithToString_AndNotThroughTheirGetters()
    {
        var value = new SecretHolder();

        var properties = SendValues(("Holder", value));

        Assert.Equal("a holder", properties.GetProperty("Holder").GetString());
        Assert.False(value.SecretWasRead);
    }

    [Fact]
    public void AValueThatThrows_CostsOnlyThatValue()
    {
        var sent = LoggerHarness.SendOne(logger =>
            logger.Log(
                LogLevel.Information,
                default,
                new List<KeyValuePair<string, object?>> { new("Bad", new ThrowingValue()), new("Good", 1) },
                null,
                (_, _) => "Message"));

        Assert.Equal("Message", sent.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("properties").GetProperty("Bad").ValueKind);
        Assert.Equal(1, sent.GetProperty("properties").GetProperty("Good").GetInt32());
    }

    [Fact]
    public void AnEnumerableThatThrows_KeepsWhatItGaveBeforeThat_AndTheJsonStaysValid()
    {
        var properties = SendStateValue("Items", ThrowsAfterOne());

        Assert.Equal(1, Assert.Single(properties.GetProperty("Items").EnumerateArray()).GetInt32());
        Assert.True(properties.GetProperty("logbook.truncated").GetBoolean());

        static IEnumerable<int> ThrowsAfterOne()
        {
            yield return 1;
            throw new InvalidOperationException("Enumeration failed");
        }
    }

    [Fact]
    public void AFormatterThatThrows_IsCaught_AndTheEventIsStillSent()
    {
        var sent = LoggerHarness.SendOne(logger =>
            logger.Log<object?>(LogLevel.Error, new EventId(5), null, null, (_, _) => throw new InvalidOperationException("Format failed")));

        Assert.Equal(string.Empty, sent.GetProperty("message").GetString());
        Assert.Equal(5, sent.GetProperty("eventId").GetInt32());
    }

    [Fact]
    public void LongTextInAValue_IsCutWhenLogged_AndMarked()
    {
        var sent = LoggerHarness.SendOne(
            logger => logger.LogInformation("{Text}", new string('x', 1000)),
            options => options.MaxMessageLength = 100);

        Assert.Equal(100, sent.GetProperty("properties").GetProperty("Text").GetString()!.Length);
        Assert.True(sent.GetProperty("properties").GetProperty("logbook.truncated").GetBoolean());
    }

    [Fact]
    public void TheMarkOfTheProvider_IsTheOneOfTheClient()
    {
        var sent = LoggerHarness.SendOne(
            logger => logger.LogInformation("{Text}", new string('x', 1000)),
            options => options.MaxMessageLength = 100);

        Assert.True(sent.GetProperty("properties").TryGetProperty(LogEventGuard.TruncatedProperty, out _));
    }

    [Fact]
    public void Scopes_WithValues_BecomeProperties()
    {
        var sent = LoggerHarness.SendOne(logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object?> { ["TenantId"] = 7 }))
            {
                logger.LogInformation("Hello");
            }
        });

        Assert.Equal(7, sent.GetProperty("properties").GetProperty("TenantId").GetInt32());
        Assert.False(sent.GetProperty("properties").TryGetProperty("Scopes", out _));
    }

    [Fact]
    public void AScopeWithATemplate_GivesItsValues_AndItsTextUnderScopes()
    {
        var sent = LoggerHarness.SendOne(logger =>
        {
            using (logger.BeginScope("Order {OrderId}", 42))
            {
                logger.LogInformation("Hello");
            }
        });

        var properties = sent.GetProperty("properties");
        Assert.Equal(42, properties.GetProperty("OrderId").GetInt32());
        Assert.Equal("Order 42", Assert.Single(properties.GetProperty("Scopes").EnumerateArray()).GetString());
    }

    [Fact]
    public void AScopeThatIsJustText_IsListedUnderScopes_OutermostFirst()
    {
        var sent = LoggerHarness.SendOne(logger =>
        {
            using (logger.BeginScope("Outer"))
            using (logger.BeginScope("Inner"))
            {
                logger.LogInformation("Hello");
            }
        });

        Assert.Equal(["Outer", "Inner"], sent.GetProperty("properties").GetProperty("Scopes").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void ValuesOfTheSameName_ThePrecedenceIsTheCall_ThenTheInnerScope_ThenTheOuterScope()
    {
        var sent = LoggerHarness.SendOne(logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object?> { ["A"] = "outer", ["B"] = "outer", ["C"] = "outer" }))
            using (logger.BeginScope(new Dictionary<string, object?> { ["A"] = "inner", ["B"] = "inner" }))
            {
                logger.LogInformation("{A}", "call");
            }
        });

        var properties = sent.GetProperty("properties");
        Assert.Equal("call", properties.GetProperty("A").GetString());
        Assert.Equal("inner", properties.GetProperty("B").GetString());
        Assert.Equal("outer", properties.GetProperty("C").GetString());
    }

    [Fact]
    public void AnEndedScope_IsNoLongerSent()
    {
        var events = LoggerHarness.Send(factory =>
        {
            var logger = factory.CreateLogger("Test");

            using (logger.BeginScope(new Dictionary<string, object?> { ["TenantId"] = 7 }))
            {
                logger.LogInformation("Inside");
            }

            logger.LogInformation("Outside");
        });

        Assert.True(events[0].TryGetProperty("properties", out _));
        Assert.False(events[1].TryGetProperty("properties", out _));
    }

    [Fact]
    public void AScopeThatThrows_IsLeftOut()
    {
        var sent = LoggerHarness.SendOne(logger =>
        {
            using (logger.BeginScope(new ThrowingValue()))
            {
                logger.LogInformation("Hello");
            }
        });

        Assert.Equal("Hello", sent.GetProperty("message").GetString());
    }

    [Fact]
    public void ValuesAreCapturedWhenLogged_NotWhenSent()
    {
        var list = new List<int> { 1 };

        var sent = LoggerHarness.SendOne(logger =>
        {
            logger.LogInformation("{Items}", list);
            list.Add(2);
        });

        Assert.Equal(1, sent.GetProperty("properties").GetProperty("Items").GetArrayLength());
    }

    [Fact]
    public void AnException_HasItsType_Message_StackTrace_AndInnerException()
    {
        var failure = Catch(() =>
        {
            try
            {
                throw new ArgumentException("Inner failure");
            }
            catch (ArgumentException inner)
            {
                throw new InvalidOperationException("Outer failure", inner);
            }
        });

        var sent = LoggerHarness.SendOne(logger => logger.LogError(failure, "Failed"));

        var exception = sent.GetProperty("exception");
        Assert.Equal("System.InvalidOperationException", exception.GetProperty("type").GetString());
        Assert.Equal("Outer failure", exception.GetProperty("message").GetString());
        Assert.Contains(nameof(LogbookLoggerTests), exception.GetProperty("stackTrace").GetString(), StringComparison.Ordinal);
        Assert.Equal("System.ArgumentException", exception.GetProperty("inner").GetProperty("type").GetString());
        Assert.Equal("Inner failure", exception.GetProperty("inner").GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, exception.GetProperty("inner").GetProperty("inner").ValueKind);
    }

    [Fact]
    public void AnExceptionThatWasNeverThrown_HasNoStackTrace()
    {
        var sent = LoggerHarness.SendOne(logger => logger.LogError(new InvalidOperationException("Not thrown"), "Failed"));

        Assert.Equal(JsonValueKind.Null, sent.GetProperty("exception").GetProperty("stackTrace").ValueKind);
    }

    [Fact]
    public void AnAggregateException_ListsAllItsInnerExceptions()
    {
        var failure = new AggregateException(
            new InvalidOperationException("First"),
            new AggregateException(new ArgumentException("Nested")));

        var sent = LoggerHarness.SendOne(logger => logger.LogError(failure, "Failed"));

        var inner = sent.GetProperty("exception").GetProperty("innerExceptions");
        Assert.Equal(2, inner.GetArrayLength());
        Assert.Equal("First", inner[0].GetProperty("message").GetString());
        Assert.Equal("Nested", inner[1].GetProperty("innerExceptions")[0].GetProperty("message").GetString());
        Assert.False(sent.GetProperty("exception").TryGetProperty("inner", out _));
    }

    [Fact]
    public void ADeepChainOfExceptions_IsLimited_AndMarked_AndTheServerCanStillReadIt()
    {
        Exception failure = new InvalidOperationException("Deepest");

        for (var level = 0; level < 100; level++)
        {
            failure = new InvalidOperationException($"Level {level}", failure);
        }

        var sent = LoggerHarness.SendOne(logger => logger.LogError(failure, "Failed"));

        var depth = 0;

        for (var current = sent.GetProperty("exception"); current.ValueKind == JsonValueKind.Object; current = current.GetProperty("inner"))
        {
            depth++;
        }

        Assert.Equal(8, depth);
        Assert.True(sent.GetProperty("properties").GetProperty("logbook.truncated").GetBoolean());
    }

    [Fact]
    public void AnAggregateExceptionWithVeryManyInnerExceptions_IsLimited_AndMarked()
    {
        var failure = new AggregateException(Enumerable.Range(0, 1000).Select(index => new InvalidOperationException($"Failure {index}")));

        var sent = LoggerHarness.SendOne(logger => logger.LogError(failure, "Failed"));

        Assert.Equal(31, sent.GetProperty("exception").GetProperty("innerExceptions").GetArrayLength());
        Assert.True(sent.GetProperty("properties").GetProperty("logbook.truncated").GetBoolean());
    }

    [Fact]
    public void AnExceptionWhoseMembersThrow_IsStillSentWithItsType()
    {
        var sent = LoggerHarness.SendOne(logger => logger.LogError(new ThrowingException(), "Failed"));

        var exception = sent.GetProperty("exception");
        Assert.Contains(nameof(ThrowingException), exception.GetProperty("type").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, exception.GetProperty("message").ValueKind);
        Assert.Equal(JsonValueKind.Null, exception.GetProperty("stackTrace").ValueKind);
        Assert.Equal("Failed", sent.GetProperty("message").GetString());
    }

    [Fact]
    public void ACurrentActivity_GivesTraceIdAndSpanId()
    {
        using var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();

        var sent = LoggerHarness.SendOne(logger => logger.LogInformation("Hello"));

        Assert.Equal(activity.TraceId.ToHexString(), sent.GetProperty("traceId").GetString());
        Assert.Equal(activity.SpanId.ToHexString(), sent.GetProperty("spanId").GetString());
    }

    [Fact]
    public void WithoutAnActivity_TraceIdAndSpanIdAreLeftOut()
    {
        var previous = Activity.Current;
        Activity.Current = null;

        try
        {
            var sent = LoggerHarness.SendOne(logger => logger.LogInformation("Hello"));

            Assert.False(sent.TryGetProperty("traceId", out _));
            Assert.False(sent.TryGetProperty("spanId", out _));
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void AHierarchicalActivity_HasNoW3CIdentifiersToSend()
    {
        using var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.Hierarchical).Start();

        var sent = LoggerHarness.SendOne(logger => logger.LogInformation("Hello"));

        Assert.False(sent.TryGetProperty("traceId", out _));
    }

    [LoggerMessage(EventId = 1234, EventName = "OrderShipped", Level = LogLevel.Information, Message = "Order {OrderId} shipped")]
    private static partial void LogOrderShipped(ILogger logger, int orderId);

    private static Exception Catch(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Nothing was thrown.");
    }

    private static JsonElement SendValues(params (string Name, object? Value)[] values)
    {
        var state = values.Select(value => new KeyValuePair<string, object?>(value.Name, value.Value)).ToList();

        var sent = LoggerHarness.SendOne(logger => logger.Log(LogLevel.Information, default, state, null, (_, _) => "Message"));

        return sent.GetProperty("properties");
    }

    // A state with the plain name, because the message formatter would enumerate these collections itself.
    private static JsonElement SendStateValue(string name, IEnumerable value) => SendValues((name, value));

    private sealed class FormattableValue : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            return ReferenceEquals(formatProvider, CultureInfo.InvariantCulture) ? "invariant" : "culture of the application";
        }
    }

    private sealed class SecretHolder
    {
        public bool SecretWasRead { get; private set; }

        public string Secret
        {
            get
            {
                SecretWasRead = true;
                return "secret";
            }
        }

        public override string ToString() => "a holder";
    }

    private sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("ToString failed");
    }

    private sealed class ThrowingException : Exception
    {
        public override string Message => throw new InvalidOperationException("Message failed");

        public override string? StackTrace => throw new InvalidOperationException("StackTrace failed");
    }
}
