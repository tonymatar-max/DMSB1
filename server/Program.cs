using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Platform;
using NexusDocs.Api.Infrastructure.Audit;
using NexusDocs.Api.Infrastructure.Auth;
using NexusDocs.Api.Infrastructure.Erp;
using NexusDocs.Api.Infrastructure.Files;
using NexusDocs.Api.Infrastructure.Flow;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Infrastructure.Secrets;
using NexusDocs.Api.Infrastructure.Tenancy;

const string DevCorsPolicy = "NexusDocsDevClient";

var builder = WebApplication.CreateBuilder(args);

// --- JWT signing key ------------------------------------------------------------------------
// JwtTokenService (which issues tokens) reads "Jwt:SigningKey" straight from IConfiguration, so
// whatever key we resolve here must be the *same* value it sees. If none is configured we
// generate a random one and write it back into the in-memory configuration so both this
// registration and JwtTokenService agree on it — this is a dev-only convenience so `dotnet run`
// works with no appsettings changes; a real deployment must set "Jwt:SigningKey" explicitly
// (tokens won't survive a restart otherwise, since the generated key is not persisted).
var signingKey = builder.Configuration["Jwt:SigningKey"];
var usingGeneratedSigningKey = string.IsNullOrWhiteSpace(signingKey);
if (usingGeneratedSigningKey)
{
    signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    builder.Configuration["Jwt:SigningKey"] = signingKey;
}

builder.Services.AddDbContext<NexusDocsDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=nexusdocs.dev.db";
    options.UseSqlite(connectionString);
});

// --- Tenancy ---------------------------------------------------------------------------------
// Two agents independently defined an ICurrentTenantAccessor: NexusDocsDbContext depends on the
// get-only NexusDocs.Api.Data one (query filters / tenant stamping), while
// TenantResolutionMiddleware and RequiresModuleAttribute depend on the settable
// NexusDocs.Api.Infrastructure.Tenancy one. Both need to reflect the *same* tenant for a given
// request, so we register one real (settable) CurrentTenantAccessor per scope and bridge the
// Data-namespace interface to it below, rather than maintaining two independent tenant values.
builder.Services.AddScoped<NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor, CurrentTenantAccessor>();
builder.Services.AddScoped<NexusDocs.Api.Data.ICurrentTenantAccessor>(sp =>
    new TenantAccessorBridge(sp.GetRequiredService<NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor>()));

// --- Auth / authorization ----------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey!)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<JwtTokenService>();

// --- Domain services ---------------------------------------------------------------------------
builder.Services.AddScoped<LicenseService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<WorkflowEngine>();
builder.Services.AddScoped<IBlobStore, DiskBlobStore>();
builder.Services.AddScoped<ISecretStore, DataProtectionSecretStore>();

// Typed HttpClient: registers both the HttpClient for SapB1ServiceLayerAdapter and
// IErpAdapter -> SapB1ServiceLayerAdapter in one call.
builder.Services.AddHttpClient<IErpAdapter, SapB1ServiceLayerAdapter>();

// --- Background workers -------------------------------------------------------------------------
builder.Services.AddHostedService<FlowTimerWorker>();
builder.Services.AddHostedService<IntegrationOutboxWorker>();

// --- MVC / Swagger / CORS -----------------------------------------------------------------------
// Every client page across both phases (Document.Status, ApprovalMode, WorkflowInstance.Status,
// etc.) assumes enums serialize as their string name ("Draft", "All", "Approved", ...) — that was
// never actually configured, so the API has been sending/expecting raw integers by default the
// whole time. String enums round-trip through JSON the same way in either direction, so this one
// config line fixes every enum field across the whole API at once.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Nexus Docs API", Version = "v1" });

    var bearerScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT access token from POST /api/auth/login, e.g. \"Bearer {token}\".",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    options.AddSecurityDefinition("Bearer", bearerScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearerScheme] = [] });
});

builder.Services.AddCors(options =>
{
    // Local Vite dev server only. A real deployment should read allowed origins from
    // configuration instead of hard-coding them.
    options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

if (usingGeneratedSigningKey)
{
    app.Logger.LogWarning(
        "Configuration value 'Jwt:SigningKey' was not set; generated a random dev-only signing " +
        "key for this run. Tokens issued now will fail validation after a restart. Set " +
        "Jwt:SigningKey in configuration for anything beyond local development.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // Only needed when the client runs on its own Vite dev server (a different origin). In
    // production the API serves the built SPA itself, so there is no cross-origin call to allow.
    app.UseCors(DevCorsPolicy);
}

// The published SPA is copied into wwwroot (see deploy/publish.ps1), so one service serves both
// the API and the app and there is no second web server to install or keep in step.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
// Must run after UseAuthentication (reads the "tenant" claim off HttpContext.User) and before
// UseAuthorization / MapControllers (RequiresModuleAttribute and the DbContext's query filters
// both need ICurrentTenantAccessor.TenantId already resolved).
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

// Client-side routes such as /cabinets/{id} are not files on disk; anything that is not an API
// call or a real static file falls through to the SPA so a refresh or a shared link works.
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.MapFallbackToFile("index.html");
}

// Applies pending migrations (creating the database on first run). Must run in every
// environment, not just Development, or a published build has no database at all.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NexusDocsDbContext>();
    db.Database.Migrate();

    if (app.Environment.IsDevelopment())
    {
        // IgnoreQueryFilters(): this runs with no ambient tenant (no HTTP request in flight), so
        // the normal tenant query filter would make Users.Any() look empty on every run and
        // re-seed a duplicate dev tenant/user each time the app starts.
        if (!db.Users.IgnoreQueryFilters().Any())
        {
            var devTenant = new Tenant { Name = "Nexus Docs Dev Tenant", Slug = "dev" };
            var (hash, salt) = PasswordHasher.Hash("ChangeMe123!");
            var devUser = new User
            {
                TenantId = devTenant.Id,
                Email = "admin@nexusdocs.dev",
                DisplayName = "Dev Admin",
                PasswordHash = hash,
                PasswordSalt = salt,
            };

            db.Tenants.Add(devTenant);
            db.Users.Add(devUser);

            // Without a TenantLicense row, every [RequiresModule(...)] endpoint 402s even in dev —
            // module gating is enforced unconditionally by design (ARCHITECTURE.md 2.4), so the dev
            // tenant needs the same entitlements a real customer would buy. Extend this list as new
            // modules ship; ValidTo far out so this never needs touching for local development.
            var farFuture = DateTimeOffset.UtcNow.AddYears(10);
            string[] devLicensedModules = ["CORE", "ARCHIVE", "FLOW", "ERP"];
            foreach (var moduleCode in devLicensedModules)
            {
                db.TenantLicenses.Add(new TenantLicense
                {
                    TenantId = devTenant.Id,
                    ModuleCode = moduleCode,
                    Edition = "Enterprise",
                    Status = TenantLicenseStatus.Active,
                    ValidFrom = DateTimeOffset.UtcNow,
                    ValidTo = farFuture,
                    Signature = "dev-unsigned",
                });
            }

            db.SaveChanges();

            app.Logger.LogWarning(
                "Seeded dev tenant {TenantSlug}, user {Email} (password: ChangeMe123!), and dev " +
                "licences for {Modules} — dev only.",
                devTenant.Slug, devUser.Email, string.Join(", ", devLicensedModules));
        }
    }
    // Production has no seeded user by design — a hardcoded password has no business existing
    // outside dev. There is no tenant/user provisioning flow yet; that is Phase 2+ scope. Until
    // then, standing up a real environment means inserting a first tenant/user by hand (or via
    // DesignTimeDbContextFactory + a one-off script) with a real password.
}

app.Run();

/// <summary>
/// Adapts the settable NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor (populated by
/// TenantResolutionMiddleware) to the get-only NexusDocs.Api.Data.ICurrentTenantAccessor that
/// NexusDocsDbContext (and DesignTimeDbContextFactory's NullCurrentTenantAccessor) depend on, so
/// both interfaces see the same per-request tenant without either agent's code changing.
/// </summary>
internal sealed class TenantAccessorBridge(NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor inner) : NexusDocs.Api.Data.ICurrentTenantAccessor
{
    public Guid? TenantId => inner.TenantId;
}
