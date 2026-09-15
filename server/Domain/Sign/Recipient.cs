using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Sign;

public enum RecipientRole
{
    Signer,
    Cc,
}

public enum RecipientStatus
{
    Pending,
    Sent,
    Viewed,
    Consented,
    Signed,
    Declined,
}

/// <summary>
/// A recipient of an envelope. <see cref="SigningOrder"/> implements sequential/parallel routing
/// (ARCHITECTURE.md section 6.3): recipients sharing the same order sign in parallel; a lower order
/// must complete before a higher order can begin. <see cref="CeremonyToken"/> is the single-use,
/// expiring, tokenised link that lets this recipient reach the public (unauthenticated) ceremony
/// endpoints without an account.
/// </summary>
public class Recipient : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EnvelopeId { get; set; }

    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public RecipientRole Role { get; set; } = RecipientRole.Signer;

    /// <summary>Recipients with the same order sign in parallel; lower orders gate higher orders.</summary>
    public int SigningOrder { get; set; }

    public RecipientStatus Status { get; set; } = RecipientStatus.Pending;

    /// <summary>Unique, random, single-use token identifying this recipient's ceremony link.</summary>
    public string CeremonyToken { get; set; } = string.Empty;
    public DateTimeOffset TokenExpiresAt { get; set; }

    public DateTimeOffset? ViewedAt { get; set; }
    public DateTimeOffset? ConsentedAt { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public DateTimeOffset? DeclinedAt { get; set; }
    public string? DeclineReason { get; set; }
}
