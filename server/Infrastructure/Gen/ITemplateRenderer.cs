namespace NexusDocs.Api.Infrastructure.Gen;

/// <summary>
/// Renders an ERP-document-generation template (ARCHITECTURE.md section 1.2, module GEN) to a
/// finished PDF. <paramref name="templateHtml"/> is an HTML/Scriban template; <paramref name="data"/>
/// is merged into it (field access, loops, conditionals - see Scriban's Liquid-like syntax) to
/// produce a concrete HTML document, which is then rendered to PDF bytes.
///
/// Implementations are free to choose the templating engine and the HTML-to-PDF strategy; see
/// <see cref="ScribanTemplateRenderer"/> for the one actually wired up (Scriban + a disclosed
/// HTML-subset PdfSharp renderer), including exactly which HTML subset it supports.
/// </summary>
public interface ITemplateRenderer
{
    Task<byte[]> RenderToPdfAsync(string templateHtml, object data);
}
