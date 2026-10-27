# Mission 1 — Guided scientific drafts WITHOUT Muse Spark

**Date:** 2026-10-27. **Scope:** strong Goldbach demonstration only. **This is NOT autonomous AI.**

## Rationale

The human researcher should not have to type every field in five report stages. This edition produces **deterministic, reviewable drafts** from curated sources and actual server-recorded evidence, without any Meta inference or paid API calls.

## UI workflow

1. Create or open a mathematics study whose title includes **Goldbach**, such as "Finite exploration of the Goldbach conjecture".
2. On stages **1, 2 or 3**, select **"Prepare guided draft (no AI)"**. Only **empty** fields are completed, preserving everything already written.
3. Read, edit and save the report. A human must personally acknowledge reviewing the report's completeness, then approve advancement. No tool autoapproves any stage.
4. At stage **4**, click **"Run guided checks (no AI)"**. The existing bounded backend runs **Goldbach finite checks on 4..100** and constrained Lean 4 tactics on a **separate, previously known theorem**; genuine evidence IDs and SHA-256 hashes are recorded.
5. Once both real evidence categories exist, select "Prepare guided draft (no AI)" on stage 4. The fields cite **actual IDs, timestamps, outcomes and hashes**. Review and approve manually.
6. At stage **5**, prepare the draft using the recorded evidence. Explain the finite-only conclusion and keep known Lean benchmark proofs strictly separate from unproved Goldbach. Review/acknowledge the final stage and export bilingual Markdown.

## Endpoints and safeguards

- `GET /api/research/{id}/stages/{stage}/guided-draft?lang=es|en` returns a **read-only preview**; it does not save or approve reports and only supports active stages 1–5 of Goldbach-titled mathematics dossiers.
- `GuidedResearchDrafts.cs` grounds stages 1–3 in the curated Goldbach catalog (MathWorld and cited DOI), prespecifying 49 even cases, .NET 10 C# computation and Lean 4 v4.35.0 verification of an **independent known benchmark**.
- Stage 4/5 generation refuses to proceed **unless both** real finite Goldbach observations and at least one authentic Lean attempt already exist for the same dossier. It cannot manufacture witnesses, compiler outcomes or source hashes.
- The existing scripted `POST /api/research/{id}/math/offline-cycle` is exposed as "Run guided checks (no AI)". Only authorized fixed tools, bounded attempts, no arbitrary Lean, no Muse calls or automatic approvals.
- The UI **fills blank browser fields only**; it does not overwrite user writing, persist drafts, close reports or click the human acknowledgment checkbox.

## Acceptance and limitations

CI smoke exercises all required bilingual stage 1–3 fields, verifies that stage 4–5 preview cites actual evidence IDs and checks that `isAutonomousAi` and `isScientificDiscovery` remain `false`. Finite Goldbach results **do not prove its universal statement**, and the Lean kernel checks only a known theorem. Scripted research drafting is not LLM reasoning.

Autonomous literature review, novel hypothesis search, actual human peer review and execution of arbitrary AI-generated Lean remain unimplemented. Mission 1 is NOT finished.

**Español:** [MISSION-001-GUIDED-DRAFTS.es.md](MISSION-001-GUIDED-DRAFTS.es.md).
