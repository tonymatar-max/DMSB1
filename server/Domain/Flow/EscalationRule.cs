using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

/// <summary>What to do when a stage instance breaches its SLA.</summary>
public enum EscalationAction
{
    Remind = 0,
    EscalateToFallback = 1,
    AutoApprove = 2,
    AutoReject = 3,
}

/// <summary>
/// Defines what should happen if a stage's SLA is breached. <see cref="TriggerAfterHours"/> is a
/// redundant convenience copy of the owning stage definition's SlaHours at the time this rule was
/// created, kept simple for Phase 2 rather than always dereferencing the stage definition.
/// </summary>
public class EscalationRule : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>FK to the StageDefinition this rule applies to.</summary>
    public Guid StageDefinitionId { get; set; }

    public int TriggerAfterHours { get; set; }

    public EscalationAction Action { get; set; }
}
