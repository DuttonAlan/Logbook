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
        Assert.Null(options.Endpoint);
        Assert.Null(options.ApiKey);
    }
}
