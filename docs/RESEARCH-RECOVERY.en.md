# Per-frame measurements and study recovery

Local implementation, 2026-10-30. Reviewing stored evidence requires no new provider inference.

## Use

1. Open **Laboratory**. A fresh visit or reload starts without an active session; no ID is created automatically.
2. Press **Generate code**, then copy and save it. Creating this empty study enables the parameters and video upload.
3. Length, angle, mass and follow-up settings are saved under the same code before the first video is registered. The UI shows saving progress or a retry action on failure. Parameters become locked after registration.
4. **Use example video** and the first personal upload keep the generated code. Uploading another video 1 or choosing **Manage → New research** creates another study; the previous one remains recoverable with its code.
5. Open **Recover** (no active session) or **Manage** (active session), paste a code and press **Search**. Home keeps only “Open laboratory”; recovery is available inside Laboratory. Uppercase and surrounding whitespace are accepted.
6. The recovery progress bar remains until saved settings, media and results finish loading. Unknown codes display “No session was found”; connection failures display a separate retry message.
7. Both videos, verifiable results and the frozen prediction are restored. Active operations are polled every 2.5 seconds without resubmission. Interrupted operations require checking their state before continuing manually.

## Guided workflow

- **Prepare:** enter the physical measurement and upload video 1. The original plays without analysis overlays.
- **Analyze:** inspect the real quote before pressing Process. The default mode reuses verified cache; advanced options retain fresh and segmented analysis. Progress appears beside the cost while the original stays clean.
- **Understand:** review SAM layers, derived measurements and the available 95% statistical interval. Confirm tracking to continue. The scientific Muse consultation retains its own visible cost and consent.
- **Predict:** request or review the prediction registered by the server with Muse's response. Video 2 is enabled after that record exists; the browser does not simulate another freezing step.
- **Compare:** review measurements from video 2 and the comparison with the preserved prediction.

The active code occupies a compact bar with **Copy** and **Manage**. Closing a session does not delete records. Missing-evidence and interrupted-attempt warnings stay visible when the menu is collapsed.

Ramp and rocket are interactive ideal-model demonstrations. Video upload is marked **Coming soon**; they make no inference requests and are never presented as measurements. The rocket illustrates a ballistic phase without propulsion or flight control.

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

## Guided V8 design (2026-10-30)

- The pendulum is presented as Prepare, Analyze, Understand, Predict and Compare. Cost and consent precede processing, original source playback is separate from SAM layers, predictions use the actual persisted record, and code-based recovery stays compact. Ramp and rocket are explicitly labeled simulations with video upload disabled.
- Local verification: Angular development and production builds; 96 focused Edge tests passed (90 existing flow tests plus six additional tests). Non-blocking warnings: 826.82 kB initial bundle and 10.02/10.75/14.44 kB styles versus 500/10 kB warning budgets.
- Isolated Edge: empty laboratory, ramp and rocket at 1440 px; ramp and rocket at 390 px in English with no horizontal overflow. H.264 recovery could not be validated from the temporary UI origin because API 5391 does not allow that origin in CORS. The owner's API 5391, frontend 4300, and CORS settings were preserved. Recovery was checked by Angular tests, not new provider inference or physical recordings.
- Meta counter 148→148; no paid calls, CI or GitHub Actions. Git publication and local service deployment are separate tasks.
