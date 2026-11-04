// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

using Logbook.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Logbook.Api.IntegrationTests;

/// <summary>
///     Checks that the migrations match the model, so a changed configuration without a migration (or a wrong
///     hand-written snapshot) fails here instead of at startup. Needs no database.
/// </summary>
public sealed class MigrationTests
{
    [Fact]
    public void Model_HasNoPendingChanges()
    {
        var options = new DbContextOptionsBuilder<LogbookContext>().UseNpgsql("Host=unused").Options;

        using var context = new LogbookContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
