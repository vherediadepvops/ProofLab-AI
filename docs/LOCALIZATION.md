# ProofLab AI — Bilingual response policy (ES/EN)

**Mission 1, Meta Global AI Developer Hackathon 2026 competition edition.**

## Implemented behavior

- The **ES / EN** switch controls UI text, Lean 4 verification explanations and mathematical test results, and Mission 1 UI warnings and errors. The chosen language is saved in the browser.
- **ES:** canonical `VERIFIED` appears as **VERIFICADO**, `REJECTED` as **RECHAZADO**; explanations for `rfl`, `simp`, and accepted proofs appear in Spanish.
- **EN:** results and explanatory text are presented in English.
- Switching language re-renders **the same verified result** without rerunning Lean or changing the evidence. Attempt IDs, tactics `rfl`, `omega`, `simp`, mathematical expressions, and SHA-256 hashes are never translated.
- The original technical Lean output, typically English, remains available for audit when a user expands the "Original technical diagnostic" section. It is not the default visible response.
- Reports exported with `?lang=es` or `?lang=en` include the Lean verification ledger in the requested language. **User-authored text** remains in the originally entered language: it is not silently machine-translated.
- Muse Spark receives an explicit instruction to respond in the active language; billable calls remain disabled until credits are confirmed. Language switching does not trigger paid model calls and clears old model prose rather than displaying it in a mismatched language.

## Mission 1 example

For the known commutativity theorem:

| Tactic | Spanish UI | English UI |
| --- | --- | --- |
| `rfl` | **RECHAZADO** — sides not definitionally equal, but theorem not refuted. | **REJECTED** — sides are not equal by definition, not a disproof. |
| `omega` | **VERIFICADO** — known theorem verified, not a discovery. | **VERIFIED** — previously known theorem, not a discovery. |
| `simp` | **RECHAZADO** — simplification could not establish the goal. | **REJECTED** — simplification did not prove the goal. |

The underlying technical compiler output can still be inspected without becoming the normal user-facing explanation.

## Limits

Localization currently covers the defined Lean outcomes and existing interface. Further scientific experiments and domain-specific validation errors require their own translations. User-authored dossier content is not automatically translated. Automated tests cover both languages; translation never changes the actual scientific verification.

**Español:** [LOCALIZATION.es.md](LOCALIZATION.es.md).
