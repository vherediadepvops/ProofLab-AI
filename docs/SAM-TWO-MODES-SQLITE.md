# ProofLab AI — Stage 4: two SAM modes and SQLite

**Date:** Oct 28, 2026 · **Mission:** Autonomous Physics Discovery / pendulum · **Status:** technical integration implemented; local validation uses synthetic data. No new Meta inference or scientific bob validation was performed.

## Modes and contract

| Selection | Behavior |
|---|---|
| **API Meta** (`api-meta`) | Requests a fresh SAM inference even when an earlier analysis exists. Never presents cache as a new execution. Requires explicit permission, credits and server admission; each execution keeps its own ID and history. |
| **API Meta + cache** (`api-meta-cache`, default) | Looks up exact video/model/protocol/prompt identity in SQLite and verifies the archived files. A HIT retrieves evidence without a key, billing approval or Meta request. A MISS only offers a manual call when the server permits it. |

The primary selector contains exactly these two options. Local ports are under **Advanced connection**, closed by default; 5391 remains the main port. Selecting a port or pasting a key does not enable billing.

- `GET /api/science/director/pendulum/sessions/{id}/sam/{role}/status?mode=api-meta-cache` reads status. It also accepts `mode=api-meta`.
- `POST .../sam/{role}/manual-one-shot` and `POST .../sam/{role}/analyze` accept `mode`; omission preserves cache mode. The UI uses the first endpoint with the existing manual action.
- Status adds `analysisId`, `mode`, `cacheHit` and `storage`. A HIT retains `CACHED_META_...`. Fresh mode uses `READY_FOR_FRESH_META_ANALYSIS` where applicable; final admission remains subject to server controls.
- `GET .../sam/{role}/evidence` returns the latest verified evidence. After a HIT or completed execution, the UI reloads the player through GET; replay never initiates a POST.
- A completed fresh execution returns its ID and `cacheHit=false`. Earlier analysis metadata in status does not establish that a new execution occurred.

## Persistence and provenance

`SamVideoEvidenceIndex` uses Microsoft.Data.Sqlite at `.private/physics/sam-video-evidence.sqlite`, separate from Lean mathematical memory. SQLite stores **metadata only**: video SHA, model/protocol, prompt hash, response and receipt hashes, private relative path, analysis ID, timestamp, frame count and provenance. It does not store keys, videos, masks, coordinates, plaintext prompts or complete responses.

`PendulumSamCache` connects this index to private response and receipt files. Each execution is saved under `.private/physics/sam-cache/<key>/runs/<analysisId>/`; new inference does not overwrite earlier archives. The backend rechecks identity, hashes, receipt and video timing before reuse. It can recover a complete archived result after an interruption without another provider request. An isolated SQLite row or manually imported JSON does not certify Meta provenance.

Identity includes original video SHA-256, `sam-3.1`, protocol `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2`, cache key and prompt hash. Paths are confined to the permitted private archive; identity or provenance conflicts are rejected.

## Authorization and concurrency

Private-video transfer, credits, billing approval, estimated cost limit, source SHA/frame/PTS and earlier-attempt checks remain in place. Server guards between processes prevent incompatible simultaneous requests; there are no automatic retries. Manual reconciliation of a possibly billed attempt still requires explicit acknowledgement. A failed new analysis returns `NEW_ANALYSIS_REQUIRES_MANUAL_RECONCILIATION` and does not enable the exceptional retry of the historical attempt.

The key is read only on manual execution and cleared from the field; it is never persisted. Switching mode or port clears consent and invalidates older checks. Such changes are blocked during submission. The JSON inspector has an independent request generation and cannot leave status checks or submissions stuck.

## Local verification

- Angular: **27/27 initial integration tests and 14/14 final manual-gate tests passed** (28 unique cases), using mocked HTTP: two options, GET-only HIT, MISS with closed gates, explicit consent before one POST, no retries, mode/port switches, stale responses, independent JSON inspection and GET player reload.
- `npm run build`: passed. Initial bundle warning: 528.07 kB against a 500 kB threshold; compilation succeeds.
- Backend: **181/181 .NET tests passed**, including 15 Stage 4 integration cases and 11 index cases (26 total). API build: **0 errors and 0 warnings**. Tests use synthetic files and transport, covering persistence, exact identity, historical archives, interruption recovery, conflicts, paths and execution controls.
- Validation runs on the authorized physical PC. **CI is manual and runs only when the user requests it**; publishing changes to GitHub does not require running CI.

These tests are not real pendulum segmentations. Video SAM inference, human bob identification, calibration and uncertainty remain pending. No new Meta spending or scientific discovery is claimed. Technical Stage 4 validation does not complete those scientific stages.

**Español:** [SAM-TWO-MODES-SQLITE.es.md](SAM-TWO-MODES-SQLITE.es.md).
