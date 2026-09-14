namespace NexusDocs.Api.Infrastructure.Licensing;

/// <summary>
/// Thrown by <see cref="LicenseService.AssertSeatAvailableAsync"/> when a tenant's active user
/// count for a module has reached (or exceeded) <c>TenantLicense.SeatsLicensed</c>. Callers that
/// want the standard 402 behaviour should let this bubble to a global exception handler / filter
/// rather than catching it locally.
/// </summary>
public class LicenseLimitExceededException : Exception
{
    public string ModuleCode { get; }
    public int SeatsLicensed { get; }
    public int SeatsUsed { get; }

    public LicenseLimitExceededException(string moduleCode, int seatsLicensed, int seatsUsed)
        : base($"Seat limit exceeded for module '{moduleCode}': {seatsUsed}/{seatsLicensed} seats in use.")
    {
        ModuleCode = moduleCode;
        SeatsLicensed = seatsLicensed;
        SeatsUsed = seatsUsed;
    }
}
