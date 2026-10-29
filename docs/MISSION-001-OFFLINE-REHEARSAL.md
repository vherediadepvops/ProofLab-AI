# Mission 1 — Scientific flow rehearsal WITHOUT Muse Spark

> **Current permissions (2026-10-29):** disabled-payment references below describe that instance's historical state or isolated rehearsal. They do not instruct agents to disable the owner's API or new branches. Follow the [current authorization and testing policy](META-API-CONNECTION.md).

**Date:** 2026-10-27. **State:** deterministic scripted harness with real bounded tools, not an AI agent.
**Meta models and billable requests stay off**, `Meta:AllowPaidRequests=false`.

## Goal and epistemic separation

Test the future `research → hypothesis → choose tool → validate parameters → execute → read feedback → retry → record → report` pipeline without Muse Spark. All proposed decisions are hardcoded; they **do not constitute autonomous model reasoning**.

**Open problem:** strong Goldbach conjecture; ProofLab has no proof or vetted Lean formalization.
**Safe tools:** bounded C# Goldbach computation and Lean proof of a **different, already known** natural-addition commutativity theorem.

## Preconditions and API

- Create a `Mathematics` dossier. Independently complete and acknowledge stages 1–3; offline rehearsal requires active stage 4 or 5.
- `POST /api/research/{id}/math/offline-cycle` accepts no user proof scripts or tool parameters.
- `GET /api/research/{id}/math/offline-events` returns append-only local JSONL with validated proposals, outcomes, feedback, IDs and SHA-256.
- The harness **does not approve reports**. The full field requirements, declared human review and genuine known Lean attempt IDs remain mandatory for stages 4 and 5.
- Limits: **3 tool actions per cycle**, **3 cycles per dossier**; existing underlying ledgers retain their limits. No arbitrary Lean fallback.

## Fixed test protocol

1. **Bounded hypothesis:** each of 49 even numbers 4..100 is expected to have a prime-sum witness. Validate `CHECK_GOLDBACH_FINITE` JSON with `evenLimit=100`. Save finite result, ID and hash; **not a formal proof**.
2. **Weak Lean benchmark:** validate `VERIFY_KNOWN_BENCHMARK` for `NAT-ADD-COMM` with `rfl`. The real verifier should return `REJECTED`; this is not a disproof.
3. **Conditional retry:** only on `REJECTED`, try `omega` for that **same known theorem**. Expected `VERIFIED`. Stop instead on timeout, unavailable verifier or surprising acceptance.
4. Always conclude `OPEN_CONJECTURE_INCONCLUSIVE` for Goldbach. Never claim a novel result or a Goldbach Lean proof.
5. Include event trace in stages 4–5 and the consolidated ES/EN report, alongside separate actual Lean and numeric evidence ledgers.

Each event records `cycleId`, sequence, hardcoded proposal JSON, actual tool outcome, evidence ID/SHA-256, time and ES/EN explanation. Original Lean failures survive in their own evidence store; event rows are appended individually, so cross-ledger writes are **not transactional**.

## Acceptance checks

- HTTP 400 for premature attempts before stage 4.
- In a **CI-only artificial documentation fixture**, run 3 actual tools with `NO_COUNTEREXAMPLE_IN_FINITE_RANGE`, `REJECTED`, `VERIFIED`; cross-check event IDs against source ledgers.
- Research remains at **stage 4** after rehearsal: no automatic scientific approval.
- Stage 4/5 approval still requires a real Lean attempt ID; fixture acknowledgments never represent peer review.
- ES/EN Markdown reports and full paper contain the real offline `cycleId` and explicitly mark Goldbach inconclusive.
- Run .NET, Angular and real Lean CI without any Muse Spark or billable Meta request.

**Not implemented:** model-driven goal selection, autonomous literature review, Lean Goldbach formalization, arbitrary Lean isolation. Passing this rehearsal **does not finish Mission 1**. The dedicated UI is pending; validation is via API and CI.

**Español:** [MISSION-001-OFFLINE-REHEARSAL.es.md](MISSION-001-OFFLINE-REHEARSAL.es.md).
