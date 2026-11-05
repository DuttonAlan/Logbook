// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Api.Authentication;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks the rules for the admin key. Needs no database.
/// </summary>
public sealed class AdminApiOptionsValidatorTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("0123456789012345678901234567890", false)]
    [InlineData("01234567890123456789012345678901", true)]
    [InlineData("0123456789012345678901234567890 ", false)]
    [InlineData("0123456789012345678901234567890\n", false)]
    public void Validate_RequiresLongKeyWithoutWhitespace(string key, bool expected)
    {
        var result = new AdminApiOptionsValidator().Validate(name: null, new AdminApiOptions { Key = key });

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public void Validate_DoesNotRevealTheKey()
    {
        var result = new AdminApiOptionsValidator().Validate(name: null, new AdminApiOptions { Key = "short-secret" });

        Assert.DoesNotContain("short-secret", result.FailureMessage, StringComparison.Ordinal);
    }
}
