# Mission 001 — Mathematical Discovery

**Competition edition:** new source authored on October 26, 2026 after the official Meta hackathon kickoff.
**Scope:** the first layer of a scientific research agent. This is a *formal verification benchmark*, not a claim that an open conjecture has been solved.

## Objective

Give the future scientific agent a reliable truth gate. A proposal is not a mathematical discovery simply because a language model finds it convincing. Formal claims must be checked by the Lean 4 kernel. Literature novelty and the correctness of the formalized statement require additional independent review.

## Implemented prototype

- Three **already-known** natural-number theorems (addition of zero, commutativity, monotonicity under nonnegative increments).
- Select a preapproved proof strategy `rfl`, `omega` or `simp` and generate a fixed Lean 4 source file.
- Run Lean on the authorized local host with a 25-second limit. A success code is labeled **VERIFIED: known theorem**; a failure is **REJECTED**. Lack of Lean reports **UNAVAILABLE**.
- Generate SHA-256 digests and append local results to `.private/math/verification-attempts.jsonl` (ignored by Git).
- Minimal bilingual web UI with problem selection, tactic, diagnostics and traceable attempt ID.

The repository pins `leanprover/lean4:v4.35.0` in `lean-toolchain`. The `formal/` directory contains both a positive Lean benchmark and a deliberately invalid proof; the separate GitHub Actions workflow independently checks that the former passes and the latter fails.

**Security:** Do not execute AI-generated or user-supplied arbitrary Lean source on this host. The API only accepts a curated theorem ID and one of three fixed tactics. Moving to unrestricted theorem-proving will require a hardened isolated sandbox with no secrets, network or user file access and resource limits.

## Local execution

Lean 4 may be installed through `winget install --id Lean.Elan -e` on Windows, then `elan default leanprover/lean4:stable` and `elan toolchain install leanprover/lean4:stable`. The tested local version on October 26 was Lean **4.35.0**. The verifier discovers a Lean executable on PATH, in the user's elan bin directory, or a Windows winget command alias. The repository pins the project toolchain to v4.35.0 for reproducible benchmarks.

Existing .NET/Angular commands still apply. New HTTP routes:

- `GET /api/math/problems` — curated benchmark problems and provenance.
- `GET /api/math/status` — verifier availability and honest development status.
- `POST /api/math/verify` — `{"problemId":"NAT-ADD-COMM","strategy":"omega"}`.
- `POST /api/math/plan` — gated Muse Spark strategy planner; its unverified suggestions are never executed automatically.

Expected output is one of `VERIFIED`, `REJECTED`, `UNAVAILABLE`, `TIMED_OUT`. The flag `isNewDiscovery` is always false in this phase.

## Verification acceptance criteria

1. A valid proof of a known theorem is accepted by a real Lean process.
2. A wrong strategy is rejected by Lean without claiming a mathematical counterexample.
3. Arbitrary source, `sorry`, `admit`, and unknown theorem IDs are rejected *before* process execution.
4. The evidence log gives the attempt ID, time, source hash and result.
5. No API credits are spent during these offline formal-verification checks.

## From Mission 1 to Mission 4

1. **Mathematical Discovery:** stable formal verifier; then select externally documented open conjectures (e.g. [Formal Conjectures](https://github.com/google-deepmind/formal-conjectures)), vet the statements, isolate execution, and integrate Muse Spark to propose research approaches.
2. **Autonomous Physics Discovery:** reuse the evidence ledger and hypothesis verification with real videos and SAM 3.1.
3. **Historical & Big Data Discovery:** time-censored scientific datasets, blind predictions and independent evaluation.
4. **Autonomous Research Director:** choose a worthwhile new problem, request human-provided measurements only when necessary, manage hypotheses and resume experiments across sessions.

We do **not** claim open-problem proofs, independent mathematical novelty, a trained new large language model, or real Meta API integration in this phase.

**Español:** [MISSION-001-MATH.es.md](MISSION-001-MATH.es.md).
