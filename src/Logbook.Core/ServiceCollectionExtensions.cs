// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.Applications;
using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.Applications.Delete;
using Logbook.Core.Application.Applications.Get;
using Logbook.Core.Application.Applications.GetAll;
using Logbook.Core.Application.Applications.IssueApiKey;
using Logbook.Core.Application.Applications.Rename;
using Logbook.Core.Application.Applications.RevokeApiKey;
using Logbook.Core.Application.LogEvents.AuthenticateApiKey;
using Logbook.Core.Application.LogEvents.Ingest;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.LogEvents.Tail;

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
        serviceCollection.AddTransient<ApiKeyIssuer>();

        serviceCollection.AddTransient<CreateApplicationService>();
        serviceCollection.AddTransient<DeleteApplicationService>();
        serviceCollection.AddTransient<GetAllApplicationsService>();
        serviceCollection.AddTransient<GetApplicationService>();
        serviceCollection.AddTransient<IssueApiKeyService>();
        serviceCollection.AddTransient<RenameApplicationService>();
        serviceCollection.AddTransient<RevokeApiKeyService>();

        serviceCollection.AddTransient<AuthenticateApiKeyService>();
        serviceCollection.AddTransient<IngestLogEventsService>();
        serviceCollection.AddTransient<SearchLogEventsService>();
        serviceCollection.AddTransient<TailLogEventsService>();

        return serviceCollection;
    }
}
