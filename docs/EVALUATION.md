# Evaluation — pendulum research workflow

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier**
**Evidence reviewed through 3 November 2026.** [Resultados en español](EVALUATION.es.md) · [Numerical results](RESULTS.md).

This document defines **what was evaluated**, the comparison protocol, controls, source separation, and what a reviewer can reproduce. It is not a claim that the system has established a new physical law. The ten-case research campaign was run on **30 October 2026**, before subsequent changes to the video-processing workflow.

## Research question

Can an AI-assisted laboratory use observations from a filmed pendulum to register a falsifiable prediction for a changed string length, then compare it with **independently measured** motion from a second recording without fitting the measured period to the prediction?

The familiar small-angle relationship is `T = 2π√(L/g)`. For a length change, the configured prediction is `T₂ = T₁√(L₂/L₁)`. This is **a test of the measurement and experimental workflow**, not a new theoretical discovery.

## Cases and controls (30 October campaign)

| Group | Cases | What was evaluated | Important qualification |
|---|---:|---|---|
| Existing real recordings | 8 | Three primary 50 cm → 80 cm pairs and five cross-pair comparisons built from six recordings (50CM_1/2/3 and 80CM_1/2/3) | Reuses the same six recordings and some SAM observations; **not eight independent filming experiments** |
| Deliberate negative control | 1 | A 50 cm video intentionally declared as an 80 cm follow-up | Tests whether the workflow identifies a disagreement, not ordinary tracking accuracy |
| Synthetic known-period control | 1 | Two simulated trajectories/videos with predefined periods | Software/geometry control, **not physical evidence** |
| **Total** | **10** | New Muse hypothesis and contrast decisions for each case | **Not ten independent prospective physical experiments** |

The six real recordings were **already filmed** before the hypotheses in this campaign. A prediction hash was frozen **before the follow-up video was uploaded to the app**, but this does **not** establish that the follow-up was filmed after prediction. No blind prospective filming claim is made.

## Protocol and decision boundaries

1. Register the original recording, declared apparatus parameters, source SHA-256, video timestamps and analysis identity. Keep original bytes separate from processed copies.
2. Ground the suspended object with Muse Spark; SAM 3.1 supplies video masks/tracks. Reject or mark uncertain missing, clipped or conflicting evidence rather than inventing observations.
3. Compute observed periods from original frame timestamps and tracked geometry. The period estimator does **not** receive the theoretical period as an input. Missing detections split usable intervals; no interpolation is made across tracking gaps.
4. Muse Spark evaluates the stored evidence, proposes a follow-up experiment or returns inconclusive. The numerical prediction is independently checked, frozen with identity/hash, and cannot be rewritten after follow-up upload.
5. Compare a separately authenticated second-video analysis with the frozen numerical prediction. The **predeclared practical comparison band** decides within/outside; uncertainty or inadequate suspension evidence can instead yield **inconclusive**. The band is **not** a statistical confidence interval.
6. Preserve the model attempts, decisions, negative outcomes and source linkage in private evidence records. Re-reading completed records does not request additional Meta inference.

Method contracts: [SAM period estimator](SAM-PERIOD.en.md), [Muse investigator and contrast](MUSE-INVESTIGATOR.en.md), [pendulum scientific protocol](EXPERIMENTS.md).

## What the controls do — and do not — establish

- **Positive comparisons:** show agreement with a predefined practical band under the observed tracking and declared lengths. Agreement does not validate all geometry, calibration or the physical law itself.
- **Wrong-length negative control:** exercises rejection of a deliberate metadata/physical mismatch. It is not evidence of a naturally occurring physical contradiction.
- **Synthetic control:** compares calculations with a known generated trajectory, but camera, suspension and object-identity errors in real videos are not thereby excluded.
- **Statistical intervals:** reported type-A uncertainties and later exploratory 95% intervals cover only stated sources of variation. Pivot, perspective, length, clock and systematic tracking uncertainties are not fully propagated.

## How Meta usage and campaign counts are reconciled

The [results register](RESULTS.md) now distinguishes the **provider Usage requests** from **recorded model inferences**, **scientific cases** and **software tests**. These are different units:

- **Provider requests:** the owner's screenshot showed **550** in the selected 28 October–3 November 2026 range under **All API keys / All models**. That graph is account/project usage, not a machine-readable, per-request ProofLab audit. A provider request can be a token count or a failed attempt; it is not necessarily a completed experiment.
- **Model-inference records:** in the private `fast-eight` JSON ledger, grouping `calls[].count` gives **19 Spark + 94 SAM = 113**; in the private `full-science` JSON ledger, **16 Spark + 66 SAM + 20 Muse = 102**. The two tables were inspected read-only on the authorized machine. These counts describe **those two ledgers only**, not a verified union across all campaigns or a billing invoice.
- **Known nested campaign totals:** **83** earlier segmented-video inferences are included in the later **113** total; the **30** initial 3 November inferences are included in the later **42**. Do not add a subtotal to its parent total. The 102-inference campaign reused six source recordings to create eight comparisons and two controls; samples are not independent.
- **Other campaigns:** early Muse validation, 12 automatic-video inferences, five pivot-refinement calls, five body-validation calls and 3 November's seven technical pipelines have dated records, but provider request IDs have **not** been reconciled across all campaigns; therefore the reported rows cannot be summed into an authoritative all-time total.
- **Software tests:** passing assertions in .NET/Angular are run repeatedly on different commits and sometimes use mocks. Neither test cases nor test-run totals should be added to the model-request count.

**Safe deduplication rule for a future complete audit:** gather a permitted provider usage export (time, project/key/model, request ID, response ID, status and request type), correlate it with the private attempt receipts by immutable provider identity and timestamp, account for token-count/failed/cached requests, and retain a separate unmatched bucket. Do **not** deduplicate distinct paid calls simply because their source-video SHA-256 matches. Never publish raw provider receipts, credentials, private user footage or database rows.

**Current audit status:** the screenshot and ledgers support the individual figures above, but a full **550-to-ledger reconciliation remains unverified**. This is an accounting/provenance gap, not evidence that any 550 requests were all successful research experiments.

## Reproduction and evidence access

**Publicly inspectable:** this protocol, [results](RESULTS.md), implementation/tests in [`tests/ProofLab.Tests`](../tests/ProofLab.Tests), [workflow tests](../tests/ProofLab.Workflow.Tests), and the published period/Muse evaluation methods. They allow review of the logic and offline fixtures. A user can run the relevant .NET and Angular tests locally with the prerequisites in the [README](../README.md); doing so does **not** repeat the live experimental campaign.

**Not public:** original user recordings, raw Meta provider replies, tokens/credentials, receipts, private SQLite data, and the full run ledger. They remain private for consent and security. Consequently, **a third party cannot independently replay all ten original cases from the public repository alone**. Public numeric summaries are not a substitute for accessible raw measurements.

The documented 3 November release [`b651232`](https://github.com/vherediadepvops/ProofLab-AI/commit/b6512323e7f72f0d718af30be648c8a59baaf648) passed focused local software and production UI checks, **without running new Meta inferences**. Later commits may alter software behavior; this historical evaluation is not a fresh validation of every subsequent revision.

## Known gaps before stronger scientific claims

- Independent physical ground truth for pivot position, object centre and camera geometry across real videos.
- An uncertainty budget covering string length, perspective, clock drift and systematic segmentation bias.
- New provider-backed end-to-end tests of the 3 November background preparation and no-zoom review flow.
- A genuinely prospective second filming session after a committed prediction, with a separately timed manual baseline.
- A public, consent-cleared or synthetic reproducibility pack sufficient for third-party replay without sharing private footage.

**Outcome tables:** [RESULTS.md](RESULTS.md). No new experiments or Meta API calls were made to write this report.
