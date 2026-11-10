// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Core.Application.Applications;
using Logbook.Core.Application.Applications.ChangeRetention;
using Logbook.Core.Application.Applications.Create;
using Logbook.Core.Application.Applications.Delete;
using Logbook.Core.Application.Applications.Get;
using Logbook.Core.Application.Applications.GetAll;
using Logbook.Core.Application.Applications.IssueApiKey;
using Logbook.Core.Application.Applications.Rename;
using Logbook.Core.Application.Applications.RevokeApiKey;
using Logbook.Core.Application.Dashboard.GetOverview;
using Logbook.Core.Application.ExceptionGroups.Search;
using Logbook.Core.Application.LogEvents.ApplyRetention;
using Logbook.Core.Application.LogEvents.AuthenticateApiKey;
using Logbook.Core.Application.LogEvents.Ingest;
using Logbook.Core.Application.LogEvents.Search;
using Logbook.Core.Application.LogEvents.Tail;
using Logbook.Core.Application.Metrics.GetNames;
using Logbook.Core.Application.Metrics.GetSeries;
using Logbook.Core.Application.Metrics.Ingest;
using Logbook.Core.Application.SavedSearches.Change;
using Logbook.Core.Application.SavedSearches.Create;
using Logbook.Core.Application.SavedSearches.Delete;
using Logbook.Core.Application.SavedSearches.GetAll;
using Logbook.Core.Application.Spans.Ingest;
using Logbook.Core.Application.Spans.Search;
using Logbook.Core.Application.Traces.Get;

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

        serviceCollection.AddTransient<ChangeRetentionService>();
        serviceCollection.AddTransient<CreateApplicationService>();
        serviceCollection.AddTransient<DeleteApplicationService>();
        serviceCollection.AddTransient<GetAllApplicationsService>();
        serviceCollection.AddTransient<GetApplicationService>();
        serviceCollection.AddTransient<IssueApiKeyService>();
        serviceCollection.AddTransient<RenameApplicationService>();
        serviceCollection.AddTransient<RevokeApiKeyService>();

        serviceCollection.AddTransient<ApplyRetentionService>();
        serviceCollection.AddTransient<AuthenticateApiKeyService>();
        serviceCollection.AddTransient<IngestLogEventsService>();
        serviceCollection.AddTransient<SearchLogEventsService>();
        serviceCollection.AddTransient<TailLogEventsService>();

        serviceCollection.AddTransient<IngestSpansService>();
        serviceCollection.AddTransient<SearchSpansService>();
        serviceCollection.AddTransient<GetTraceService>();
        serviceCollection.AddTransient<SearchExceptionGroupsService>();

        serviceCollection.AddTransient<IngestMetricsService>();
        serviceCollection.AddTransient<GetMetricNamesService>();
        serviceCollection.AddTransient<GetMetricSeriesService>();
        serviceCollection.AddTransient<GetOverviewService>();

        serviceCollection.AddTransient<ChangeSavedSearchService>();
        serviceCollection.AddTransient<CreateSavedSearchService>();
        serviceCollection.AddTransient<DeleteSavedSearchService>();
        serviceCollection.AddTransient<GetAllSavedSearchesService>();

        return serviceCollection;
    }
}
