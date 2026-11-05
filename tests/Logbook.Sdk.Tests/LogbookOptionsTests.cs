// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks the defaults of the client, which must never block or overload the host application.
/// </summary>
public sealed class LogbookOptionsTests
{
    [Fact]
    public void Defaults_AreBoundedAndBatched()
    {
        var options = new LogbookOptions();

        Assert.Equal(100, options.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(2), options.FlushInterval);
        Assert.Equal(10_000, options.MaxQueueSize);
        Assert.Equal(64L * 1024 * 1024, options.MaxQueueBytes);
        Assert.Equal(5 * 1024 * 1024, options.MaxRequestBytes);
        Assert.Equal(256 * 1024, options.MaxEventBytes);
        Assert.Equal(65_536, options.MaxMessageLength);
        Assert.Equal(TimeSpan.FromDays(7), options.MaxEventAge);
        Assert.Equal(TimeSpan.FromDays(1), options.MaxFutureOffset);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ShutdownTimeout);
        Assert.Null(options.Endpoint);
        Assert.Null(options.ApiKey);
    }

    [Theory]
    [InlineData(nameof(LogbookOptions.MaxEventBytes))]
    [InlineData(nameof(LogbookOptions.MaxMessageLength))]
    [InlineData(nameof(LogbookOptions.MaxEventAge))]
    [InlineData(nameof(LogbookOptions.MaxFutureOffset))]
    [InlineData(nameof(LogbookOptions.RequestTimeout))]
    [InlineData(nameof(LogbookOptions.ShutdownTimeout))]
    public void FindInvalidSetting_NamesTheSetting(string setting)
    {
        var options = new LogbookOptions { Endpoint = new Uri("https://example.com"), ApiKey = "key" };
        Assert.Null(options.FindInvalidSetting());

        switch (setting)
        {
            case nameof(LogbookOptions.MaxEventBytes):
                options.MaxEventBytes = 0;
                break;
            case nameof(LogbookOptions.MaxMessageLength):
                options.MaxMessageLength = 0;
                break;
            case nameof(LogbookOptions.MaxEventAge):
                options.MaxEventAge = TimeSpan.Zero;
                break;
            case nameof(LogbookOptions.MaxFutureOffset):
                options.MaxFutureOffset = TimeSpan.FromSeconds(-1);
                break;
            case nameof(LogbookOptions.RequestTimeout):
                options.RequestTimeout = TimeSpan.Zero;
                break;
            default:
                options.ShutdownTimeout = TimeSpan.FromSeconds(-1);
                break;
        }

        Assert.Equal(setting, options.FindInvalidSetting());
    }

    [Fact]
    public void ShutdownTimeout_OfZero_IsValid_AndMeansNotToWait()
    {
        var options = new LogbookOptions { Endpoint = new Uri("https://example.com"), ApiKey = "key", ShutdownTimeout = TimeSpan.Zero };

        Assert.Null(options.FindInvalidSetting());
    }
}
