using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Gen;

/// <summary>
/// A reusable HTML/Scriban template that renders ERP or free-form data into a PDF (ARCHITECTURE.md
/// section 1.2: "HTML/Handlebars or DOCX templates rendered to PDF from ERP document data, pushed
/// straight into an archive cabinet, a workflow or a signing envelope"). This phase implements the
/// HTML/Scriban half only - DOCX templates are explicitly out of scope (see GeneratedDocument and
/// the Gen rendering pipeline for the disclosed HTML-subset PDF-rendering limitation).
/// </summary>
public class DocumentTemplate : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The Scriban template source (Handlebars-like/Liquid-like syntax) rendered to HTML.</summary>
    public string TemplateHtml { get; set; } = string.Empty;

    /// <summary>
    /// The B1 object code (ARCHITECTURE.md section 4.1, e.g. 22 = PurchaseOrder) this template is
    /// meant to render, when it targets one specific ERP document type. Null means the template is
    /// generic and accepts any data shape (not tied to a particular B1 object).
    /// </summary>
    public int? SourceObjectType { get; set; }

    /// <summary>
    /// A stored sample payload (JSON) used to preview the template without needing a live ERP
    /// connection - the same shape a real generation's InputDataJson would take.
    /// </summary>
    public string? SampleDataJson { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();
}
