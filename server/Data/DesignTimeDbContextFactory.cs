using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NexusDocs.Api.Data;

/// <summary>
/// Lets the EF Core CLI (`dotnet ef migrations add`, `dotnet ef database update`) construct
/// <see cref="NexusDocsDbContext"/> without running the full host/DI pipeline. Uses Sqlite so
/// migration authoring needs no external database; the running app supplies its own
/// provider/connection string via normal DI (Sqlite for dev, SQL Server for production).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NexusDocsDbContext>
{
    public NexusDocsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NexusDocsDbContext>()
            .UseSqlite("Data Source=nexusdocs.dev.db");

        return new NexusDocsDbContext(optionsBuilder.Options, new NullCurrentTenantAccessor());
    }
}
