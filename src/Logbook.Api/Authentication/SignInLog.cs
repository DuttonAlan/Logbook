// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Api.Authentication;

/// <summary>
///     Log messages of the sign-in. What was entered is never logged, as people type passwords into the wrong field.
/// </summary>
internal static partial class SignInLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "{UserName} signed in.")]
    public static partial void SignedIn(this ILogger logger, string userName);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "{UserName} signed out.")]
    public static partial void SignedOut(this ILogger logger, string? userName);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "A sign-in from IP address {IpAddress} failed.")]
    public static partial void SignInFailed(this ILogger logger, string ipAddress);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "The OpenID Connect user {Subject} signed in, but lacks the required claim, so access was denied.")]
    public static partial void OpenIdConnectSignInDenied(this ILogger logger, string subject);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Warning, Message = "A sign-in through OpenID Connect failed with {ExceptionType}.")]
    public static partial void OpenIdConnectSignInFailed(this ILogger logger, string exceptionType);
}
