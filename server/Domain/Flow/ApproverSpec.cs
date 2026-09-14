using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

public enum ApproverKind
{
    NamedUser,
    Role,
    Group,
    ManagerOfInitiator,
    OwnerOfLinkedErpObject,
    FieldExpression
}

/// <summary>
/// Describes how to resolve one or more approvers for a <see cref="StageDefinition"/>. Only the
/// property relevant to <see cref="Kind"/> is expected to be set; the others stay null.
/// </summary>
public class ApproverSpec : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public Guid StageDefinitionId { get; set; }

    public ApproverKind Kind { get; set; }

    /// <summary>Only for <see cref="ApproverKind.NamedUser"/>.</summary>
    public Guid? NamedUserId { get; set; }

    /// <summary>Only for <see cref="ApproverKind.Role"/>.</summary>
    public Guid? RoleId { get; set; }

    /// <summary>
    /// Only for <see cref="ApproverKind.ManagerOfInitiator"/>: how many levels up the management
    /// hierarchy to walk from the initiator (e.g. 1 = direct manager).
    /// </summary>
    public int? ManagerHierarchyLevels { get; set; }

    /// <summary>
    /// Only for <see cref="ApproverKind.OwnerOfLinkedErpObject"/>: the ERP field that names the
    /// owner, e.g. "SalesEmployee" or "Buyer".
    /// </summary>
    public string? ErpOwnerField { get; set; }

    /// <summary>
    /// Only for <see cref="ApproverKind.FieldExpression"/>: a simple expression reference (e.g.
    /// "IndexField:CostCentreOwner") resolved by the engine at runtime. This entity only stores
    /// the shape; resolution is out of scope here.
    /// </summary>
    public string? FieldExpression { get; set; }
}
