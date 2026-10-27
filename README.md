# ProofLab AI — Meta Hackathon 2026

**Competition edition — created October 26, 2026 after the official kickoff.**

ProofLab AI is an experimental **scientific evidence engine** for adult researchers, built for the
*Push the Frontier* theme of the Meta Global AI Developer Hackathon. It uses physical
measurements and the models provided by Meta to make testable predictions and
help explain why data may agree or disagree with a scientific hypothesis.

**Project status: alpha.** Real Meta inference and end-to-end SAM frame parsing
have **not yet been verified**. Calculations and demonstration data work offline.

## Competition provenance

This is a **new project**, distinct from the pre-event private prototype at
`C:\PROOFLAB-AI`. No pre-kickoff source files were copied into this codebase.
All repository history represents work performed after the October 26 kickoff,
following an official clarification from Cerebral Valley in Discord
that development is permitted after the October 26 kickoff.

The final submission must be public, original, and disclose exactly what was built
during the event. Do not misrepresent demonstration data as physical evidence.

## What this edition implements

- **Bilingual ES/EN responses:** Lean explanations, visible results, UI messages and generated reports follow the selected language without changing the underlying proof or triggering new billable requests. See [language policy](docs/LOCALIZATION.md).

- **Scientific Method Engine (Mission 1):** five visible research stages, documented required fields, server-side gates, locally stored draft reports, human completeness acknowledgement, SHA-256 freeze and Markdown exports, plus a consolidated research report. These controls do **not** certify scientific validity. See [methodology](docs/SCIENTIFIC-METHOD.md).

- **Linked Lean evidence (Mission 1):** deterministic proof cycles and manually selected tactics execute in a curated Lean 4 sandbox-free local verifier; genuine attempt IDs and hashes are required by stages 4–5 of a mathematics dossier, and the report includes those records. This is a known-theorem control, not a novel AI discovery. [Evidence workflow](docs/MISSION-001-EVIDENCE.md).
- **Mission 1 — Mathematical Discovery (first working stage):** three curated known theorems, fixed Lean 4 tactics, real compiler verification, result hashes and private local attempt ledger. **No open problem has been solved.** See [Mission 1](docs/MISSION-001-MATH.md).
- **Curated open conjecture (strong Goldbach):** dated sources, bounded finite checks through 2000, reproducible hashes, research-specific evidence ledger and bilingual reporting. **Not a Lean proof or new discovery.** See [open conjecture study](docs/MISSION-001-OPEN-RESEARCH.md), [agent tool contract](docs/MISSION-001-AGENT.md) and [future Lean sandbox design](docs/LEAN-SANDBOX.md).
- **Mission 1 offline rehearsal (WITHOUT Muse Spark):** scripted flow connecting a bounded Goldbach check, rejected Lean `rfl` tactic, conditional `omega` retry, evidence/decision ledger and ES/EN dossier appendices. Reports still require declared human completeness review; no original mathematical discovery is claimed. [Offline protocol](docs/MISSION-001-OFFLINE-REHEARSAL.md).
- **SQLite scientific experience memory:** stores genuine Lean outcomes and recalls earlier compatible proofs across research dossiers, while rechecking every recalled tactic to produce a new evidence ID. No AI inference, parameter learning or open-conjecture proof. [Scientific memory](docs/SCIENTIFIC-MEMORY.md). The [versioned SQL schema](src/ProofLab.Api/Database/001_scientific_experience.sql) lives in this repository; actual `.sqlite` data stays private.
- **Bilingual guided research drafts (NO AI):** generate empty fields for stages 1–3 from curated Goldbach material and 4–5 from actual recorded evidence; a separate stage-4 control runs bounded Goldbach/known-Lean tools. Never overwrite user work or autoapprove; not a Goldbach proof. [Guided draft protocol](docs/MISSION-001-GUIDED-DRAFTS.md).

- .NET 10 ASP.NET Core API, with independent scientific prediction and evaluation.
- Pendulum period: small-angle theory `T=2π√(L/g)`; measured period detected from
  observed upward center crossings, **not biased by the theoretical period**.
- Rolling solid sphere on an incline: predicted `a=(5/7)g·sin(θ)` and
  measured acceleration fitted from calibrated time-and-position observations.
- Provenance labels distinguish synthetic, manually entered and tracking coordinates.
- Angular 20 web UI, with Spanish/English switch, prediction, synthetic demonstrations
  and JSON report export.
- Meta Muse Spark 1.3 hypothesis-planning adapter over Chat Completions.
- Meta SAM 3.1 single-image segmentation adapter over Responses API;
  **returns raw output only**, without decoded masks or video tracking.
- The Meta adapters are **disabled by default** until credits, billing, and
  safe testing are confirmed.

## Prerequisites

.NET SDK 10, Node.js 20+, npm. Use a Windows PC or equivalent environment.

### Start the API

```powershell
cd C:\PROOFLAB-AI-HACKATHON
dotnet test tests/ProofLab.Tests/ProofLab.Tests.csproj
dotnet run --project src/ProofLab.Api --urls http://127.0.0.1:5391
```

### Start the web UI (second terminal)

```powershell
cd C:\PROOFLAB-AI-HACKATHON\ui
npm.cmd ci
npm.cmd start -- --host 127.0.0.1 --port 4300
```

Open `http://127.0.0.1:4300/` on the same computer.

### API routes

| Route | Result |
| --- | --- |
| `GET /api/health` | Readiness and Meta usage gate |
| `GET /api/meta/readiness` | Secret-free key, credit confirmation, consent and request-budget status |
| `GET /api/research/stages` | Scientific method stage definitions and mandatory fields |
| `POST /api/research` | Create a mathematical research dossier |
| `POST /api/research/{id}/stages/{n}/draft` | Save a local report draft |
| `POST /api/research/{id}/stages/{n}/muse-draft` | Gated Muse Spark suggestion, unverified and never autosaved |
| `POST /api/research/{id}/stages/{n}/approve` | Lock a complete report after declared human review |
| `GET /api/research/{id}/reports/{n}` | Download each stage report (Markdown) |
| `GET /api/research/{id}/paper` | Download consolidated paper after all stages |
| `GET /api/math/problems` | Known theorem control benchmarks |
| `GET /api/math/status` | Lean availability and truthful research status |
| `POST /api/math/verify` | Check a curated theorem via Lean 4, save local attempt record |
| `POST /api/math/plan` | Muse Spark suggests research tactics, gated behind verified Meta credits |
| `GET /api/science/experiments` | Supported physics experiments |
| `POST /api/science/predict` | Scientific prediction |
| `POST /api/science/evaluate` | Evaluation of timestamped observations |
| `GET /api/science/synthetic/{kind}` | Clearly synthetic sample, never real evidence |
| `POST /api/ai/plan` | Live Muse Spark planning; gated |
| `POST /api/ai/segment-frame` | Live SAM 3.1 response; gated, raw grammar |

Example prediction:

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:5391/api/science/predict `
  -Method Post -ContentType 'application/json' `
  -Body '{"kind":"Pendulum","lengthMeters":0.5}'
```

Sample evaluation fields: `setup`, `observations` (array of
`{"timeSeconds":0,"position":1.0}`), `origin` (`Simulated`,
`ManuallyEntered`, or `TrackingCoordinates`). For the pendulum
the position is a consistent horizontal coordinate; for rolling sphere it
is **metres along the ramp**.

### Enabling Meta API — only after verifying credit and permission

The key belongs **only to the backend process** as `MODEL_API_KEY`, never Angular or GitHub. `GET /api/meta/readiness` reports key presence **without returning the secret**. Live calls require four simultaneous gates: key configured, `Meta:CreditsConfirmedByOwner=true`, `Meta:OwnerApprovedPaidRequests=true`, and `Meta:AllowPaidRequests=true`. The app also imposes **2 live Meta calls per process startup**, which is not a hard dollar cap. Billing credit status is not verified automatically. When genuinely authorized, the Goldbach interface can request unverified Muse Spark drafts, still requiring human review.

**Step-by-step token instructions:** [Meta Model API connection EN](docs/META-API-CONNECTION.md). Do not enable charges until Billing credits and owner approval are confirmed. Spending alert emails are not hard limits.

No Meta calls have been made as part of the implementation tests.

## Roadmap

**Research mission sequence:** (1) Mathematical Discovery → (2) Autonomous Physics Discovery → (3) Historical & Big Data Discovery → (4) Autonomous Research Director. All four share a future independently verifiable research ledger; only an initial part of Mission 1 is implemented today.


1. Record 3×50 cm and 3×80 cm real pendulum videos; measure manual baseline.
2. Integrate SAM 3.1 video upload + streamed masks using official Meta SAM parser.
3. Connect masks to calibrated observations with timestamps and audit log.
4. Run Muse Spark planning and interpretation using actual experiment results.
5. Add rolling-sphere recordings and reproduce measurements.
6. Publish a working demo, record a three-minute English video, and submit before
   November 4, 2026 at 9 PM Peru time.

See the bilingual project documentation: [English](docs/EXPERIMENTS.md) ·
[Español](docs/EXPERIMENTS.es.md), [provenance](docs/PROVENANCE.md) ·
[procedencia](docs/PROVENANCE.es.md).

**Versión en español:** [README.es.md](README.es.md).
