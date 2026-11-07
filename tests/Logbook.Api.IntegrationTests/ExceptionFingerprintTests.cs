// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Text.Json;

using Logbook.Core.Domain.ValueObjects;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks what makes two exceptions the same failure. Needs no database.
/// </summary>
public sealed class ExceptionFingerprintTests
{
    private const string Stack = """
           at Shop.Orders.OrderService.Place(Int32 id) in /src/Shop/OrderService.cs:line 42
           at Shop.Api.OrdersController.Post(OrderRequest request) in /src/Shop/OrdersController.cs:line 17
        """;

    [Fact]
    public void Compute_IgnoresTheMessage()
    {
        Assert.Equal(
            Fingerprint(new { type = "System.Exception", message = "Order 1 failed", stackTrace = Stack }),
            Fingerprint(new { type = "System.Exception", message = "Order 2 failed", stackTrace = Stack }));
    }

    [Fact]
    public void Compute_IgnoresFilesAndLinesOfAnotherBuild()
    {
        const string OtherBuild = """
               at Shop.Orders.OrderService.Place(Int32 id) in C:\agent\work\Shop\OrderService.cs:line 99
               at Shop.Api.OrdersController.Post(OrderRequest request) in C:\agent\work\Shop\OrdersController.cs:line 1
            """;

        Assert.Equal(
            Fingerprint(new { type = "System.Exception", stackTrace = Stack }),
            Fingerprint(new { type = "System.Exception", stackTrace = OtherBuild }));
    }

    [Fact]
    public void Compute_IgnoresTheLanguageOfTheStackTrace()
    {
        const string German = """
               bei Shop.Orders.OrderService.Place(Int32 id) in /src/Shop/OrderService.cs:Zeile 42.
               bei Shop.Api.OrdersController.Post(OrderRequest request) in /src/Shop/OrdersController.cs:Zeile 17.
            """;

        Assert.Equal(
            Fingerprint(new { type = "System.Exception", stackTrace = Stack }),
            Fingerprint(new { type = "System.Exception", stackTrace = German }));
    }

    [Fact]
    public void Compute_IgnoresTheNumbersTheCompilerAddsToNames()
    {
        const string First = """
               at Shop.OrderService.<Place>d__3.MoveNext()
               at Shop.OrderService.<>c__DisplayClass5_0.<Run>b__1(Int32 id)
               at Shop.OrderService.<Place>g__Local|3_0()
            --- End of stack trace from previous location ---
               at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
            """;

        const string Second = """
               at Shop.OrderService.<Place>d__17.MoveNext()
               at Shop.OrderService.<>c__DisplayClass9_2.<Run>b__4(Int32 id)
               at Shop.OrderService.<Place>g__Local|17_2()
            --- End of stack trace from previous location ---
               at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
            """;

        Assert.Equal(
            Fingerprint(new { type = "System.Exception", stackTrace = First }),
            Fingerprint(new { type = "System.Exception", stackTrace = Second }));
    }

    [Fact]
    public void Compute_SeparatesOtherTypes()
    {
        Assert.NotEqual(
            Fingerprint(new { type = "System.ArgumentException", stackTrace = Stack }),
            Fingerprint(new { type = "System.InvalidOperationException", stackTrace = Stack }));
    }

    [Fact]
    public void Compute_SeparatesOtherStacks()
    {
        Assert.NotEqual(
            Fingerprint(new { type = "System.Exception", stackTrace = Stack }),
            Fingerprint(new { type = "System.Exception", stackTrace = "   at Shop.Billing.Invoice.Send()" }));
    }

    [Fact]
    public void Compute_SeparatesOtherInnerTypes()
    {
        Assert.NotEqual(
            Fingerprint(new { type = "System.Exception", stackTrace = Stack, inner = new { type = "System.IO.IOException" } }),
            Fingerprint(new { type = "System.Exception", stackTrace = Stack, inner = new { type = "System.TimeoutException" } }));
    }

    [Fact]
    public void Compute_IgnoresTheOrderOfAggregatedExceptions()
    {
        Assert.Equal(
            Fingerprint(new { type = "System.AggregateException", innerExceptions = new[] { new { type = "A" }, new { type = "B" } } }),
            Fingerprint(new { type = "System.AggregateException", innerExceptions = new[] { new { type = "B" }, new { type = "A" } } }));
    }

    [Fact]
    public void Compute_HasTheLengthOfTheColumn()
    {
        Assert.Equal(ExceptionFingerprint.Length, Fingerprint(new { type = "System.Exception" })!.Length);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"message":"Only a message"}""")]
    [InlineData("""{"type":42,"stackTrace":["not text"]}""")]
    public void Compute_WithoutATypeOrAStack_IsNull(string json)
    {
        Assert.Null(ExceptionFingerprint.Compute(JsonDocument.Parse(json).RootElement));
    }

    [Fact]
    public void Compute_WithoutAnException_IsNull()
    {
        Assert.Null(ExceptionFingerprint.Compute(null));
    }

    private static string? Fingerprint(object exception)
    {
        return ExceptionFingerprint.Compute(JsonSerializer.SerializeToElement(exception));
    }
}
