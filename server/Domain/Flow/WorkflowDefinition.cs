using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

/// <summary>
/// Design-time definition of an approval workflow: an ordered set of <see cref="StageDefinition"/>
/// rows plus the routing rules between them (see <see cref="RoutingRule"/>).
/// </summary>
/// <remarks>
/// A workflow definition is versioned and immutable once instances exist. This entity does not
/// enforce that rule itself (the engine does): once any WorkflowInstance references this
/// definition's <see cref="BaseEntity.Id"/>, an edit must create a new WorkflowDefinition row with
/// <see cref="Version"/> + 1 and the same <see cref="WorkflowFamilyId"/>, rather than mutating this
/// row in place.
/// </remarks>
public class WorkflowDefinition : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Stays constant across versions of "the same" workflow; distinct from <see cref="BaseEntity.Id"/>,
    /// which identifies this specific version row.
    /// </summary>
    public Guid WorkflowFamilyId { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Optionally ties this workflow to an Archive DocumentType so it is triggered by that
    /// document type. Null when the workflow stands alone (e.g. requisitions with no archived
    /// document behind them).
    /// </summary>
    public Guid? DocumentTypeId { get; set; }

    public int Version { get; set; }

    public bool IsActive { get; set; }

    public ICollection<StageDefinition> Stages { get; set; } = new List<StageDefinition>();
}
