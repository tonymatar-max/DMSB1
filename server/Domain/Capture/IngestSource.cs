using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Capture;

/// <summary>Which kind of mailbox/folder a monitored ingest source watches.</summary>
public enum IngestSourceKind
{
    HotFolder = 0,
    Imap = 1,
}

/// <summary>
/// A monitored inbound source for the A/P invoice capture pipeline (ARCHITECTURE.md section 7,
/// step 1 "capture"). Two kinds are supported: a local hot folder (watched with
/// FileSystemWatcher - fully real and testable in this environment) and an IMAP mailbox (real
/// MailKit-backed code, but not run against a live mailbox here - see
/// Infrastructure/Capture/ImapIngestPoller.cs for the disclosure).
/// </summary>
public class IngestSource : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public IngestSourceKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Local directory watched for dropped files. Only set when Kind = HotFolder.</summary>
    public string? HotFolderPath { get; set; }

    /// <summary>IMAP host. Only set when Kind = Imap.</summary>
    public string? ImapHost { get; set; }

    public int? ImapPort { get; set; }

    public bool ImapUseSsl { get; set; } = true;

    public string? ImapUsername { get; set; }

    /// <summary>
    /// Key into the existing ISecretStore holding the IMAP password/app-password - never the
    /// raw credential, same pattern as ErpConnection.CredentialsRef.
    /// </summary>
    public string? ImapCredentialsRef { get; set; }

    public string? ImapFolderName { get; set; } = "INBOX";

    /// <summary>Last time this source was polled (hot folder scan / IMAP UNSEEN check).</summary>
    public DateTimeOffset? LastPolledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
