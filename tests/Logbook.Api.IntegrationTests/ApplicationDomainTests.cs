// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Domain.Entities;
using Logbook.Core.Domain.Exceptions;
using Logbook.Core.Domain.ValueObjects;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks rules that depend on the current time, which requests cannot control. Needs no database.
/// </summary>
public sealed class ApplicationDomainTests
{
    private const string HashValue = "stored-hash-value";

    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ApiKeyHash Hash = ApiKeyHash.FromHashedValue(HashValue);

    [Fact]
    public void ApiKey_WithExpiry_IsActiveUntilItExpires()
    {
        var apiKey = Application.Create("Shop", Now).AddApiKey(Hash, Now, Now.AddDays(1));

        Assert.True(apiKey.IsActive(Now.AddHours(23)));
        Assert.False(apiKey.IsActive(Now.AddDays(1)));
    }

    [Fact]
    public void ApiKey_WithoutExpiry_StaysActiveUntilRevoked()
    {
        var application = Application.Create("Shop", Now);
        var apiKey = application.AddApiKey(Hash, Now, expiresAt: null);

        Assert.True(apiKey.IsActive(Now.AddYears(10)));

        application.RevokeApiKey(apiKey.Id, Now.AddYears(10));

        Assert.False(apiKey.IsActive(Now.AddYears(10)));
    }

    [Fact]
    public void AddApiKey_WithExpiryAtTheCurrentTime_Throws()
    {
        var application = Application.Create("Shop", Now);

        Assert.Throws<DomainValidationException>(() => application.AddApiKey(Hash, Now, Now));
    }

    [Fact]
    public void Create_WithLocalTime_IsRejectedAsABug()
    {
        var exception = Assert.Throws<ArgumentException>(() => Application.Create("Shop", DateTime.Now));

        Assert.IsNotType<DomainValidationException>(exception);
    }

    [Fact]
    public void ApiKeyHash_DoesNotExposeItsValueInToString()
    {
        Assert.DoesNotContain(HashValue, Hash.ToString(), StringComparison.Ordinal);
    }
}
