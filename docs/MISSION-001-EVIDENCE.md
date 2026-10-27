# Mission 1 — Lean 4 evidence linked to scientific reports

**Competition work:** October 26, 2026, after the official kickoff.
**Release maturity:** controlled mathematics benchmark. No unsolved problem has been solved.

## Why this milestone matters

The previous Scientific Method Engine could freeze five written reports. It now requires **real server-generated formal proof attempts** for the mathematical execution and verification stages, so a report cannot advance solely by typing arbitrary verification claims.

## Reproducible demonstration (NO AI yet)

1. Create a new `Mathematics` research dossier in the Mission 1 web workspace.
2. Complete and acknowledge stages 1, 2 and 3 (problem, hypotheses, preregistered method). This acknowledgement is for **document completeness only**.
3. Open **Stage 4: Execution and evidence**. Choose a **known** theorem in the curated catalog, for example natural-number addition commutativity.
4. Click **Run controlled test cycle**. The backend requests two strictly allowlisted Lean tactics, `rfl` then `omega`, stopping at a successful proof or a blocked verifier. On the commutativity benchmark, `rfl` fails and `omega` is accepted. Results are determined by **actual Lean 4**, never fabricated.
5. Click **Attach to current stage report** for a real attempt. The button inserts the authentic attempt ID, tool result and SHA-256 source hash into the evidence manifest. Finish the other required fields and acknowledge review.
6. In **Stage 5: Analysis and verification**, attach an existing recorded attempt again to the verification field, describe the result and limitations, then approve report completeness.
7. Download each Markdown report and the IMRaD-inspired consolidated report, which include the server-generated Lean attempt ledger.

## Server-enforced gates

- Attempts are disabled before stages 1, 2 and 3 are completed and after the dossier is finished.
- Proof input allows **only** a fixed curated problem ID and `rfl`, `omega`, `simp`; no free-form Lean source, shell commands or arbitrary model code are accepted.
- Each project stores a private, local append-only-by-app JSONL list of attempts, with timestamps, outcomes, diagnostics, SHA-256 digest and known-theorem labels.
- Mathematical stage 4 cannot be approved without a real attempt, regardless of success or failure, **and** the manifest must contain an attempt ID recorded for that same project.
- Stage 5 must likewise cite an actual attempt from that same project in the verification text.
- These guards distinguish **verified known theorem**, **failed proof attempt**, and **novel mathematical discovery (NONE claimed)**.
- Records are not tamper-proof: a person with local filesystem access can change files. The development API is unauthenticated and bound to loopback only.

## Endpoints

- `GET /api/research/{id}/math/attempts` — list recorded proofs.
- `POST /api/research/{id}/math/attempts` — `{"problemId":"NAT-ADD-COMM","strategy":"rfl"}` (one allowlisted attempt).
- `POST /api/research/{id}/math/cycle` — `{"problemId":"NAT-ADD-COMM"}` (deterministic two-tactic baseline).
- `POST /api/research/{id}/stages/4/approve` and `.../5/approve` — now additionally enforce real proof attempt references.
- `GET /api/research/{id}/reports/{stage}?lang=en` and `GET /api/research/{id}/paper?lang=en` append the evidence ledger.

The public end-to-end regression script `tests/smoke-math-evidence.ps1` runs a labeled **control-only** mathematical dossier and checks: early execution rejection, missing evidence rejection, `rfl` failure, `omega` success, cross-stage attempt-ID requirements, and proof records in the exported paper. GitHub Actions re-runs this scenario on an ephemeral runner using pinned Lean 4 and .NET 10.

No API keys or original user research data belong in the public repository. Runtime ledgers stay under `src/ProofLab.Api/.private/research/math-attempts/` and are excluded from Git.

## Next: actual AI hypothesis cycles

The local proof test cycle is deliberately named `DETERMINISTIC_BASELINE_NOT_AI`. It is **not** Muse Spark and is **not** scientific discovery. To support genuinely agentic research, we must validate Meta billing/credits, implement an explicit tool-calling loop with a limited set of proof tools, and isolate more expressive generated Lean source before attempting open conjectures. A kernel-accepted proof is not by itself proof of *originality* or *correct formalization*; review of literature and statement equivalence remains essential.

Lean 4 reference: https://lean-lang.org/doc/reference/latest/Tactic-Proofs/Tactic-Reference
Meta function-calling cookbook: https://dev.meta.ai/docs/cookbook/tool-function-calling

**Español:** [MISSION-001-EVIDENCE.es.md](MISSION-001-EVIDENCE.es.md).
