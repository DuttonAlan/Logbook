// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Reflection;
using System.Runtime.Versioning;

using Logbook.Extensions.Logging;

namespace Logbook.Sdk.Tests;

/// <summary>
///     Checks that the tests run against the intended build of the SDK, as they would otherwise prove nothing about it.
/// </summary>
public sealed class SdkBuildTests
{
    [Fact]
    public void Tests_RunAgainstTheIntendedBuildOfTheSdk()
    {
#if SDK_NETSTANDARD
        const string Expected = ".NETStandard,Version=v2.0";
#else
        const string Expected = ".NETCoreApp,Version=v10.0";
#endif

        Assert.Equal(Expected, typeof(LogbookClient).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
        Assert.Equal(Expected, typeof(LogbookLoggerProvider).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }
}
