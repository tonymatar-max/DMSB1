# Presales Engineer #2 â€” Deal-Cycle / RFP / Win-Loss Analysis: DMS B1 vs. Market

## 0. Critical framing correction first

I read `C:/claude/DMS B1/ARCHITECTURE.md` in full. It is **an architecture and product plan**, not a build-status report. There is no module in it marked "done," "stub," or "unverified" â€” every module is described in the future/normative tense ("A workflow definition is versioned...", "Signing produces a PAdES signature...", repo layout is explicitly labeled **"(planned)"**), the delivery roadmap (Â§9) lists Phases 1â€“5 as **not-yet-executed** work with Phase 1 described as what "should be shipped" next, and Â§11 lists eight **open decisions** (PDF library not chosen, B1 Service Layer `ApprovalRequests` behavior not validated, QES providers not contracted, e-signature legal claims not reviewed by counsel, OCR accuracy not benchmarked).

**This changes the answer materially.** The task asked me to assess whether we could sell this "today, in its current state" against EASY/DocuWare/ELO/M-Files in a real bake-off. Based on this document alone, there is no evidence any of CORE/ARCHIVE/FLOW/SIGN/CAPTURE has been coded, let alone demoed. If there is actually running code, a database, a working add-on, or a demo tenant somewhere, that lives outside this architecture doc and I have not seen it â€” treat everything below as conditioned on "if Phase 1 is real and running," and get that confirmed before using this analysis to greenlight a sales motion.

I flagged rather than guessed. Now, on to the market and deal-cycle work, which stands regardless of build status.

## 1. What the market actually looks like today

- **EASY Software (part.de is a reseller/implementation partner, not the vendor)** â€” mature DMS/ECM vendor with a dedicated SAP add-on line and pre-built SAP Business One archiving/retrieval integration; on-prem and cloud. This is a real, established competitor in DACH and increasingly MEA via partners.
- **DocuWare** â€” has a direct, productized SAP Business One connector (through partners like bob systemlÃ¶sungen / James Imaging's "Conduit Connector"), doing automatic archiving of AR/AP documents with metadata sync and workflow-driven posting back into B1. DocuWare is a $B-scale, Ricoh-owned platform with deep channel and marketing reach â€” a credible large-account competitor.
- **ELO Digital Office (ELO ECM)** â€” full SAP Business One integration ("ELO for SAP") allowing documents to be viewed/searched/filed from inside the B1 client, audit-proof archiving, workflow. Strong in Europe/DACH, has active B1-specific partners (an-group.one, be one solutions/ELO USA), and is expanding partner coverage â€” a serious "full ECM maturity" competitor for larger accounts.
- **M-Files** â€” has a **general SAP ArchiveLink connector and Extension Kit / M-Extender**, but I found **no evidence of a B1-specific connector** â€” its published integrations target SAP ECC/S/4HANA via ArchiveLink, not Business One's Service Layer. This is a real gap for M-Files in the B1 space specifically; it's a market player worth naming but a weaker direct B1 competitor than the other three.
- **Boyum IT** (b1.usability.package, Beas) â€” did not turn up evidence of a document-management/ECM product line; their strength is UX/manufacturing add-ons, not DMS. Not a direct DMS competitor, but worth checking with them directly since they're a channel-adjacent B1 ISV many of our prospects already run.
- **SAP native (do-nothing baseline)** â€” attachments as a bare file path, no versioning/OCR/retention, and Approval Procedures that fire only pre-Add (this is confirmed in our own architecture doc Â§4.4 and matches how SAP's approval workflow actually behaves) â€” this is the floor every vendor, including us, is selling above.
- **iPaaS (Boomi, Celigo)** â€” not itself a DMS story; relevant only as "how do we move documents/metadata between B1 and an external DMS," a minor integration-layer footnote, not a competing product.

## 2. What actually wins B1 DMS/e-sign RFPs and bake-offs

From RFP templates, AP-automation RFP guides, and vendor positioning I found:

1. **Native ERP integration depth is the #1 evaluation criterion** â€” not "can it archive PDFs" but pre-built connectors vs. custom API work; "integration challenges account for 67% of AP automation project delays" per one AP-automation RFP guide, so buyers actively probe this.
2. **Approval routing specificity** â€” buyers write RFP line items for approval tiers, dollar thresholds, department/category routing, escalation rules â€” i.e., exactly `FLOW`'s routing-rule and SLA-escalation design, not a generic "approvals" checkbox.
3. **AP automation ROI story with a measurable KPI** â€” "invoices touched once, matched automatically" is the right shape of pitch; buyers want automation-rate and cost-reduction numbers, which we cannot yet supply because CAPTURE is Phase 4 (last), unbuilt, and unbenchmarked (open item #5 in our own doc).
4. **E-signature legal bindingness by jurisdiction** â€” buyers and RFPs expect a vendor answer on legal enforceability (ESIGN/UETA-style assurance in Western RFPs; UAE Pass/Nafath/PACI-equivalent in GCC). Our own doc is honest that this is unreviewed by counsel (open item #4) â€” that's a hard blocker for any customer-facing claim, and it's an easy point of exposure if we skip it.
5. **Audit trail / compliance defensibility** â€” hash-chained, tamper-evident audit logs are a genuine differentiator when we have them; competitors also claim "audit-proof" archiving (ELO explicitly markets this), so it's table stakes, not a wedge, unless we can demonstrate the hash-chain concretely.
6. **Ease of use / fast time-to-value** drives adoption and is explicitly called out as a top RFP scoring factor â€” favors whichever vendor has a working, demoable product on the day of the bake-off, not the one with the better architecture document.

## 3. Could we credibly compete today?

Given the doc shows nothing built end-to-end yet (best available reading), the honest answer is: **not against EASY/DocuWare/ELO in a real bake-off today.** All three have shipped, referenceable B1 integrations with real customers, marketing pages, and partner networks; we have a design document and zero disclosed running demo. Walking into an RFP citing PAdES B-LTA sealing, three-way match, and hash-chained audit as if they exist would be a credibility risk the moment a prospect asks for a live demo or a reference customer.

**Where we could win, once Phase 1â€“2 are real:**
- The single-product, single-vendor "archive â†’ approve â†’ sign â†’ post back to B1" chain is a genuinely differentiated pitch none of the three named competitors make as cleanly (DocuWare and ELO are archive/workflow-first with signature usually bolted on or absent; none of them own e-signature the way DocuSign or we would).
- Being the customer's existing B1 implementation partner is a real, non-technical win condition for SME/lower-tier deals â€” one vendor, one contract, one support line, versus stitching DocuWare + DocuSign + a B1 partner's config work.
- SMB pricing model bundling three products (archive+workflow+signature) undercuts buying DocuWare and DocuSign separately.

**Where we would lose:**
- Reference customers and case studies â€” we have none; EASY/DocuWare/ELO all do.
- CAPTURE/AP-automation, the single highest-value, most sellable story per our own doc (Â§7) â€” is Phase 4, i.e., built last, and unbenchmarked.
- QES/legal signature assurance for GCC jurisdictions is an open commercial conversation, not a shipped capability (Â§11 item 3â€“4) â€” competitors with established regional TSP partnerships will out-answer us on this in any public-sector or large-enterprise deal.
- OCR accuracy on mixed Arabic/English invoices is unbenchmarked (Â§11 item 5) â€” a GCC-specific proof point we cannot yet substantiate, and Arabic OCR quality is exactly what a skeptical evaluator will test live.

## 4. Minimum work before a real competitive sales situation (not a demo)

1. Confirm and demo Phase 1 (CORE+ARCHIVE+B1 link+attachment bridge) live against a real B1 company DB â€” non-negotiable starting point.
2. Ship Phase 2 (FLOW with Pattern A) with at least one real routing scenario (amount-band + manager hierarchy) demoable end to end.
3. Get local counsel sign-off on e-signature assurance-level claims for at least the GCC country of the first prospect (open item #4) before SIGN is mentioned in any written proposal.
4. Benchmark OCR on a real invoice sample (open item #5) before quoting an AP-automation ROI number â€” do not repeat a vendor-marketing number as our own.
5. Decide the PDF signing library (open item #1) â€” this affects both cost and whether PAdES B-LTA is even a Phase-3 reality on the promised timeline.
6. Produce one real or friendly-customer reference before entering any formal RFP â€” buyers will ask, and EASY/DocuWare/ELO all have them.

## 5. Sell-alongside recommendation

Yes â€” this is the credible middle path. Lead with our own product for SME/lower-tier, price-sensitive B1 accounts once Phase 1â€“2 are real and demoable, where "one partner, one contract, archive+workflow+signature bundled" beats assembling DocuWare+DocuSign+integration work. For large/complex accounts demanding full ECM maturity (large-volume WORM archives, multi-country QES, proven AP-automation ROI at scale), resell/partner with DocuWare or ELO now rather than compete, and revisit direct competition once CAPTURE and QES are shipped and benchmarked, not before.

## Go/No-Go

- **Today: No-go for competitive sales situations.** We have a plan, not a shippable, referenceable product per the ARCHITECTURE.md I read. Selling it into a live bake-off against EASY/DocuWare/ELO today risks a credibility loss on the deal and on our own brand as a B1 partner.
- **In 6 months: Conditional go â€” SME/lower-tier only, and only if Phases 1â€“2 are actually built, demoed, and the counsel/OCR/PDF-library open items are closed.** Do not compete for large/complex accounts on our own product in this window; resell a market leader there instead.
- **Not-at-all: not the right call** â€” the "one vendor for archive+approval+signature+ERP posting" seam is a real differentiator no named competitor fully owns, worth finishing, just not worth selling before it exists.

Before this goes further: I'd get direct confirmation from engineering on whether Phase 1 has actually shipped code and a demo tenant anywhere outside this document â€” that single fact flips the whole recommendation from "build before you sell" to "here's what we can show this quarter."

Sources: [DocuWare SAP B1 Integration](https://start.docuware.com/de/sap-business-one-docuware-integration), [DocuWare Connect to SAP](https://start.docuware.com/connect-to-sap), [bob systemlÃ¶sungen DocuWare ECM Partner](https://www.bobsys.com/news/docuware-ecm-partner-1), [James Imaging SAP DocuWare Integration](https://www.jamesimaging.com/sap-docuware-integrations/), [EASY SAP Business One (part.de)](https://www.part.de/en/solutions/erp/sap-business-one), [EASY Software SAP add-ons](https://easy-software.com/en/sap/), [ELO SAP Business One Integration](https://www.elo.com/en-us/blog/seamless-digital-transformation-with-elo-sap-business-one-integration.html), [ELO Integration for SAP](https://www.elo.com/en-us/software/integration/sap.html), [an-group.one ELO Integration for SAP B1](https://www.an-group.one/en/elo-integration-for-sap-business-one-manage-documents-digitally/), [M-Files SAP Integration](https://www.m-files.com/m-files-platform/integrations/sap/), [M-Files Connector for SAP ArchiveLink](https://catalog.m-files.com/shop/m-files-connector-for-sap-archivelink/), [AP Automation RFP Template](https://www.sifthub.io/blog/ap-automation-rfp-template), [Corpay AP Automation RFP Top 17 Features](https://www.corpay.com/resources/blog/accounts-payable-request-for-proposal), [RFP.wiki Document Management Suppliers 2026](https://www.rfp.wiki/document-management), [Boyum IT SAP Business One](https://www.boyum-solutions.com/sap-business-one/)