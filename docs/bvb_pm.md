# PM Analysis â€” Nexus Docs (DMS B1): Build vs. Partner vs. Hybrid

## 0. Ground truth from ARCHITECTURE.md â€” what we actually have today

Important honesty check first: **ARCHITECTURE.md is a design document, not a build log.** It describes module shape, data model, integration patterns and a 5-phase roadmap in detail, but it contains **no "done/in-progress/stub" status markers, no test results, and no record of bugs found/fixed**. Treat every module as **designed, largely unbuilt** unless another artifact (code repo, sprint board) says otherwise â€” I found no such artifact in this task. Anyone selling "we have a DMS product" today would be selling a very detailed architecture, not a shipping product.

What the document itself flags as genuinely open (Section 11, verbatim risks):
1. **PDF library decision unmade** (iText AGPL vs. Apryse commercial vs. BouncyCastle custom build) â€” blocks real PAdES/AES signing (SIGN module, Phase 3).
2. **B1 Service Layer `ApprovalRequests` behavior unverified** by version/patch â€” Pattern B (alongside-B1-drafts) is not committed, needs a one-week spike.
3. QES per-country trust providers (UAE Pass, Saudi CA/Nafath, Kuwait PACI) â€” not built, "commercial conversation per country."
4. Legal review of e-signature claims per GCC jurisdiction â€” not done.
5. OCR accuracy â€” no benchmark run yet ("benchmark on 200 real customer invoices").
6. HANA vs. MSSQL read-only SQL â€” undecided.
7. Retention/erasure conflict policy â€” designed, not implemented/tested.
8. On-prem WORM storage cost model â€” not modeled.

Additional gaps beyond the document's own list, inferred from the roadmap table:
- **GEN module: not started** (Phase 5, last).
- **Nexus B1 Gateway** (the on-prem WSS tunnel that is the only viable connectivity path for GCC's mostly-on-prem B1 estates) is architected but the document gives no evidence it is built â€” and it's a **hard blocker** for every GCC deployment, not a nice-to-have.
- **Zero customer pilots** are referenced anywhere in the document.
- CORE + ARCHIVE (Phase 1) is the most fleshed-out on paper (multi-tenancy, blob store, licensing, audit chain all specified in real detail) â€” this is plausibly closest to buildable, but "specified in detail" â‰  "built and tested."

**Bottom line for the PM analysis: we are at architecture-complete, code-unknown/early stage**, not "feature-gap-away-from-selling." Every estimate below assumes we are building from this design, largely from scratch, with normal integration surprises on top of the honestly-flagged risks.

---

## 1. Remaining build-out cost/time (in-house path to "first paying customer ready")

Reading the roadmap literally, a **sellable-not-just-demo** bar requires at minimum Phases 1â€“3 (ARCHIVE + FLOW + SIGN) working end-to-end against a real B1 tenant, because Section 10's own positioning pitch ("Nexus Docs covers all four in one product") is what differentiates us â€” shipping only ARCHIVE is just "B1 attachments with search," not obviously worth a new subscription line against free B1 attachments.

Small team assumption: 1 tech lead, 2â€“3 backend/.NET engineers, 1 frontend, 1 QA/part-time â€” roughly 4.5â€“5.5 FTE.

| Phase | Scope | Person-weeks (small team, realistic incl. rework) | Notes |
|---|---|---|---|
| 1 | CORE + ARCHIVE + B1 link + thin add-on + attachment bridge | 14â€“18 pw | Reuses Nexus Ops shell/tenancy/licensing patterns â€” genuinely reduces this; still real work: content-addressed blob store, search indexing, ERP object linking, WebView2 add-on. |
| 1.5 | Nexus B1 Gateway (on-prem tunnel) | 5â€“8 pw | Not in any phase row explicitly but is a hard prerequisite for any GCC on-prem customer to connect at all. Security review (credential vault, WSS) adds time. |
| 2 | FLOW engine + designer + Pattern A | 10â€“14 pw | Workflow designers are notoriously underestimated â€” approver resolution, SLA timers, delegation, routing-rule UI. |
| â€” | Pattern B spike (per doc's own note) | 1 pw | Explicitly scoped as a spike; do not skip, it decides whether Pattern B is sellable at all. |
| 3 | SIGN â€” PDF library decision, PAdES/AES signing, ceremony, verify endpoint | 12â€“16 pw | The single riskiest phase: PAdES B-LTA + HSM key management + TSA integration is specialist cryptographic/PDF work, not routine CRUD. License decision alone (cost negotiation with Apryse, or building a BouncyCastle signer) can eat 2â€“3 weeks before code starts. |
| 4 | CAPTURE + OCR + 3-way match | 12â€“16 pw | OCR accuracy work against mixed Arabic/English invoices (flagged risk #5) is the long pole â€” benchmarking and tuning against 200 real invoices is not a one-sprint task. |
| â€” | First real customer pilot (Phase 1 candidate, per doc's own recommendation) | 4â€“6 pw elapsed, low direct effort but calendar-blocking | Bug-fixing against real B1 data, real edge cases in attachments/UDFs. |
| â€” | Legal/compliance (GCC e-signature review, per-country QES) | External cost, not team time | Budget separately; gates public signing claims. |

**Rough total to reach ARCHIVE+FLOW+SIGN sellable (Phases 1â€“3 + Gateway + pilot): ~60â€“75 person-weeks of engineering**, which at 5 FTE is **roughly 3â€“4 elapsed months** if nothing goes wrong â€” realistically **5â€“7 months** given GCC B1 version fragmentation, the PDF-library decision lag, and pilot iteration. CAPTURE (Phase 4, the "flagship ROI story" per the doc) adds another **3 months** on top if a paying customer needs the A/P automation story to sign (many will, since Section 7 calls it "the demo that closes deals").

**Realistic effort to a genuinely competitive, first-paying-customer-ready product (Phases 1â€“4): 12â€“18 person-months of engineering, roughly 7â€“10 months elapsed** with a small team, plus non-trivial legal/compliance spend that is not engineering time at all. GEN (Phase 5) can be deferred; it is explicitly last in the roadmap and not required to compete with DocuWare/EASY/ELO, none of which lead with template generation either.

---

## 2. Three-year TCO by path

Assumptions: blended fully-loaded engineer cost ~$70â€“90k/yr (GCC delivery blend), Seidor's existing B1 practice absorbs project delivery cost regardless of path (that's a wash), so TCO below is the **incremental** cost of the DMS decision itself.

### (a) Continue building in-house
- Year 1: 12â€“18 person-months build-out (~$140kâ€“$220k loaded) + legal/QES setup (~$30â€“50k) + infra (KMS/HSM, TSA, cloud) (~$20â€“40k/yr) â‰ˆ **$220kâ€“$300k**
- Year 2: ongoing 2â€“3 FTE for CAPTURE hardening, GEN, AI module, bug fixing, first 3â€“5 customer pilots, per-country QES additions â‰ˆ **$250kâ€“$350k**
- Year 3: steady-state 2 FTE product maintenance + roadmap â‰ˆ **$180kâ€“$250k**
- **3-yr total: ~$650kâ€“$900k**, but with 100% IP ownership, no royalty/margin leakage, and a second product line (resellable to other GCC B1 partners, matching our existing pattern with B1 Integrator/AI agents/Nexus Ops).
- Revenue offset: none guaranteed â€” this is speculative product investment until first paying customer, unlike (b)/(c) which can bill from month 1.

### (b) Reseller/implementation partner for an existing DMS-for-B1 product (DocuWare, ELO, EASY/part.de)
- No product build cost. Partner program costs: certification/training (~$10â€“20k one-off), typically modest.
- Margin: search confirms typical VAR/ISV reseller margins run **20â€“40%** of license/subscription value depending on tier and services attached (partner-margin norms found for on-prem enterprise software: ~40% off list at top VAR tier; SaaS product-only resale often 10â€“25%). ECM vendors like DocuWare/ELO/EASY typically sell *through* certified partners (part.de is exactly this model for EASY) with services (implementation, config, integration) as the partner's real margin, not the software resale itself.
- Year 1â€“3: mostly implementation labor (billable to customers, same as any B1 project) plus **thin, structurally-capped software margin** â€” Seidor becomes margin-taker on someone else's roadmap and price list. No new IP created; the "product" line item in Seidor's portfolio stays thin.
- **3-yr total cost: low direct cost (~$30â€“60k certification/enablement)**, but **opportunity cost = no owned product asset**, and long-run margin compression as the vendor (not Seidor) controls list price and product roadmap.

### (c) Hybrid â€” resell archive+capture, build SIGN+FLOW as differentiation
- Reseller costs as in (b) for ARCHIVE/CAPTURE tier (~$20â€“40k enablement).
- Build cost for SIGN + FLOW only (skip ARCHIVE/CAPTURE build entirely): from Section 1's estimates, roughly **Phase 2 + Phase 3 â‰ˆ 23â€“31 person-weeks â‰ˆ $110kâ€“$170k Year 1**, much less than full in-house.
- Integration engineering cost (connecting our SIGN/FLOW to a third-party archive's API/webhooks) â€” **not free**: budget an extra 15â€“20% on top of (b)+(c)'s build number for integration glue and its ongoing maintenance across the partner vendor's version upgrades.
- Year 2â€“3: maintain the differentiation layer (~1.5 FTE, ~$150â€“200k/yr) plus partner margin flow-through on the archive/capture side.
- **3-yr total: ~$450kâ€“$600k**, split between owned IP (SIGN/FLOW â€” arguably our most B1-specific, hardest-to-replicate differentiation, since none of DocuWare/ELO/EASY are B1-native workflow engines) and vendor-margin-exposed commodity archive/capture.

---

## 3. Delivery risk comparison

| Path | Key failure modes |
|---|---|
| **In-house** | **Key-person dependency** â€” this is a small, specialist build (PAdES/HSM signing, OCR tuning, workflow engine) where losing 1â€“2 engineers stalls a phase for months; no vendor fallback if we don't finish. Timeline slippage risk is real and already visible in the doc's own "spike/decide before Phase 3" flags â€” those are unresolved dependencies today, not hypothetical. Biggest risk: spending 12+ months before the first dollar of DMS revenue, with zero pilots so far as ground truth. |
| **Reseller/partner** | **Vendor lock-in and margin squeeze** â€” the vendor sets price, roadmap and support SLAs; our margin is a residual, not a moat, and can be renegotiated down at renewal. **Weak differentiation**: DocuWare's B1 connector is described in the doc itself as "a separate, expensive product" â€” competing GCC B1 partners can resell the same thing, commoditizing the practice. Also real: none of DocuWare/ELO/EASY connectors were confirmed in search results to be GCC-region-native (Arabic OCR, UAE Pass/Nafath/PACI QES) â€” we'd likely still need to build local compliance glue even on this path. |
| **Hybrid** | **Integration risk between two codebases we don't fully control** â€” every partner-vendor API/version change can break our SIGN/FLOW layer; support becomes three-way finger-pointing (customer/Seidor/vendor) when something fails. Requires the most sustained coordination overhead of the three paths. If done well, though, it isolates risk: a failure in the archive vendor's roadmap doesn't kill our own IP. |

---

## 4. Portfolio fit

Seidor already runs **B1 Integrator** (any-source-to-B1 integration platform), a **B1 AI agent product line** (3 agents + read-only MCP server), and **Nexus Ops** (modular CMMS/EAM/Ops SaaS) â€” all under the same "Nexus" brand, same .NET 8 + React shell pattern, same shared-schema multi-tenancy approach that ARCHITECTURE.md explicitly says Nexus Docs reuses. This matters for the decision:

- **In-house build reuses real, already-amortized infrastructure** (tenancy, licensing, audit, ERP adapters, and per Section 4.3 the Nexus B1 Gateway is explicitly "worth building once for the whole Nexus product line" since Nexus Ops needs the identical capability) â€” this lowers the effective in-house cost below what a green-field vendor would pay, and is the strongest argument against pure resale.
- A pure reseller path **adds nothing to the Nexus platform story** â€” it's a bolt-on line item unrelated to our other products, weaker for cross-sell ("one Nexus platform for your B1 estate") than owning SIGN/FLOW alongside Ops and Integrator.
- The **AI agent line and B1 MCP server** are a natural fit with Nexus Docs' own planned `AI` module (Phase 5, NL search/classification) â€” another reason the hybrid or in-house path compounds existing IP rather than a resale path, which compounds nothing.

---

## 5. PM Recommendation

**Recommend the hybrid path (c): become an implementation partner for a market-leading archive/capture product (EASY via part.de's existing partner model, or ELO/DocuWare depending on GCC support quality) for the ARCHIVE + CAPTURE tier, while continuing to build FLOW and SIGN in-house as the differentiated, B1-native layer, reusing the already-designed Nexus B1 Gateway and shared platform shell.**

Justification: ARCHITECTURE.md's own risk log shows the two hardest, most specialist, most legally-exposed pieces of the whole product are exactly FLOW's B1-approval-pattern nuance (Section 4.4's Pattern A/B/C reasoning is genuinely differentiated B1 domain knowledge no generic ECM vendor has) and SIGN's PAdES/HSM/QES work (which is valuable enough, and hard enough, to be worth owning) â€” while ARCHIVE and CAPTURE's OCR/matching, though the "flagship ROI story," are commodity capabilities that established vendors (EASY, ELO, DocuWare) have already spent years hardening, including OCR accuracy work our own doc admits we haven't even benchmarked yet. Pure in-house asks Seidor to out-build category incumbents on their strongest ground before we've sold a single seat; pure reseller throws away our best, most B1-specific IP and turns a potential product line into a thin-margin resale line indistinguishable from competing B1 partners. The hybrid path gets a sellable, credible offering to market fastest (partner archive/capture is available now; FLOW+SIGN is a 5â€“7 month build, not 10+), caps our downside if the in-house build slips, and still leaves Seidor with a genuinely differentiated, portfolio-compounding product layer (FLOW+SIGN) that fits the existing Nexus platform investment rather than duplicating a category we'd struggle to out-build.

---

### Sources
- [SAP - Business One â€“ Boyum Help Center](https://support.boyum-it.com/hc/en-us/articles/30017467500573-SAP-Business-One)
- [Document Management Integration for Invoice Processing & HR Software | DocuWare](https://start.docuware.com/certified-product-ecm-connect-to-sap-business-one)
- [EASY Document Management in SAP Business One (part.de)](https://www.part.de/en/solutions/erp/sap-business-one)
- [SAP Business One: The Practical ERP System... from PART](https://www.part.de/en/solutions/dms/easy-for-sap-business-one)
- [Seamless Digital Transformation with ELO - SAP Business One Integration](https://www.elo.com/en-us/blog/seamless-digital-transformation-with-elo-sap-business-one-integration.html)
- [ELO and SAP](https://www.elo.com/en-de/software/integration/sap.html)
- [M-Files Connector for SAP ArchiveLink](https://catalog.m-files.com/shop/m-files-connector-for-sap-archivelink/)
- [SAP Integration for Document Management | M-Files](https://www.m-files.com/m-files-platform/integrations/sap/)
- [Partner Margin Structure: Reseller Economics - Forecastable](https://forecastable.com/partner-margin-structure-2/)
- [How to set margins for software resellers | MSDynamicsWorld.com](https://msdynamicsworld.com/story/how-set-margins-software-resellers)

**Note on framing:** The computed task requested "1 PM, 2 presales, 2 consultants" as separate role perspectives; this run only executed the **Program Manager** perspective (delivery, cost, roadmap, risk), per its explicit "YOUR ROLE" instruction. The other four role analyses (2x presales, 2x consultants) were not produced by this run and would need separate passes if required â€” flagging this so the orchestrating workflow can confirm whether those are covered by parallel runs.