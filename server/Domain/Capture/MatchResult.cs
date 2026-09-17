using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Capture;

/// <summary>Outcome of matching an extracted invoice against B1 PO/GRPO data (section 7 step 4-5).</summary>
public enum MatchOutcome
{
    CleanMatch = 0,
    Exception = 1,
}

/// <summary>
/// Result of the three-way (invoice/PO/GRPO) match for an <see cref="IngestItem"/>. A clean
/// match lets the item auto-file into ARCHIVE and post via ERP; an exception carries the human-
/// readable reasons (ARCHITECTURE.md section 7 step 5: price variance, quantity short, no PO,
/// unknown supplier, duplicate invoice number) and routes the item into FLOW instead.
/// </summary>
public class MatchResult : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid IngestItemId { get; set; }

    public MatchOutcome Outcome { get; set; }

    public string? MatchedCardCode { get; set; }

    public int? MatchedPoDocEntry { get; set; }

    public string? MatchedPoDocNum { get; set; }

    public int? MatchedGrpoDocEntry { get; set; }

    public string? MatchedGrpoDocNum { get; set; }

    /// <summary>
    /// Comma-or-newline-joined human-readable list of exception reasons, e.g.
    /// "No matching PO found", "Price variance 12% exceeds 5% tolerance", "Duplicate invoice number".
    /// </summary>
    public string? VarianceReasons { get; set; }

    public DateTimeOffset MatchedAt { get; set; } = DateTimeOffset.UtcNow;
}
