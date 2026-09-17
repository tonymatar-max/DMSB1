# Nexus Docs (DMS B1): Build vs. Buy vs. Hybrid â€” Decision Synthesis

*Prepared for Seidor leadership. Synthesizes five independent analyses: Program Manager (delivery/cost/risk), Presales Engineer #1 (competitive landscape), Presales Engineer #2 (deal-cycle/win-loss), Technical Consultant #1 (integration depth), Technical Consultant #2 (implementation cost).*

---

## 1. Executive Summary

**Recommendation: Hybrid â€” become an implementation/reseller partner for a market-leading archive + AP-capture ECM product (ELO where on-prem/data-residency is a hard requirement, DocuWare otherwise) to cover the commodity ARCHIVE/CAPTURE tier now, while continuing to build FLOW and SIGN in-house â€” but narrow the "in-house" scope specifically to the Nexus B1 Gateway (the on-prem tunnel), the honest three-pattern B1 approval framework, and the SIGN/QES layer, rather than the full workflow-designer breadth PM's estimate assumes.** All five analyses converge on hybrid as the right shape; they diverge on how fast we can talk about it and how wide the "build" half should be. Today, Nexus Docs is architecture-complete and code-unknown/early â€” there is no evidence in ARCHITECTURE.md of shipped code, a demo tenant, or a single pilot customer, and both presales analyses are explicit that we cannot compete in a live bake-off against EASY/DocuWare/ELO right now. The honest near-term move is therefore to **sell the reseller product today**, use that revenue and customer contact to fund and de-risk the in-house FLOW+SIGN build, and only bring our own product to market once Phase 1â€“2 are demoed live and the open technical/legal risks (PDF-library choice, Pattern B validation, QES legal review, OCR benchmark) are closed.

---

## 2. The Real Market Landscape

Deduped across all five analyses. "Nexus Docs" row reflects designed/target capability, not shipped capability â€” see Section 3.

| Product | Archive | Approval Workflow | E-Signature | AP Capture/OCR | B1 Integration Depth | On-Prem Option | GCC/MEA Presence | Pricing (where known) |
|---|---|---|---|---|---|---|---|---|
| **Nexus Docs (ours, target design)** | Versioned, retention, WORM, redaction | Full designer, SLA, delegation, ERP-aware routing | SES/AES built, QES pluggable (UAE Pass/Nafath/PACI) | OCR + 3-way match to PO/GRPO | Deep â€” purpose-built (Service Layer + outbound-WSS Gateway) | Yes, on-prem-first by design | We ARE the GCC partner (but zero reference customers) | Per-user/module + envelope + volume bands (not yet market-tested) |
| **DocuWare** (+ ECM Connect / Conduit Connector, 3rd-party B1 connectors) | Strong, market-leading, own module | Yes, own module | No â€” 3rd party only | Yes â€” Intelligent Indexing, proven | Bolt-on 3rd-party connector, not DocuWare-built | Yes, but commercially cloud-first | None confirmed | ~$75â€“120/user/mo cloud blended; on-prem ~Â£20â€“57/user; implementation $5kâ€“25k+ |
| **ELO Digital Office** (ELO ECM / "B1ECM") | Strong, GoBD-compliant | Yes ("intelligent workflows") | No | Basic scan/barcode capture only, not 3-way match | Native in-client embed (BLP + Integration Service) | Yes â€” strongest on-prem/private-cloud flexibility of the three | None confirmed | Unpublished |
| **EASY DMS (part.de)** | Strong, archive-first | Basic/ECM-level | No | Not evidenced | Deep, B1-specific reseller relationship | Yes, DACH on-prem-first | None found | Unpublished |
| **M-Files** | Strong (general ECM) | Limited | No | No | Weak â€” ArchiveLink/S4-oriented; no confirmed B1-specific connector | Yes | None found | Unpublished |
| **Boyum IT (B1UP/B1DM)** | Basic keyword filing only | No (not a workflow engine) | No | No | Very deep, SAP-certified B1 add-on â€” but shallow DMS functionally | Yes | Strong existing B1 channel reach | Unpublished, per-partner |
| **SAP B1 native (do-nothing baseline)** | File-path only, no versioning | Pre-add only, structurally fragile | No | No | N/A | Yes | Universal | Included in B1 |

**Read on the table:** no competitor covers all four pillars in one product â€” every one is missing e-signature outright, or capture, or has only shallow B1 integration. That gap is real. But three of them (DocuWare, ELO, EASY) are live, referenceable, and have shipped for years; we have a design document.

---

## 3. Where the Five Roles Agree

1. **ARCHITECTURE.md is a design document, not a build log.** All five independently flagged this: no done/stub markers, everything in future/normative tense, repo layout explicitly marked "(planned)." Treat Nexus Docs as **architecture-complete, code-unknown** â€” not "feature-gap-away-from-selling." This is the single most load-bearing shared finding and should anchor the whole decision.
2. **No competitor combines archive + workflow + e-signature + AP-capture as one audit-chained, B1-native product.** Confirmed independently by both presales analyses and the PM. This is a real positioning gap.
3. **The Nexus B1 Gateway (outbound-WSS tunnel) and the honest three-pattern approval framework (A/B/C, naming that B1 Approval Procedures fire pre-add) are genuine, GCC-relevant technical differentiation** â€” not something any of DocuWare/ELO/M-Files/EASY's public documentation shows solved the same way. TC1's research is the strongest evidence for this and both PM and TC2 lean on it.
4. **ARCHIVE and CAPTURE are commodity ground the incumbents have hardened for years; we haven't even benchmarked our own OCR.** PM, TC1, and TC2 all independently conclude it's not worth out-building DocuWare/ELO on archive/retention/OCR from scratch.
5. **A hybrid model â€” resell the commodity tier, build the differentiated tier â€” is the right shape.** All five land here, including both presales roles, despite their more cautious tone on timing.
6. **The named open risks are real gating items, not paperwork**: PDF-library decision (iText/Apryse/BouncyCastle), Pattern B's unvalidated Service Layer behavior, per-country QES/legal review, and the unbenchmarked Arabic/English OCR. Every role that touched SIGN or CAPTURE flagged these independently, and TC1's citation of SAP Community confirms Pattern B risk is a documented, version-dependent constraint industry-wide, not something we're behind on.
7. **GCC/MEA presence looks like a genuine white space** â€” no confirmed Gulf presence for EASY, DocuWare, ELO, or M-Files for B1. (Note: Presales #1 itself flags this as unconfirmed-absence, not confirmed-absence â€” see tension #4 below.)

---

## 4. Where the Five Roles Genuinely Disagree

**a) How urgent the "don't sell this yet" warning is.** Presales #2 issues an explicit **Go/No-Go: "No-go for competitive sales situations" today**, and states walking into an RFP citing PAdES/3-way-match/hash-chained audit "as if they exist would be a credibility risk." The PM's business-case framing is far softer â€” it discusses 3-year TCO and portfolio fit as a forward capital-allocation decision without ever stating a stop-selling instruction, and its recommendation could be read by a commercial audience as "greenlight the hybrid build" without registering that the in-house half currently has **zero** reference customers and no demoed Phase 1. **What this means for the decision: leadership must not read the PM's hybrid recommendation as license to start actively selling FLOW/SIGN capability to customers now.** The presales view should govern go-to-market timing; the PM view should govern investment timing. These are different clocks and conflating them is the single biggest risk of this whole synthesis.

**b) How wide the "build" half of the hybrid should be.** PM's hybrid keeps *all* of FLOW + SIGN in-house as "our most B1-specific, hardest-to-replicate differentiation." TC1's technical read is narrower and more skeptical: he explicitly says the Service Layer + Gateway + three-pattern architecture is "sound... but not a technical moat" except specifically for **the Gateway and the honest approval-pattern framing** â€” competent engineering re-solving a problem the market solved 10-15 years ago via ArchiveLink/BLP/DI-API bridges. TC1 does not extend that "keep it" endorsement to the full workflow-designer breadth (routing UI, SLA timers, delegation) that PM's person-week estimate for Phase 2 is built around, nor unconditionally to all of SIGN's crypto stack. **This matters commercially: if the real differentiation is narrower than PM's estimate assumes, the 10-14 person-week FLOW estimate may be over-scoped relative to what's actually worth building in-house versus what could itself be reduced or partially sourced from a workflow-capable ECM partner.** Leadership should ask engineering to explicitly re-scope FLOW against TC1's narrower "what's actually a moat" boundary before committing the PM's estimated budget to it.

**c) Confidence in the GCC white-space claim.** PM and TC1 use "no GCC presence for any competitor" as a load-bearing portfolio-fit and differentiation argument. Presales #1, who generated the underlying finding, explicitly caveats it: *"not found in search" isn't proof of absence... presales #2 or the market-research pass should sanity check this before we lean on it as a headline claim.* Presales #2 did not independently re-verify it either. **This is an unresolved evidentiary gap sitting underneath one of the recommendation's supporting pillars, not a settled fact** â€” it should be verified (e.g., checking Gulf-region B1 partner directories, EASY/DocuWare/ELO regional partner listings directly) before it's used in a customer-facing pitch or a board deck.

**d) Whether the case for resale is "we're not there yet" or "some things aren't worth building at all."** PM and TC2 frame resale primarily as a speed/risk-reduction move on the path to eventually owning more. TC1's framing is closer to a permanent verdict: rebuilding the archive/retention/WORM/redaction engine is "the real risk" in build-vs-buy, independent of timing â€” implying even a well-funded, well-staffed in-house effort would be misallocating engineering effort against 20-year incumbent depth. This is a difference in *why* to resell (temporary bridge vs. permanent commodity-tier boundary) that affects how the hybrid boundary should be revisited later (see Section 5) â€” if it's "permanent," Seidor should not plan to eventually build ARCHIVE/CAPTURE in-house once revenue justifies it; if it's "temporary," that's a legitimate later expansion path.

---

## 5. Recommendation

### Next 90 days
1. **Do not make any customer-facing claim about Nexus Docs' FLOW/SIGN/CAPTURE capability.** Per Presales #2's Go/No-Go, this is a hard stop until Phase 1â€“2 are real and demoed.
2. **Stand up the reseller relationship** with ELO (default, for on-prem/data-residency-sensitive GCC prospects) and/or DocuWare (for cloud-tolerant accounts) â€” certification/enablement cost is low (~$20â€“60k) relative to everything else in play, and it lets Seidor bill from month one on real deals.
3. **Get direct engineering confirmation of actual build status** â€” both presales analyses independently flagged that they could find no evidence of shipped code or a demo tenant anywhere outside ARCHITECTURE.md, and this single fact "flips the whole recommendation" per Presales #2. Resolve this before allocating further build budget.
4. **Run the Pattern B one-week spike** against a real B1 10.x tenant (per PM/TC1) â€” this gates whether Pattern B, and therefore a chunk of the FLOW pitch, is sellable at all.
5. **Decide the PDF-signing library** (iText AGPL vs. Apryse vs. BouncyCastle) â€” blocks all SIGN work and has a real cost/timeline tail attached.
6. **Re-scope FLOW's build estimate against TC1's narrower "actual moat" boundary** (Gateway + approval-pattern honesty, not the full workflow-designer surface) before committing PM's estimated 10â€“14 person-weeks.
7. **Verify the GCC white-space claim independently** (partner directories, direct outreach to EASY/DocuWare/ELO regional contacts) before using it as a headline differentiator externally.

### Next 6â€“12 months
1. Ship and **demo Phase 1 (CORE+ARCHIVE+Gateway+attachment bridge) live against a real B1 company database** â€” non-negotiable per both presales roles before any competitive sales motion.
2. Ship Phase 2 (FLOW, re-scoped per above) with one real routing scenario end-to-end.
3. **Get local counsel sign-off on e-signature assurance-level claims** for at least the first prospect's GCC jurisdiction before SIGN appears in any written proposal.
4. **Benchmark OCR against ~200 real mixed Arabic/English invoices** before quoting any AP-automation ROI number.
5. **Land one real or friendly-customer pilot** on Phase 1 â€” every analysis independently treats "zero reference customers" as the single biggest credibility gap versus EASY/DocuWare/ELO.
6. Continue billing and delivering through the ELO/DocuWare reseller channel throughout this period â€” this is not a stopgap to be abandoned once the build starts working; it's the revenue and credibility base the whole hybrid depends on.

### Conditions for revisiting this recommendation
- **If a paying/friendly pilot on Phase 1 has not landed within 6 months**, reassess whether in-house FLOW/SIGN build should continue at current pace, or whether Seidor should deepen the resale relationship instead and shrink the "build" half further.
- **If the Pattern B spike or PDF-library decision slips past 60 days**, treat that as a leading indicator the 5â€“7 month PM timeline for Phases 1â€“3 is optimistic, and re-forecast before committing further engineering months.
- **If GCC-presence verification turns up an existing regional DocuWare/ELO/EASY partner**, the differentiation argument weakens materially and the hybrid's build-side ambition should be scaled back accordingly.
- **If counsel review finds QES/e-signature claims cannot be made in the first target jurisdiction on the current timeline**, SIGN's business case should be reassessed independent of the rest of FLOW.

---

## 6. Named Risks to This Recommendation

| Risk | What would make the recommendation wrong | How we'd notice |
|---|---|---|
| **Build status is worse than "unknown" â€” it's actually zero, and stays zero** | If, 90 days from now, there is still no running Phase 1 demo tenant, continuing to fund in-house build is a sunk-cost trap; the correct call becomes "resell everything, defer build indefinitely." | Engineering cannot produce a live B1-tenant demo of ARCHIVE+Gateway on request. |
| **FLOW's real moat is narrower than PM's estimate assumes** | If TC1 is right that only the Gateway + approval-pattern framing is truly differentiated, we may be about to fund 10-14 person-weeks of workflow-designer UI that a resold ECM's own workflow module already covers adequately â€” wasted build spend. | A side-by-side feature comparison shows ELO's/DocuWare's own workflow module already meets 80%+ of target customers' routing needs once B1-approval interception is handled by our Gateway/pattern layer alone. |
| **GCC white-space claim collapses under verification** | If a Gulf-region DocuWare/ELO/EASY partner turns up, the "we're the only GCC-native option" pillar of the portfolio-fit argument weakens, and pure resale becomes commercially safer, not just faster. | Direct partner-directory or vendor-relationship check turns up a named Gulf reseller for any of the three. |
| **QES legal review comes back negative or open-ended for the first target country** | SIGN cannot be sold at all in that jurisdiction on any near-term timeline; the whole "own e-signature as differentiation" thesis narrows to countries with resolved QES. | Counsel review (90-day action item) returns "not clearable in current timeline" for the lead prospect's country. |
| **Pattern B fails the spike on the customer's actual FP/patch level** | A chunk of FLOW's "we solve pre-add approval" pitch degrades to Pattern C (UDF-stamping, not real interception) for that customer, weakening the core differentiation claim mid-deal. | Spike results show `ApprovalRequests` behavior unsupported or unstable on the target tenant's Service Layer version. |
| **Presales urgency gets lost inside the PM's calmer capital-allocation framing** | Sales teams start quoting FLOW/SIGN/CAPTURE capability to prospects because the PM memo reads as a green light, triggering exactly the credibility risk Presales #2 warned about. | Any customer-facing proposal or RFP response references Nexus Docs SIGN/FLOW/CAPTURE before Phase 1â€“2 are demoed and piloted. |