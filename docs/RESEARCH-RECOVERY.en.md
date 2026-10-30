# Per-frame measurements and study recovery

Local implementation, 2026-10-30. Reviewing stored evidence requires no new provider inference.

## Use

Copy the study ID in the Laboratory heading. After a reload or lost connection, enter it in Home → Resume study by ID. The browser remembers the last ID. Configuration, both videos, verifiable results and the frozen prediction are restored. An active operation is polled every 2.5 seconds without resubmission.

The ID belongs to a study, not the temporary API token. Recovery needs the same local API and its persistent private data; it is not cloud synchronization. An incomplete file upload must be selected again. Tokens are never stored by recovery; new paid work after a reload requires entering the token again.

## Measurement behavior

One moderately displaced suspension reference no longer rejects two agreeing references within the existing tolerance. Total displacement is bounded to three tolerances. All three still require visibility, confidence and authenticated provenance. Moving cameras, gross disagreement and predominantly inconsistent tracks remain inconclusive. Rejected reference ordinals are explicit; the pivot remains an estimate.

SAM positions are checked individually. An invalid frame has no angular value, while a subsequent valid frame regains θ. Angular velocity requires consecutive valid neighbors. No derivatives cross gaps and no missing observations are invented. Playback sampling tolerance is at most half the native frame interval.

Report V2 retains all raw centroids, labels excluded frame indices, threshold and raw residual. Period cycles crossing gaps are not used. Coverage and stability checks remain. The 95% interval covers repeatability only, not total uncertainty. Muse's worker shares the calculation; previously frozen observations and predictions are not rewritten.

## Persistence

GET /api/science/director/pendulum/sessions/{id}/recovery reads the session, verified results and a private operation journal. Immutable started/finished receipts contain IDs, stage, state, timestamps and process identity, never request bodies or credentials.

A submitted stage survives browser disconnection for up to 70 minutes, bounded by API shutdown. Recovery neither starts subsequent stages nor resends possibly billed requests. Unfinished operations after server restart are marked interrupted. Optional X-ProofLab-Operation-Id rejects duplicate execution of the same ID. Recovery cannot guarantee continuation inside the provider without stored recoverable evidence.

A frozen prediction pins its original baseline analysis. Missing or invalid evidence is reported, never silently replaced with a newer analysis of the same source. Existing files and SQLite records remain intact.

## Verification and limits

110 relevant Angular tests passed; 27 UI/recovery tests repeated after the final adjustment. Seven .NET tests cover durable state, isolation, interruption, browser disconnection, duplicates and frozen identity. Angular and .NET builds pass; Angular warns about a ~739 kB initial bundle and two styles slightly above 10 kB.

Stored real 80 cm evidence: 1,875 masks, one of three suspension references rejected, zero masks excluded, candidate period 1.798157 s from 16 complete/8 selected cycles. This is reanalysis, not a new physical experiment or independent accuracy certification.

Edge verified ID recovery, both videos, reload and frozen prediction without Meta POSTs. Original H.264 playback works. The isolated Edge profile cannot decode the MOV/HEVC image; stored measurements were verified offline. The historical H.264 fixture lacks a stored suspension and appropriately keeps angular values inconclusive.

No new Meta calls or CI. Owner manual permissions and usage counters preserved. This is not SAM/Spark training.

Español: [RESEARCH-RECOVERY.es.md](RESEARCH-RECOVERY.es.md).
