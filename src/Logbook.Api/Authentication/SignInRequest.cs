// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using System.ComponentModel.DataAnnotations;

namespace Logbook.Api.Authentication;

/// <summary>
///     Represents the request of the local administrator to sign in.
/// </summary>
/// <param name="UserName" example="admin">The name from the setting <c>SignIn:Administrator:UserName</c>.</param>
/// <param name="Password">The password from the setting <c>SignIn:Administrator:Password</c>.</param>
public sealed record SignInRequest(
    [Required, StringLength(SignInRequest.MaxLength)] string UserName,
    [Required, StringLength(SignInRequest.MaxLength)] string Password)
{
    private const int MaxLength = 1024;
}
