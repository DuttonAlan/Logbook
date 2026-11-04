// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Logbook.Core;

/// <summary>
///     Defines Extension Methods for the IServiceCollection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Application Logic to the Service Collection.
    /// </summary>
    /// <remarks>
    ///     Every use case service in <c>Application/&lt;Area&gt;/&lt;UseCase&gt;/</c> must be registered here.
    /// </remarks>
    /// <param name="serviceCollection">The Service Collection to add the services to.</param>
    /// <returns>The Service Collection, for chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection serviceCollection)
    {
        return serviceCollection;
    }
}
