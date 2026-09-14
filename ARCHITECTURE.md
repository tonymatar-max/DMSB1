# Nexus Docs — Platform Architecture

A standalone, multi-tenant **document management, approval and e-signature platform** with SAP
Business One as its first-class connector. It is DocuWare's archive + workflow and DocuSign's
signing ceremony in one product, priced and deployed for SMB/mid-market ERP customers.

Architecturally it is a sibling of **Nexus Ops**: same shell (ASP.NET Core 8 + React 19 SPA,
shared-schema multi-tenancy, RSA-signed licence gating, transactional outbox, ERP adapters), so
patterns, infrastructure code and deployment tooling are reused rather than reinvented.

---

## 1. Product shape

### 1.1 Module catalogue

| Code      | Module                                      | Sold as   | Depends on |
|-----------|---------------------------------------------|-----------|------------|
| `CORE`    | Platform (tenants, users, roles, audit, storage, search) | always on | — |
| `ARCHIVE` | Document archive (cabinets, versions, index fields, retention) | licence | `CORE` |
| `FLOW`    | Approval workflow engine + designer          | licence   | `CORE` |
| `SIGN`    | E-signature (envelopes, ceremony, sealed PDF)| licence   | `CORE` |
| `CAPTURE` | Scan / email / OCR ingest + data extraction  | add-on    | `ARCHIVE` |
| `GEN`     | Template-driven document generation from ERP data | add-on | `CORE` |
| `ERP`     | ERP connector (SAP B1 first; D365, QuickBooks later) | add-on | any module |
| `AI`      | AI assist (classification, extraction, NL search, summarisation) | add-on | `ARCHIVE` |

Sub-features are gated by feature codes so an Essential / Professional / Enterprise ladder can be
sold without code changes: `ARCHIVE.RETENTION`, `ARCHIVE.WORM`, `ARCHIVE.REDACTION`,
`FLOW.DESIGNER`, `FLOW.SLA_ESCALATION`, `SIGN.QES`, `SIGN.BULK_SEND`,
`CAPTURE.AP_AUTOMATION`, `ERP.WRITEBACK`.

### 1.2 Module surface

**ARCHIVE** — Cabinets · document types with typed index-field schemas · immutable versioning ·
renditions (PDF/A, thumbnails, text layer) · annotations, stamps and redactions as overlays ·
metadata + full-text search with saved searches and result lists · retention schedules,
disposition review and legal hold · ERP object linking

**FLOW** — Visual route designer · sequential / parallel / conditional stages · routing by amount,
document type, cost centre, business partner, ERP field value · role-, user- and
manager-hierarchy assignment · delegation and out-of-office · SLA timers, reminders, escalation ·
re-route on reject with comment thread · full decision audit · optional ERP write-back on approval

**SIGN** — Envelopes with ordered or parallel recipients · signature, initial, date, text and
checkbox fields placed on the page · browser signing ceremony with OTP identity proofing and
consent capture · PAdES sealed PDF with RFC 3161 timestamp · certificate of completion ·
tamper-verification endpoint · in-person and bulk send

**CAPTURE** — Monitored email inboxes (Microsoft Graph / IMAP) · hot folders · desktop scanner
uploader (TWAIN/WIA) · mobile capture · OCR with searchable text layer · document classification ·
header and line-item extraction · ERP matching and three-way match with tolerance

**GEN** — HTML/Handlebars or DOCX templates rendered to PDF from ERP document data, pushed
straight into an archive cabinet, a workflow or a signing envelope.

### 1.3 The seams (why this is one product, not four tools)

- A document can be **archived, routed for approval, signed and pushed back to the ERP** without
  leaving the system or losing a single audit link — that continuous chain is the thing neither
  DocuWare nor DocuSign gives a B1 customer.
- **CAPTURE feeds FLOW**: a scanned A/P invoice is classified, extracted, matched against the B1
  purchase order and GRPO, and only the exceptions reach a human.
- **FLOW feeds SIGN**: internal approval completes, then the contract is released to the
  counterparty for signature; the signed seal returns to the same document record.
- **SIGN feeds ERP**: the sealed PDF and its hash are written into the B1 record's attachments, so
  B1 users see the executed document natively.
- Every module writes into the same **hash-chained audit log**, so one export answers an auditor's
  question about capture, approval and execution together.

---

## 2. Technical architecture

```
+---------------+  +-----------------+  +-------------------+  +--------------------+
|  React SPA    |  |  Signing portal |  |  B1 client add-on |  |  Desktop uploader  |
| (Vite/TS/TW)  |  |  (public token) |  |  (WebView2)       |  |  (scanner, TWAIN)  |
+-------+-------+  +--------+--------+  +---------+---------+  +----------+---------+
        |                   |                     |                       |
        +-------------------+----------+----------+-----------------------+
                                       |  HTTPS / JWT or signed ceremony token
                         +-------------v--------------+
                         |    ASP.NET Core 8 API      |
                         |  Tenant middleware         |
                         |  Licence filter            |
                         |  Permission + ACL handler  |
                         |  Controllers / domain svcs |
                         |  EF Core + global filters  |
                         |                            |
                         |  Workers:                  |
                         |    IngestWorker (OCR)      |
                         |    RenditionWorker         |
                         |    FlowTimerWorker (SLA)   |
                         |    SealWorker (PAdES)      |
                         |    RetentionWorker         |
                         |    IntegrationOutboxWorker |
                         +--+--------+--------+-------+
                            |        |        |
        +-------------------+   +----v----+   +------------------+
        |                       |         |                      |
  +-----v------+        +-------v------+ +-v----------+  +-------v--------+
  |  SQL DB    |        | Blob store   | | Search idx |  | ERP adapters   |
  | MSSQL /    |        | disk/S3/blob |  | FTS then   |  | SAP B1 SL +    |
  | Postgres   |        | + KMS + WORM |  | OpenSearch |  | read-only SQL  |
  +------------+        +--------------+ +------------+  +-------+--------+
                                                                 |
                                                        +--------v---------+
                                                        | Nexus B1 Gateway |
                                                        | on-prem agent,   |
                                                        | outbound WSS     |
                                                        +--------+---------+
                                                                 |
                                                        +--------v---------+
                                                        | SAP Business One |
                                                        | SL / DI / HANA   |
                                                        +------------------+
```

### 2.1 Multi-tenancy

Identical to Nexus Ops: shared database, shared schema. Every tenant-scoped entity implements
`ITenantScoped`; `NexusDocsDbContext` applies a global query filter per entity type and stamps
`TenantId` on insert. Tenant resolved by middleware from the JWT `tenant` claim (`X-Tenant`
header for service-to-service, sub-domain for sign-up). A tenant can be promoted to a dedicated
database later by returning a connection string instead of a filter value — no code change.

Blob keys are tenant-partitioned (`{tenantId}/{hashPrefix}/{hash}`), so storage isolation matches
database isolation.

### 2.2 Storage model

- **Content-addressed blobs.** A file is stored under its SHA-256; identical content across
  versions or cabinets is stored once per tenant boundary and referenced N times. Deduplication is
  a side effect — the real reason is that the hash *is* the integrity proof quoted in the audit log
  and the certificate of completion.
- `IBlobStore` implementations: `DiskBlobStore` (on-prem), `S3BlobStore`, `AzureBlobStore`.
- **Encryption at rest**: per-tenant data encryption key wrapped by a KMS/Key Vault master key
  (envelope encryption). On-prem falls back to a certificate-protected DEK.
- **WORM / immutability**: for cabinets with `ARCHIVE.WORM`, blobs are written to S3 Object Lock
  (compliance mode) or Azure immutable blob containers for the retention term. The database row can
  be superseded; the bytes cannot be rewritten.
- Nothing is ever hard-deleted while a `LegalHold` exists — disposition is a reviewed, audited act.

### 2.3 Search

Two indexes, one query surface:

- **Metadata**: index fields live on `DocumentIndex` as a JSON column plus persisted computed
  columns for the fields declared filterable — so `Invoice Total between X and Y` and
  `BP = C20000` are plain SQL with real indexes.
- **Full text**: the OCR/extracted text layer of each rendition is indexed with Postgres `tsvector`
  or MSSQL Full-Text to start. `ISearchProvider` allows swapping in OpenSearch when a tenant passes
  roughly a million documents or needs relevance tuning and Arabic analysers.

DocuWare-style **saved searches** and **result lists** are first-class objects, because that is how
document controllers actually work: they don't browse a tree, they re-run a query.

### 2.4 Licensing

`TenantLicense (TenantId, ModuleCode, Edition, SeatsLicensed, EnvelopeQuota, StorageQuotaGb,
ValidFrom, ValidTo, Status, Signature)`. `LicenseService` answers `IsModuleEnabled`,
`IsFeatureEnabled`, `AssertSeatAvailable`, `AssertEnvelopeQuota`, `GetEntitlements`.
Enforcement is server-side and mandatory (`[RequiresModule("SIGN")]` returns `402 Payment
Required` with a machine-readable body the SPA turns into an upgrade prompt). `Signature` is an
RSA signature over the licence payload so on-prem installs validate offline.

### 2.5 Security and compliance

- JWT access + refresh tokens; PBKDF2 password hashing; optional OIDC/SAML SSO (Entra ID first)
  and SCIM provisioning.
- **RBAC plus ABAC.** Permissions (`documents.read`, `flow.approve`, `sign.send`,
  `retention.dispose`) gate endpoints. Access to *content* is decided by `AccessRule` rows scoped
  to a cabinet or document type and optionally conditioned on index-field values
  (`Department = 'HR'`, `Owner = @me`) — an HR cabinet must not be readable by the finance approver
  who can approve its invoices.
- **Append-only, hash-chained audit.** `AuditEvent (Seq, TenantId, Actor, Action, Subject,
  PayloadJson, PrevHash, Hash)` where `Hash = SHA256(PrevHash || canonical(payload))`. A daily
  anchor hash is recorded and optionally timestamped by the TSA, so any retro-edit of history is
  detectable. This is what makes an approval or signature defensible rather than merely logged.
- Retention vs erasure conflict (a GDPR / GCC PDPL erasure request against a statutory retention
  term) is modelled explicitly: an erasure request against a held document produces a
  `DispositionConflict` for a human to resolve, never a silent delete.
- Signing keys never touch application disk — Azure Key Vault / Managed HSM, AWS KMS + CloudHSM,
  or a PKCS#11 HSM on-prem.

### 2.6 Background work

`Cronos`-driven `BackgroundService`s, same host pattern as Nexus Ops.

| Worker                     | Trigger | Purpose |
|----------------------------|---------|---------|
| `IngestWorker`             | queue   | Fetch from email/hot folder, de-dupe, OCR, classify, extract, create document |
| `RenditionWorker`          | queue   | PDF/A conversion, thumbnails, text layer, page count |
| `FlowTimerWorker`          | 1 min   | SLA breach, reminders, auto-escalation, auto-decision on timeout |
| `SealWorker`               | queue   | PAdES signing, TSA timestamp, certificate of completion assembly |
| `RetentionWorker`          | daily   | Advance retention states, raise disposition reviews, expire legal holds |
| `IntegrationOutboxWorker`  | 1 min   | Drain the outbox to the ERP with retry/back-off, record `ErpKey` |

Long or fragile work (OCR, PDF sealing) runs out-of-process behind a queue so the API never blocks
on a 40-page scan or an unreachable TSA.

---

## 3. Data model (core)

```
Tenant
  Cabinet ......................... a filing room; carries default ACL + retention
    DocumentType .................. index-field schema, naming rule, workflow binding
      IndexFieldDefinition ........ code, label, type, required, picklist/ERP lookup, filterable
    Document ...................... the record: type, cabinet, status, owner, current version
      DocumentVersion ............. immutable: blob hash, size, page count, author, comment
        Rendition ................. PDF/A | thumbnail | text layer -> blob hash
      DocumentIndex ............... typed + JSON metadata values (searchable)
      Annotation .................. overlay: note, stamp, highlight, redaction (never burned in)
      ErpObjectLink ............... link to one or many ERP objects
      RetentionState .............. policy, trigger date, disposition due, hold
  AccessRule ...................... subject x cabinet/type x condition x rights
  AuditEvent ...................... append-only hash chain

FLOW
  WorkflowDefinition -> StageDefinition -> RoutingRule / ApproverSpec
  WorkflowInstance -> StageInstance -> ApprovalTask -> Decision
  Delegation, EscalationRule, CommentThread

SIGN
  Envelope -> Recipient -> SignatureField
  CeremonyEvent (viewed, consented, OTP verified, signed, declined)
  Seal (signed blob hash, cert thumbprint, TSA token), CompletionCertificate

CAPTURE
  IngestSource -> IngestBatch -> IngestItem
  ClassificationResult, ExtractionResult, MatchResult

ERP
  ErpConnection (system, company DB, credentials ref, gateway id)
  ErpObjectRef, IntegrationOutbox
```

---

## 4. SAP Business One integration

This is the differentiator, so it is designed in detail rather than left as "connector".

### 4.1 The link key

```
ErpObjectRef (ConnectionId, CompanyDb, ObjectType, DocEntry, DocNum, CardCode, Label)
```

`ObjectType` uses B1's own object codes so the link survives upgrades and is recognisable to any
B1 consultant:

| Code | B1 object            | Code | B1 object              |
|------|----------------------|------|------------------------|
| 2    | Business Partner     | 18   | A/P Invoice            |
| 4    | Item                 | 20   | Goods Receipt PO       |
| 13   | A/R Invoice          | 22   | Purchase Order         |
| 15   | Delivery             | 23   | Sales Quotation        |
| 16   | Return               | 171  | Employee               |
| 17   | Sales Order          | —    | UDO (by UDO code)      |

A document may carry several links — an A/P invoice document links to the invoice, its PO and its
GRPO — which is what makes the archive navigable from any side. `Label` is a denormalised human
string (`PO 4711 — Al Sayer Trading`) refreshed on read, so the UI never needs B1 online to render
a list.

### 4.2 Reading from B1

Two channels, both read-only:

1. **Service Layer (OData v4)** for object reads and writes — the canonical, version-safe path.
2. **Read-only SQL** (MSSQL views or HANA calculation views) for list pickers, master-data caching
   and anything where Service Layer paging would be too slow. Never written to.

Master data (business partners, items, employees, projects, cost centres, dimensions, UDF
picklists) is cached into `ErpLookupCache` and refreshed incrementally, so index-field pickers and
approval routing rules resolve instantly and keep working while B1 is down for maintenance.

### 4.3 The Nexus B1 Gateway

Most GCC B1 estates are on-premise behind a firewall, and no customer will open inbound ports for a
SaaS. The **Nexus B1 Gateway** is a small Windows service that opens an *outbound* WebSocket (WSS)
tunnel to the cloud and proxies Service Layer and read-only SQL calls, with a credential vault
local to the customer site. Cloud tenants get B1 connectivity with zero inbound firewall change;
on-prem installs simply run the API and the gateway together.

This component is worth building once for the whole Nexus product line — Nexus Ops needs exactly
the same thing for its ERP connector.

### 4.4 Writing back to B1 — and the one honest constraint

B1's native **Approval Procedures fire before the document exists**: the user clicks Add, the
document is diverted into Drafts (`ODRF`/`OWDD`), and B1's own approval UI owns the decision. An
external system cannot "approve" a document B1 has not created. Pretending otherwise is where most
third-party B1 approval add-ons become fragile. So Nexus Docs supports three explicit patterns, and
the implementing consultant chooses one per document type:

**Pattern A — Nexus upstream (recommended).**
The request originates in Nexus: purchase requisition, contract, expense claim, vendor onboarding,
payment run, credit-limit change. Nexus runs the full approval and signature flow, and on final
approval **creates the B1 object via Service Layer** through the outbox. B1's own approval
procedure for that document type stays off. No interception, no race, no version sensitivity — and
the approval history lives where the attachments and signatures already are.

**Pattern B — Nexus alongside B1 drafts.**
B1's approval procedure fires and the document lands in Drafts. Nexus watches drafts, runs its
richer route (parallel stages, delegation, SLA, mobile approval, attachments), and on completion
either records the B1-side decision through the Service Layer `ApprovalRequests` /
`ApprovalRequestDecisions` endpoints, or posts the final document from the draft itself.
*Engineering note: the availability and behaviour of those Service Layer approval endpoints must be
validated against the specific B1 version and patch level before this pattern is committed to on a
project — treat it as a per-customer spike, not a given.*

**Pattern C — document control alongside an existing B1 record.**
No interception at all. The B1 document exists; what needs approving is the *file* — a signed
contract, a quality certificate, a bank guarantee, a payment authorisation. Nexus approves the
document, then stamps UDFs on the B1 record (`U_NX_DocStatus`, `U_NX_DocsUrl`, `U_NX_SignedOn`) so
B1 users see the state without leaving B1.

### 4.5 The attachment bridge

On final approval or signature completion, Nexus pushes the sealed PDF into B1's `Attachments2` via
Service Layer and sets the target document's `AttachmentEntry`, so a B1 user sees the executed
document natively in the client. The blob hash is recorded on both sides, so if someone replaces
the file in the B1 attachment folder the drift is detectable on the next verification pass.

This deliberately duplicates the bytes. The alternative — pointing B1's attachment path at a
Nexus-served UNC share — is cleaner in theory and unreliable in practice across clients, VPNs and
Citrix sessions. Storage is cheap; a B1 user not finding the contract is not.

### 4.6 Surfaces inside B1

- **B1 Windows client add-on (thin).** A small SDK add-on adds a *Nexus Docs* menu entry and a form
  button. It reads the active form's object type and `DocEntry`, then opens a WebView2 panel at a
  signed deep link (`/embed?conn=…&obj=22&entry=4711&t=<sso-ticket>`) showing that record's
  documents, approvals and signature status, with drag-and-drop filing. **No business logic lives
  in the add-on** — it is a launcher, so it needs almost no maintenance across B1 patches.
- **B1 Web Client.** Extensibility here is limited; rather than fight it, each record carries a
  `U_NX_DocsUrl` UDF holding a stable permalink, so the document set is one click from the record.
- **Nexus portal.** The primary UI: search and browse by B1 object, business partner or metadata;
  drop a file on a B1 record; approve from an inbox; send for signature.

---

## 5. Approval workflow engine

A workflow definition is versioned and immutable once instances exist; running instances keep the
definition version they started on.

- **Stages** run sequentially; within a stage approvers are `All`, `Any`, or `Quorum(n)`.
- **Approver resolution** at runtime: named user, role, group, manager-of-initiator (walks the HR
  hierarchy N levels), owner of the linked B1 object (sales employee, buyer, project manager), or
  an expression over index fields.
- **Routing rules** are declarative conditions over index fields, ERP field values and amounts,
  evaluated to pick the next stage — amount bands, cost-centre owners, doc-type-specific legal
  review, business-partner risk class.
- **Delegation** (explicit or out-of-office window) reassigns tasks and records both principal and
  delegate on the decision, which is what auditors ask for.
- **SLA**: per-stage due duration with reminder, escalation to a fallback approver, and optional
  auto-decision on timeout — used sparingly and always audited as a system decision.
- **Reject** returns the instance to the initiator or a nominated stage with a mandatory comment;
  the comment thread lives with the document, not in email.
- Approvals are actionable from the SPA inbox, from mobile web, and from a signed one-click email
  link with a confirmation step — never a bare approve-by-URL.

Every transition writes an `AuditEvent` and, where Pattern A or B applies, an `IntegrationOutbox`
row in the *same transaction* as the decision, so a successful approval can never fail to reach B1.

---

## 6. E-signature

### 6.1 Assurance levels

| Level | What it is | How Nexus delivers it |
|-------|------------|----------------------|
| SES | Simple electronic signature | Drawn/typed signature + email-verified identity + full audit trail |
| AES | Advanced | Adds OTP (SMS/email) identity proofing, optional ID-document check, signer identity cryptographically bound into a PAdES signature made with the tenant's HSM-held certificate |
| QES | Qualified | Delegated to a licensed trust service provider per country (UAE Pass and UAE TSPs, Saudi CAs with Nafath identity, Kuwait PACI) via a pluggable `IQualifiedSignatureProvider` |

Most SMB contract, HR and finance use cases are satisfied by AES. QES is the enterprise and
public-sector door-opener, and is built as an integration point rather than as core code.

*Legal note: the GCC states all recognise electronic signatures, with differing requirements for
which instruments need a qualified signature and which documents are excluded altogether.
Per-country claims in customer-facing material must be reviewed by local counsel before they are
made — this document does not establish them.*

### 6.2 The seal

Signing produces a **PAdES** signature (target: B-LTA — long-term validation with document
timestamp and embedded validation data) over a PDF/A-3b output, with:

- an RFC 3161 timestamp from a commercial TSA,
- the signing certificate held in Azure Key Vault Managed HSM / AWS CloudHSM / PKCS#11 HSM,
- a visible signature appearance per field (signer name, method, timestamp, envelope id),
- the machine-readable audit XML embedded as a PDF/A-3 attachment,
- a **certificate of completion** page appended: every recipient, every ceremony event with
  timestamp, IP, user agent and identity-proofing method, and the document hash at each stage.

A public `/verify` endpoint re-checks the signature, the chain and the recorded hash, so a
counterparty can validate without a Nexus account.

*Build decision to settle early: the PDF library. iText 8 is AGPL (a commercial licence is required
for this product), Apryse is capable and costly, and a BouncyCastle-based signer over an open PDF
library is cheapest but the most work for PDF/A-3 and incremental-update correctness. This is a
real line item, not a detail — price it before Phase 3 starts.*

### 6.3 The ceremony

Single-use, expiring, tokenised link (no account required) → identity proofing → explicit
consent-to-electronic-signature record → guided field-by-field completion → signature capture
(draw, type, upload, or adopt a stored one) → immediate sealed copy to every party. Declines,
abandonments and re-sends are all ceremony events. Sequential and parallel recipient routing,
CC/observer recipients, in-person signing, and bulk send from a list.

---

## 7. Capture, and the flagship scenario: A/P invoice automation

The single most sellable story for a B1 customer, and the reason `CAPTURE` exists:

1. A supplier emails a PDF invoice to `ap@customer.com`, monitored via Microsoft Graph.
2. `IngestWorker` de-dupes by content hash, OCRs (Azure Document Intelligence prebuilt-invoice in
   cloud; Tesseract with an Arabic + English model on-prem) and classifies the document.
3. Header and line extraction: supplier tax number, invoice number and date, currency, net, VAT,
   total, PO reference, line descriptions and quantities.
4. **ERP matching**: supplier resolved by tax ID or IBAN to a B1 `CardCode`; PO resolved by
   `DocNum`; then a **three-way match** across Purchase Order (22), Goods Receipt PO (20) and the
   invoice, with per-tenant quantity and price tolerances.
5. Clean matches route straight through; exceptions (price variance, quantity short, no PO, unknown
   supplier, duplicate invoice number) route into `FLOW` with the variance shown beside the image.
6. On final approval the outbox creates the B1 `PurchaseInvoices` document copied from the GRPO
   (`DocumentLines[].BaseType = 20`, `BaseEntry`, `BaseLine` — so B1 closes the base document and
   keeps the cost trail correct), attaches the source PDF, and links the archive document to the
   new invoice.

Measurable outcome for the customer: invoices touched once, matched automatically, with the image
one click from the B1 record forever. That is the demo that closes deals.

---

## 8. Deployment

| Model | Who it's for | Notes |
|-------|--------------|-------|
| Cloud multi-tenant SaaS | SMB, new logos | Nexus-hosted; B1 reached via the on-prem Gateway |
| Cloud single-tenant | Compliance-sensitive / data residency | Dedicated DB + storage account in-region |
| On-premise | Customers whose B1 and policy are on-prem (common in GCC) | Same binary, Docker Compose or Windows Service; offline RSA-signed licence |

One codebase, three topologies — enforced by keeping every external dependency behind an interface
(`IBlobStore`, `ISearchProvider`, `IOcrProvider`, `ISigningProvider`, `IKeyVault`, `IErpAdapter`)
with a cloud and an on-prem implementation of each.

---

## 9. Delivery roadmap

| Phase | Scope | Outcome |
|-------|-------|---------|
| 1 | `CORE` + `ARCHIVE` + B1 link + thin client add-on + attachment bridge | Sellable B1 document archive; replaces "attachments are a file path" |
| 2 | `FLOW` engine + designer + Pattern A (requisition → B1 posting) | Approvals with real routing; the first competitive wedge |
| 3 | `SIGN` — ceremony, PAdES seal, certificate of completion, verify endpoint | Removes the DocuSign line item from the customer's budget |
| 4 | `CAPTURE` + A/P automation with three-way match | The ROI story; highest-value module |
| 5 | `GEN` templates + `AI` add-on (classification, NL search, summarisation) | Differentiation, reusing the existing B1 AI agent work |

Phase 1 should be shipped to a friendly existing B1 customer before Phase 2 starts. The archive
alone is sellable, and the workflow engine is far easier to specify once real documents are in it.

---

## 10. Commercial shape

- `ARCHIVE`: per named user / month, with a storage tier (e.g. 100 GB included, then per 100 GB).
- `FLOW`: per named user / month; unlimited workflow runs — metering approvals punishes adoption.
- `SIGN`: bundled envelope allowance per year, overage per envelope; this is what undercuts
  DocuSign's per-seat-plus-envelope pricing.
- `CAPTURE`: per pages-scanned band, or per supplier-invoice volume.
- `ERP`: per connected B1 company database.
- On-prem: annual subscription including maintenance; no perpetual licence.

**Positioning.** DocuWare is capable but heavy, and its B1 connector is a separate, expensive
product. DocuSign signs but knows nothing about a purchase order. B1 natively stores an attachment
as a *file path* with no versioning, OCR, retention or metadata, and its approval procedures only
fire pre-add. Nexus Docs covers all four in one product, is priced for SMB, deploys on-premise when
the customer requires it, and is implemented by the same partner that runs their ERP.

---

## 11. Open decisions and risks

| # | Item | Why it matters | Action |
|---|------|----------------|--------|
| 1 | PDF library licence (iText AGPL vs Apryse vs BouncyCastle build) | Direct COGS and Phase 3 timeline | Price and decide before Phase 3 |
| 2 | B1 Service Layer `ApprovalRequests` behaviour by version | Decides whether Pattern B is offerable | One-week spike on a real 10.0 FP estate |
| 3 | QES provider per country (UAE Pass, Saudi CA, Kuwait PACI) | Enterprise and public-sector deals | Commercial conversation per country |
| 4 | Legal review of e-signature claims per GCC jurisdiction | Marketing and contractual exposure | Local counsel before any public claim |
| 5 | OCR accuracy commitment, especially mixed Arabic/English | Support cost and customer expectation | Benchmark on 200 real customer invoices |
| 6 | HANA vs MSSQL read-only SQL layer | Two SQL dialects to maintain | Keep Service-Layer-first; SQL only where measured |
| 7 | Retention vs erasure-request conflict policy | Regulatory exposure | Product decision, documented per tenant |
| 8 | Storage growth and cost model for on-prem WORM | Margin on large archives | Model before quoting large estates |

---

## 12. Repository layout (planned)

```
nexus-docs/
|- ARCHITECTURE.md          this document
|- server/                  ASP.NET Core 8 API
|  |- Domain/               Archive, Flow, Sign, Capture, Erp bounded contexts
|  |- Data/                 DbContext, migrations, seeding
|  |- Infrastructure/       tenancy, auth, licensing, audit chain, blob, search,
|  |                        ocr, signing, workers
|  |- Api/                  controllers
|  \- Models/               request/response DTOs
|- client/                  React 19 + Vite + Tailwind SPA (Nexus design system)
|- signing/                 public signing ceremony app
|- gateway/                 Nexus B1 Gateway (Windows service, outbound WSS)
|- addon-b1/                thin SAP B1 SDK add-on (launcher only)
|- uploader/                desktop scanner uploader
\- deploy/                  Docker Compose, IIS/Windows service, migrations
```
