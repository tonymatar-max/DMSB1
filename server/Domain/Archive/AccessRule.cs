using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

public enum AccessRuleSubjectType
{
    User,
    Role,
}

[Flags]
public enum AccessRights
{
    Read = 1,
    Write = 2,
    Delete = 4,
    Approve = 8,
}

/// <summary>
/// Grants Rights to a subject (user or role) over a cabinet and/or document type, optionally narrowed
/// to documents whose ConditionFieldCode index value equals ConditionValue (e.g. Department = "HR").
/// </summary>
public class AccessRule : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public AccessRuleSubjectType SubjectType { get; set; }

    /// <summary>The User or Role id this rule applies to, depending on SubjectType.</summary>
    public Guid SubjectId { get; set; }

    public Guid? CabinetId { get; set; }
    public Guid? DocumentTypeId { get; set; }

    /// <summary>Index field code to condition on (e.g. "Department"); null means the rule applies unconditionally.</summary>
    public string? ConditionFieldCode { get; set; }

    /// <summary>Required value of ConditionFieldCode for the rule to apply (e.g. "HR").</summary>
    public string? ConditionValue { get; set; }

    public AccessRights Rights { get; set; }
}
