# Scientific Method Engine — five documented research stages

**ProofLab AI · Meta Global AI Developer Hackathon 2026 · Competition code authored after kickoff.**

The Scientific Method Engine provides a **project workflow** with five visible steps, structured report fields, draft persistence, required document completeness checks, human acknowledgement before approval, immutable approved report snapshots and downloadable Markdown reports. It currently supports **mathematics** through the Angular UI. It is an *alpha research workflow* — not a peer-review platform, a certified laboratory information system, nor a demonstration of a new scientific discovery.

## Why exactly five stages?

There is **no universal internationally mandated five-step scientific method**. The workflow is a practical synthesis of several recognised research methods and transparency guidelines. Real scientific investigation is iterative, and different disciplines require specialized designs and approvals.

| Stage | Report | Minimal gate | Methodological reference |
| --- | --- | --- | --- |
| 1. Problem and prior work | Problem statement report | Focused question, prior-work gap, traceable DOI/URL | IMRaD Introduction; TOP reporting transparency |
| 2. Hypotheses and objectives | Hypothesis report | Main testable claim, alternatives, falsification criteria | OSF preregistration; TOP analysis plan |
| 3. Methodology and protocol | Research protocol | Reproducible procedure, controls, prespecified analysis, ethics/safety | OSF preregistration; TOP study protocol |
| 4. Execution and evidence | Execution log and evidence manifest | Original data references, complete attempt log, provenance | FAIR; W3C PROV-O |
| 5. Analysis and verification | Scientific report and verification dossier | Results, verification status, conclusions and limitations | IMRaD; TOP; JCGM/GUM where measurements are involved |

The **final consolidated report** combines the five report snapshots using IMRaD-inspired headings, but it is not guaranteed to meet a specific journal's submission requirements.

### Stage state and human review

- New research starts in **DRAFT** at stage 1; later stages are **LOCKED**.
- Drafts can be edited and saved locally.
- To advance, every required field must meet the minimum completeness check and the user must explicitly acknowledge reviewing the documentation.
- Approved records receive an approval timestamp and SHA-256 digest of the snapshot; the next stage unlocks.
- Reports approved through the API cannot be edited silently. New revision/reopening workflows are **not implemented yet**, and should preserve previous snapshots when added.
- Completing all five stages proves **documentation completeness only**. The reviewer acknowledgement is not third-party verification, ethical review, peer review, formal mathematical proof, or an accreditation.

**Important limitations:** Current validators check presence, length and basic source format, not the truth or originality of statements. The evidence manifest currently stores **references in text**; the system does not yet upload or verify raw videos/images or the files' hashes. The local `.private/research/audit.jsonl` is an append-only-by-application event log, **not tamper-proof storage** or a complete W3C PROV-O implementation. Access uses an unprotected localhost prototype API, not a secure multi-user deployment.

## Mathematical research specialization

For **Mission 1**, treat a conjecture as a testable claim, numerical experiments as exploration, and a **formal proof verified in Lean 4** as the applicable validation for a mathematical theorem. Numerical support alone does not establish universal truth. If a Lean attempt fails, label the proof attempt **not verified**, not the theorem disproved. A genuinely new mathematical contribution requires a correct formal statement, formal verification (when applicable), novelty/literature assessment, and expert review.

Muse Spark's mathematical planning adapter is intentionally **disabled** until the Meta API billing credit is confirmed. The Scientific Method Engine does **not** auto-generate or auto-approve a report in this stage.

## Local API

- `GET /api/research/stages` — bilingual stage definitions and required fields.
- `POST /api/research` — `{"title":"Research question and scope", "discipline":"Mathematics"}`.
- `GET /api/research/{id}` — read a project.
- `POST /api/research/{id}/stages/{n}/draft` — `{"fields":{"question":"...",...}}` for its active stage.
- `POST /api/research/{id}/stages/{n}/approve` — `{"reviewedByHuman":true}`; **fails** when out of order/incomplete.
- `GET /api/research/{id}/reports/{n}?lang=en` — download a stage Markdown report.
- `GET /api/research/{id}/paper?lang=en` — consolidate a document **only after all five stages are approved**.

Private data stays in `src/ProofLab.Api/.private/research/`, excluded from Git. The UI retains the last research ID locally in browser storage. Avoid storing patient or sensitive data in this unauthenticated prototype.

## Actual sources and scope

- [TOP Guidelines (2025 framework)](https://www.cos.io/initiatives/top-guidelines): research registration, protocols, data/code transparency and verification practices.
- [OSF Preregistration](https://help.osf.io/article/330-welcome-to-registrations): timestamped protocol and plan **before** data collection/analysis; updates require documented changes.
- [FAIR Principles](https://www.go-fair.org/fair-principles/): findable, accessible, interoperable and reusable scientific data.
- [W3C PROV-O](https://www.w3.org/TR/prov-o/): interoperable definitions for entities, agents, activities and lineage.
- [JCGM 100:2008 / GUM](https://www.bipm.org/en/doi/10.59161/jcgm100-2008e): measurement uncertainty, useful for **physics**.
- [ICMJE manuscript recommendations](https://www.icmje.org/recommendations/browse/manuscript-preparation/preparing-for-submission.html): IMRaD report structure and disclosure of research methods, AI use and limitations.
- [EQUATOR Network](https://www.equator-network.org/about-us/what-is-a-reporting-guideline/): domain/design-specific reporting checklists such as CONSORT, STROBE or PRISMA.
- [ISO/IEC 17025](https://www.iso.org/es/contents/data/standard/06/69/66912.html): requirements for competent **testing and calibration laboratories**; **not** a universal scientific-method checklist. ProofLab AI does not claim ISO/IEC 17025 conformance.

## New: real mathematical proof evidence (Mission 1)

For mathematics dossiers, stage 4 and stage 5 now require a **real server-generated Lean 4 attempt ID associated with the same research project**. The attempt can fail or succeed; reports distinguish rejected proof tactics from verified **known** theorems. The server adds the evidence ledger and SHA-256 source digests to stage exports and the consolidated paper. This goes beyond purely typed descriptions, though supporting external video/image files still lack verified upload and provenance checking.

See [the mathematical evidence workflow](MISSION-001-EVIDENCE.md). The deterministic cycle does **not** run Muse Spark and does not represent a scientific discovery.

**Español:** [SCIENTIFIC-METHOD.es.md](SCIENTIFIC-METHOD.es.md).
