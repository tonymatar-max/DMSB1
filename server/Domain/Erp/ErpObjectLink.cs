using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Erp;

/// <summary>
/// The link key that ties an archived Nexus Docs document to an object in an ERP system.
/// See ARCHITECTURE.md section 4.1 ("The link key"): a single document can carry several of
/// these (an A/P invoice document can link to the invoice itself, its PO and its GRPO), which
/// is what makes the archive navigable from any side.
///
/// <see cref="ObjectType"/> uses SAP Business One's own object codes, so the link survives B1
/// upgrades and is immediately recognisable to any B1 consultant. Reference table (from
/// ARCHITECTURE.md section 4.1):
///
///   2   Business Partner      18   A/P Invoice
///   4   Item                  20   Goods Receipt PO
///   13  A/R Invoice           22   Purchase Order
///   15  Delivery              23   Sales Quotation
///   16  Return                171  Employee
///   17  Sales Order           —    UDO (by UDO code)
/// </summary>
public class ErpObjectLink : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>FK to the archived document this link belongs to (Archive.Document).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>FK to the ERP connection (and therefore CompanyDb) this link resolves against.</summary>
    public Guid ErpConnectionId { get; set; }

    /// <summary>SAP B1 object code. See the reference table in this file's summary.</summary>
    public int ObjectType { get; set; }

    /// <summary>The B1 object's internal key (DocEntry).</summary>
    public int DocEntry { get; set; }

    /// <summary>The B1 object's user-facing number (DocNum), where applicable.</summary>
    public string? DocNum { get; set; }

    /// <summary>Business partner code the object relates to, where applicable.</summary>
    public string? CardCode { get; set; }

    /// <summary>
    /// Denormalised human string (e.g. "PO 4711 - Al Sayer Trading"), refreshed on read, so
    /// the UI can render a list of links without B1 being reachable.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
