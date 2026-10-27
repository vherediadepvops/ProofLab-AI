# ProofLab AI — Persistent scientific experience memory (Mission 1)

**Local implementation, October 27, 2026 — EN.** Recalls verifier outcomes without model fine-tuning. The pipeline stays **offline without Muse Spark**, without billable API calls or arbitrary Lean code.

## Purpose

Reuse prior failed or successful Lean tactics across **distinct research dossiers**, reducing duplicate work while never replacing independent proof checking. A rejected tactic does not disprove a theorem. Initial benchmark `NAT-ADD-COMM` is a **known theorem**, not an open conjecture; Goldbach remains unsolved and never receives a remembered benchmark proof.

## Architecture

- `ScientificMemoryStore` uses pinned `Microsoft.Data.Sqlite 10.0.14` and creates `src/ProofLab.Api/.private/research/scientific-experience.sqlite` within the competition folder. The `.private/` tree is gitignored. No sharing with another project's SQLite or MySQL instance.
- `lean_experience` stores authentic constrained verifier outcomes with `attempt_id`, `research_id`, `problem_id`, exact formal-goal SHA-256, generated-source SHA-256, tactic, outcome, pinned Lean version and UTC time.
- Original `math-attempts/*.jsonl` evidence is preserved. SQLite is a derived local retrieval index, **not a tamper-proof scientific registry or certification**.
- `MemoryGuidedMathFlow` retrieves only matching `problemId + exact Lean declaration SHA-256 + leanprover/lean4:v4.35.0`. It cannot execute free-form Lean or transfer a tactic to an unrelated objective.
- Up to two tactics per memory cycle and three cycles per dossier; append-only local `memory-cycle-traces/*.jsonl` records recalled source IDs, fresh attempts, decision mode and outcome. Stage-4/5 and consolidated scientific reports include ES/EN appendices.

## Version-controlled SQL schema in the primary repository

- **Public SQL source:** [`src/ProofLab.Api/Database/001_scientific_experience.sql`](../src/ProofLab.Api/Database/001_scientific_experience.sql). It contains only the `lean_experience` table and index definitions: **no experiences, personal identifiers, or secrets**.
- The backend loads **the same SQL file as an embedded resource** via `ScientificMemoryStore` on startup. `CREATE ... IF NOT EXISTS` can initialize a new empty database while preserving existing rows. Future schema changes need new, reviewed versioned migrations rather than rewriting historical records.
- **Actual records:** `src/ProofLab.Api/.private/research/scientific-experience.sqlite` stays local and gitignored, as do JSONL evidence files. Committing a `.sql` schema is **not** publishing the `.sqlite` database.
- The initial SQL contains no fixture data or backups; protected private backup procedures are a separate concern.

## Test scenario

1. Study A, cold: `COLD_START` selects `rfl`; genuine Lean response `REJECTED` triggers `omega`, which is `VERIFIED`.
2. Independent study B: retrieve A's supporting attempt for the **exact same formal goal**, prioritize `omega`, and invoke Lean **again**, yielding a distinct evidence ID. The old proof is never copied as a new validation.
3. `NAT-ADD-ZERO` stays `COLD_START` without its own compatible history. `GOLDBACH-STRONG` is not a known Lean benchmark and is rejected for memory-guided verification.
4. Restarting the API preserves the experience, counts and source attempt IDs on disk.

## Endpoints and safeguards

- `GET /api/math/memory/{problemId}`: read a strategy suggestion with prior evidence source.
- `POST /api/research/{id}/math/memory-cycle`, body `{"problemId":"NAT-ADD-COMM"}`: run the memory-guided proof only in mathematics stages 4/5.
- `GET /api/research/{id}/math/memory-cycles`: review decisions and cited results. Stage approval still requires declared human documentation review and authentic Lean attempt references.

Only the existing curated `rfl`/`omega`/`simp` strategies and maximum 24 Lean attempts per dossier may execute. A failure, unavailable compiler or timeout is never a proof/disproof; `VERIFIED` on known benchmarks is never original discovery.

## Limitations

SQLite and JSONL are protected by local private paths and input validation, but **have no cryptographic signature or external attestation**. This is evidence-based retrieval, not LLM weight training, autonomous novel research, or general transfer learning. The dedicated memory UI and isolated executor for unrestricted Lean remain unimplemented. Operate only on `DESKTOP-ESGSLJH`.

**Español:** [SCIENTIFIC-MEMORY.es.md](SCIENTIFIC-MEMORY.es.md).
