// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Sdk;

using Microsoft.Extensions.Logging;

namespace Logbook.Extensions.Logging;

/// <summary>
///     Creates the loggers that send to Logbook.
/// </summary>
/// <remarks>
///     It does not dispose the client, which belongs to the container and outlives the provider.
/// </remarks>
/// <param name="client">The client to send with.</param>
/// <param name="environment">The Environment of the events.</param>
/// <param name="maxTextLength">Maximum length of text in the structured values of an event.</param>
/// <param name="maxEventBytes">Maximum size of the content of an event.</param>
/// <param name="timeProvider">The clock for the timestamps.</param>
[ProviderAlias(ProviderName)]
internal sealed class LogbookLoggerProvider(
    LogbookClient client,
    string? environment,
    int maxTextLength,
    int maxEventBytes,
    TimeProvider timeProvider) : ILoggerProvider, ISupportExternalScope
{
    /// <summary>
    ///     The name of the provider in the section <c>Logging</c>.
    /// </summary>
    public const string ProviderName = "Logbook";

    // Sending a log event must not create log events. The SDK only reports through its EventSource and its own HttpClient
    // does not log, but this keeps a feedback loop out for good. The logs of the other HttpClients of the application are
    // excluded with it, as they cannot be told apart by category.
    private static readonly string[] ExcludedCategories =
    [
        "Logbook.Sdk",
        "Logbook.Extensions.Logging",
        "Logbook.AspNetCore",
        "System.Net.Http.HttpClient"
    ];

    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    /// <summary>
    ///     The client to send with.
    /// </summary>
    public LogbookClient Client { get; } = client;

    /// <summary>
    ///     The Environment of the events.
    /// </summary>
    public string? Environment { get; } = environment;

    /// <summary>
    ///     Maximum length of text in the structured values of an event.
    /// </summary>
    public int MaxTextLength { get; } = maxTextLength;

    /// <summary>
    ///     Maximum size of the content of an event.
    /// </summary>
    public int MaxEventBytes { get; } = maxEventBytes;

    /// <summary>
    ///     The clock for the timestamps.
    /// </summary>
    public TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>
    ///     The scopes of the application.
    /// </summary>
    public IExternalScopeProvider ScopeProvider => _scopeProvider;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        return new LogbookLogger(this, categoryName, Client.IsEnabled && !IsExcluded(categoryName));
    }

    /// <inheritdoc/>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private static bool IsExcluded(string categoryName)
    {
        foreach (var excluded in ExcludedCategories)
        {
            if (categoryName.StartsWith(excluded, StringComparison.Ordinal)
                && (categoryName.Length == excluded.Length || categoryName[excluded.Length] == '.'))
            {
                return true;
            }
        }

        return false;
    }
}
