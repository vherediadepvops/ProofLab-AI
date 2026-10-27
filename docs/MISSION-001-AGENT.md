# Mission 1 — Tool contract for a future Muse Spark agent

**2026-10-27 status: a local proposal validator has been implemented and tested. There is NOT YET an autonomous model-connected research loop.** Promotional credit verification and real inference are prerequisites.

## Planned supervised research cycle

1. Load research dossier and reviewed literature; declare the claim's current status.
2. Muse Spark proposes a precise question/subgoal and **one JSON tool action**.
3. Validate schema, field names, data types, bounds and research status. Reject arbitrary code and shell commands.
4. Call an independent tool only if allowed: (a) finite Goldbach observations or (b) a known theorem with a preapproved Lean tactic.
5. Persist proposal, validation decision, attempt, data, errors, hashes, model/version and timestamps, preserving negative evidence.
6. Return tool feedback to Muse for revision or an inconclusive conclusion; allow at most **3 tool calls per cycle**.
7. Produce five-stage ES/EN reports citing genuine attempt IDs, with **no automatic scientific approval** or ungrounded novelty claims.

## Allowlisted tools (available validator)

`CHECK_GOLDBACH_FINITE`:
```json
{"tool":"CHECK_GOLDBACH_FINITE","conjectureId":"GOLDBACH-STRONG","evenLimit":100}
```
Finite even numbers 4–2000 only, using deterministic C# computation, NOT Lean proof.

`VERIFY_KNOWN_BENCHMARK`:
```json
{"tool":"VERIFY_KNOWN_BENCHMARK","problemId":"NAT-ADD-COMM","strategy":"omega"}
```
Fixed theorems with `rfl`, `omega`, or `simp` only; arbitrary `leanSource` is rejected.

`ResearchAgentToolPolicy.ValidateProposal` blocks unexpected fields, shell commands, unsafe proof text, out-of-range parameters, fabricated IDs and unsupported tactics. `GET /api/math/agent/capabilities` reports **SCHEMA_VALIDATION_ONLY_NO_MODEL_INFERENCE**.

## Outstanding gates

- Confirm Meta credits and obtain approval **before** enabling billable traffic; keep `Meta:AllowPaidRequests=false`.
- Implement the coordinator using live Muse Spark decisions, with spending/iteration limits and model/prompt provenance.
- Validate a disposable sandbox before any free-form Lean execution.
- Independently review a correct Lean formalization of Goldbach; distinguish finite checks, known lemmas and the universal claim.
- Run blind evaluations against a baseline and have a human review the final report.

**Español:** [MISSION-001-AGENT.es.md](MISSION-001-AGENT.es.md).