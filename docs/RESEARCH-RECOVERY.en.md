# Per-frame measurements and study recovery

Local implementation, 2026-10-30. Reviewing stored evidence requires no new provider inference.

## Use

1. Open **Laboratory**. A fresh visit or reload starts without an active session; no ID is created automatically.
2. Press **Generate recovery code**, then copy and save it. Creating this empty study enables the parameters and video upload.
3. Length, angle, mass and follow-up settings are saved under the same code before the first video is registered. The UI shows saving progress or a retry action on failure. Parameters become locked after registration.
4. **Test video** and the first personal upload keep the generated code. A subsequent New trial creates another study; the previous study remains recoverable with its own code.
5. Paste a code in **Already have a code?**, beside the generation action, and press **Search**. The previous Home entry remains available. Uppercase and surrounding whitespace are accepted.
6. The recovery progress bar remains until saved settings, media and results finish loading. Unknown codes display “No session was found”; connection failures display a separate retry message.
7. Both videos, verifiable results and the frozen prediction are restored. Active operations are polled every 2.5 seconds without resubmission. Interrupted operations require checking their state before continuing manually.

The code identifies a study, not an API token. Recovery needs the same API and persistent private data; it is not cloud synchronization. Incomplete file uploads must be selected again. Tokens are never stored by recovery; new paid work after a reload requires entering the token again.

Local PUT /api/science/director/pendulum/sessions/{id}/draft validates parameters and atomically replaces the draft model while preserving its ID. It returns 404 for missing studies and 409 after video, proposal or prediction registration. Frozen evidence is never rewritten.

## Measurement behavior

One moderately displaced suspension reference no longer rejects two agreeing references within the existing tolerance. Total displacement is bounded to three tolerances. All three still require visibility, confidence and authenticated provenance. Moving cameras, gross disagreement and predominantly inconsistent tracks remain inconclusive. Rejected reference ordinals are explicit; the pivot remains an estimate.

SAM positions are checked individually. An invalid frame has no angular value, while a subsequent valid frame regains θ. Angular velocity requires consecutive valid neighbors. No derivatives cross gaps and no missing observations are invented. Playback sampling tolerance is at most half the native frame interval.

Report V2 retains all raw centroids, labels excluded frame indices, threshold and raw residual. Period cycles crossing gaps are not used. Coverage and stability checks remain. The 95% interval covers repeatability only, not total uncertainty. Muse's worker shares the calculation; previously frozen observations and predictions are not rewritten.

## Persistence

GET /api/science/director/pendulum/sessions/{id}/recovery reads the session, verified results and a private operation journal. Immutable started/finished receipts contain IDs, stage, state, timestamps and process identity, never request bodies or credentials.

A submitted stage survives browser disconnection for up to 70 minutes, bounded by API shutdown. Recovery neither starts subsequent stages nor resends possibly billed requests. Unfinished operations after server restart are marked interrupted. Optional X-ProofLab-Operation-Id rejects duplicate execution of the same ID. Recovery cannot guarantee continuation inside the provider without stored recoverable evidence.

A frozen prediction pins its original baseline analysis. Missing or invalid evidence is reported, never silently replaced with a newer analysis of the same source. Existing files and SQLite records remain intact.

## Recovery entry verification (2026-10-30)

- 46 Angular UI/recovery/navigation tests passed in Edge.
- Seven local API checks with isolated storage: same code, restored parameters, invalid input, unknown code, post-upload locking, integrity and zero Meta consumption. Run tests/recovery-draft.integration.mjs with the absolute compiled DLL path and optionally an available port.
- Live Edge walkthrough: generate code, save 80 cm, reload without active session, search uppercase code, observe progress, restore 80 cm, search an unknown code, then restore a historical H.264 with 182 verified masks. Checked at 1440 and 540 px; no JavaScript exceptions.
- Angular/.NET builds pass. Angular warns about the ~748 kB initial bundle and ~11.5/10.2 kB styles exceeding 500/10 kB budgets.
- API 5391 and localhost:4300 updated. No Meta calls, counter 148→148, owner manual permissions preserved. No CI.

The earlier measurement verification below is historical; it was not repeated for this UI adjustment.

## Verification and limits

110 relevant Angular tests passed; 27 UI/recovery tests repeated after the final adjustment. Seven .NET tests cover durable state, isolation, interruption, browser disconnection, duplicates and frozen identity. Angular and .NET builds pass; Angular warns about a ~739 kB initial bundle and two styles slightly above 10 kB.

Stored real 80 cm evidence: 1,875 masks, one of three suspension references rejected, zero masks excluded, candidate period 1.798157 s from 16 complete/8 selected cycles. This is reanalysis, not a new physical experiment or independent accuracy certification.

Edge verified ID recovery, both videos, reload and frozen prediction without Meta POSTs. Original H.264 playback works. The isolated Edge profile cannot decode the MOV/HEVC image; stored measurements were verified offline. The historical H.264 fixture lacks a stored suspension and appropriately keeps angular values inconclusive.

No new Meta calls or CI. Owner manual permissions and usage counters preserved. This is not SAM/Spark training.

Español: [RESEARCH-RECOVERY.es.md](RESEARCH-RECOVERY.es.md).
