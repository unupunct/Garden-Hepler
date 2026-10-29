using Microsoft.EntityFrameworkCore;

namespace GardenHelper.Core.Data;

/// <summary>Creates short-lived contexts so services never share a context across threads.</summary>
public sealed class ContextFactory : IDbContextFactory<GardenDbContext>
{
    private readonly DbContextOptions<GardenDbContext> _options;
    public ContextFactory(AppPaths paths) => _options = DbFactory.Options(paths.DatabaseFile);
    public GardenDbContext CreateDbContext() => new(_options);
}
