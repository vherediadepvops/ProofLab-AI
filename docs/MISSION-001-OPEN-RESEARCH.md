# Mission 1 — First curated open conjecture

**Record:** 2026-10-27. **State:** catalog and bounded computational observations; NOT a formal proof or a new discovery.

## Problem GOLDBACH-STRONG

**Strong Goldbach conjecture:** every even integer greater than 2 can be written as the sum of two primes.
**Research status reviewed on 2026-10-27:** OPEN_RESEARCH (unsolved).
**Sources:**
- Wolfram MathWorld, [Goldbach Conjecture](https://mathworld.wolfram.com/GoldbachConjecture.html), accessed 2026-10-27. Distinguish the strong version from the solved ternary version.
- T. Oliveira e Silva, S. Herzog and S. Pardi, *Mathematics of Computation* 83 (2014), 2033–2060, [DOI:10.1090/S0025-5718-2013-02787-1](https://doi.org/10.1090/S0025-5718-2013-02787-1). Previous researchers computationally checked through 4×10^18: this is NOT a ProofLab contribution.
- [DeepMind Formal Conjectures](https://github.com/google-deepmind/formal-conjectures): useful statement-versioning practices and formalization-accuracy warnings. **ProofLab has NOT imported or validated a Lean Goldbach statement from this repository.**

## Bounded computational instrument (M1.1)

Endpoints: `GET /api/math/conjectures`, `GET /api/math/conjectures/GOLDBACH-STRONG`.
Once research stages 1–3 are approved: `POST /api/research/{id}/math/conjectures/GOLDBACH-STRONG/finite-check` with `{"evenLimit":100}`.
The limit must be even and between **4 and 2000**. A deterministic prime sieve finds a prime-pair witness for each even number in `4..evenLimit`.

Each observation is appended to a **private JSONL ledger**, recording attempt ID/time, version `goldbach-sieve-v1`, range, count, sample witnesses and SHA-256 hashes of the input and canonical summary. The summary hash excludes timestamp and random ID for independent replay. Retrieve with `GET /api/research/{id}/math/finite-checks`. Stages 4–5 and the consolidated scientific paper include ES/EN evidence.

**Important:** checking through 2000 cannot prove a universal conjecture, and is vastly below prior published verification. It is NOT Lean formal verification. This is bounded computational evidence designed to test tools, traceability and scientific honesty. The existing math-stage **real Lean attempt ID** requirement remains in force; these observations are supplementary.

## Five scientific stages and next agent loop

1. **Problem:** Goldbach, status review, DOI, limitations.
2. **Hypothesis:** universal statement, lemmas/examples, bounded falsifiable prediction.
3. **Protocol:** describe primality checks, range, version, replay instructions.
4. **Experiment:** execute bounded instrument; record witnesses, error, attempt ID and SHA; attach an independent known Lean control until an open-code sandbox is validated.
5. **Review:** acknowledge limitations, compare literature and explicitly state `INCONCLUSIVE` for the universal claim; propose a next experiment.

**Next phase (NOT implemented yet):** live Muse Spark proposes only allowlisted tool actions with iteration and spend budgets; an independent validator gates requests; untrusted Lean source runs only after an isolated runner and target formalization have been tested. No LLM-generated answer is a proof.

See [Lean security plan](LEAN-SANDBOX.md) and [Mission 1](MISSION-001-MATH.md).
