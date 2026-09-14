using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NexusDocs.Api.Domain.Archive;
using NexusDocs.Api.Domain.Common;
using NexusDocs.Api.Domain.Erp;
using NexusDocs.Api.Domain.Platform;

namespace NexusDocs.Api.Data;

/// <summary>
/// The shared-schema, multi-tenant EF Core context for CORE + ARCHIVE (Phase 1). Every entity
/// implementing <see cref="ITenantScoped"/> is filtered to the current tenant automatically (see
/// <see cref="OnModelCreating"/>) and stamped with <see cref="ICurrentTenantAccessor.TenantId"/> on
/// insert (see <see cref="SaveChangesAsync"/>), so application code never reads or writes
/// TenantId itself. FLOW/SIGN/CAPTURE (Phase 2+) add their own DbSets and configuration here
/// without touching the tenancy plumbing below.
/// </summary>
public class NexusDocsDbContext(
    DbContextOptions<NexusDocsDbContext> options,
    ICurrentTenantAccessor tenantAccessor)
    : DbContext(options)
{
    public ICurrentTenantAccessor TenantAccessor { get; } = tenantAccessor;

    // Platform
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<TenantLicense> TenantLicenses => Set<TenantLicense>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // Archive
    public DbSet<Cabinet> Cabinets => Set<Cabinet>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<IndexFieldDefinition> IndexFieldDefinitions => Set<IndexFieldDefinition>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentIndexValue> DocumentIndexValues => Set<DocumentIndexValue>();
    public DbSet<Rendition> Renditions => Set<Rendition>();
    public DbSet<Annotation> Annotations => Set<Annotation>();
    public DbSet<AccessRule> AccessRules => Set<AccessRule>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();
    public DbSet<RetentionState> RetentionStates => Set<RetentionState>();

    // ERP (SAP B1) integration
    public DbSet<ErpConnection> ErpConnections => Set<ErpConnection>();
    public DbSet<ErpObjectLink> ErpObjectLinks => Set<ErpObjectLink>();
    public DbSet<ErpLookupCacheEntry> ErpLookupCacheEntries => Set<ErpLookupCacheEntry>();
    public DbSet<IntegrationOutbox> IntegrationOutbox => Set<IntegrationOutbox>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Search performance (ARCHITECTURE.md 2.3): lookups by field-across-tenant and by owning
        // document both happen on every document list/search request.
        b.Entity<DocumentIndexValue>().HasIndex(e => new { e.TenantId, e.FieldCode });
        b.Entity<DocumentIndexValue>().HasIndex(e => new { e.TenantId, e.DocumentId });

        // The hash chain is addressed by (tenant, seq); Seq must never collide within a tenant.
        b.Entity<AuditEvent>().HasIndex(e => new { e.TenantId, e.Seq }).IsUnique();

        // One account per email per tenant; the same email may belong to a user in another tenant.
        b.Entity<User>().HasIndex(e => new { e.TenantId, e.Email }).IsUnique();

        // Tenant.Slug identifies the tenant itself (subdomain/URL routing), so it is unique
        // across the whole install, not just within a tenant.
        b.Entity<Tenant>().HasIndex(e => e.Slug).IsUnique();

        foreach (var entityType in b.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;

            // Guid primary keys are set client-side (every entity's Id defaults to
            // Guid.NewGuid()); ValueGeneratedOnAdd keeps EF's own Guid generator as a fallback
            // for the rare case a caller leaves Id at Guid.Empty, without ever overwriting a
            // value the entity already set.
            var idProperty = entityType.FindProperty(nameof(BaseEntity.Id));
            if (idProperty is { ClrType.Name: nameof(Guid) })
            {
                idProperty.ValueGenerated = ValueGenerated.OnAdd;
            }

            if (!typeof(ITenantScoped).IsAssignableFrom(clr)) continue;

            // e => e.TenantId == TenantAccessor.TenantId
            var parameter = Expression.Parameter(clr, "e");
            var accessorProperty = Expression.Property(Expression.Constant(this), nameof(TenantAccessor));
            var currentTenantId = Expression.Property(accessorProperty, nameof(ICurrentTenantAccessor.TenantId));
            var body = Expression.Equal(
                Expression.Convert(Expression.Property(parameter, nameof(ITenantScoped.TenantId)), typeof(Guid?)),
                currentTenantId);

            b.Entity(clr).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    public override int SaveChanges()
    {
        StampTenantAndTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenantAndTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Stamps TenantId on new tenant-scoped entities from the ambient accessor and UpdatedAt on
    /// modified <see cref="BaseEntity"/> entities, so application code never sets either itself.
    /// </summary>
    private void StampTenantAndTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = TenantAccessor.TenantId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added
                && entry.Entity is ITenantScoped scoped
                && scoped.TenantId == Guid.Empty
                && tenantId is { } currentTenantId)
            {
                scoped.TenantId = currentTenantId;
            }

            if (entry.State == EntityState.Modified && entry.Entity is BaseEntity baseEntity)
            {
                baseEntity.UpdatedAt = now;
            }
        }
    }
}
