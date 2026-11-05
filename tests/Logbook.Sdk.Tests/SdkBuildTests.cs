// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.Reflection;
using System.Runtime.Versioning;

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

        var attribute = typeof(LogbookClient).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.Equal(Expected, attribute?.FrameworkName);
    }
}
