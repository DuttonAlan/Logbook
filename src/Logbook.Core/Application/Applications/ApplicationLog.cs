// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Logbook.Core.Application.Applications;

/// <summary>
///     Log messages for changes to Applications. API Keys are never logged, only their Ids.
/// </summary>
internal static partial class ApplicationLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Application {ApplicationId} ({ApplicationName}) was created.")]
    public static partial void ApplicationCreated(this ILogger logger, Guid applicationId, string applicationName);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Application {ApplicationId} was renamed to {ApplicationName}.")]
    public static partial void ApplicationRenamed(this ILogger logger, Guid applicationId, string applicationName);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Application {ApplicationId} ({ApplicationName}) was deleted.")]
    public static partial void ApplicationDeleted(this ILogger logger, Guid applicationId, string applicationName);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "API Key {ApiKeyId} was issued for Application {ApplicationId}.")]
    public static partial void ApiKeyIssued(this ILogger logger, Guid applicationId, Guid apiKeyId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "API Key {ApiKeyId} of Application {ApplicationId} was revoked.")]
    public static partial void ApiKeyRevoked(this ILogger logger, Guid applicationId, Guid apiKeyId);
}
