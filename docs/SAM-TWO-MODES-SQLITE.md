# ProofLab AI — Stage 4: two SAM modes and SQLite

**Date:** Oct 28, 2026 · **Mission:** Autonomous Physics Discovery / pendulum · **Status:** technical integration implemented; local validation uses synthetic data. No new Meta inference or scientific bob validation was performed.

## Modes and contract

| Selection | Behavior |
|---|---|
| **API Meta** (`api-meta`) | Requests a fresh SAM inference even when an earlier analysis exists. Never presents cache as a new execution. Requires explicit permission, credits and server admission; each execution keeps its own ID and history. |
| **API Meta + cache** (`api-meta-cache`, default) | Looks up exact video/model/protocol/prompt identity in SQLite and verifies the archived files. A HIT retrieves evidence without a key, billing approval or Meta request. A MISS only offers a manual call when the server permits it. |

The primary selector contains exactly these two options. The key is entered in **Settings**; local ports are under its **Advanced connection** section, closed by default; 5391 remains the main port. Selecting a port or pasting a key does not enable billing.

- `GET /api/science/director/pendulum/sessions/{id}/sam/{role}/status?mode=api-meta-cache` reads status. It also accepts `mode=api-meta`.
- `POST .../sam/{role}/manual-one-shot` and `POST .../sam/{role}/analyze` accept `mode`; omission preserves cache mode. The UI uses the first endpoint with the existing manual action.
- Status adds `analysisId`, `mode`, `cacheHit` and `storage`. A HIT retains `CACHED_META_...`. Fresh mode uses `READY_FOR_FRESH_META_ANALYSIS` where applicable; final admission remains subject to server controls.
- `GET .../sam/{role}/evidence` returns the latest verified evidence. The UI retrieves evidence through GET after Process is pressed or an authorized inference completes; checking cost or playing never starts a Meta POST.
- A completed fresh execution returns its ID and `cacheHit=false`. Earlier analysis metadata in status does not establish that a new execution occurred.

## Cost and Process button

Uploading/restoring a video or changing mode/port automatically performs local status and preflight GET requests. The screen always quotes sending the original video to Meta; valid cache also shows **US$0 for this operation**. This estimate is not an invoice. A match means exact source-file/model/protocol identity, not visual similarity between images.

**Process** stays visible. In cache mode, a HIT selects the existing analysis without a key or paid call; a MISS requires a key, consent and server authorization before one request. Meta API requires the same controls and requests a fresh analysis. Earlier attempts requiring reconciliation remain blocked.

Before Process is pressed in Laboratory, the preview is static: playback, frame stepping, timeline and review confirmation are locked. The returned source SHA and selected analysis ID must match before playback unlocks. Overlays use verified SAM masks exclusively; errors never fall back to local tracking. A genuine completed zero-detection result enables playback with a no-detections notice and no fabricated overlays. Changing video, trial, mode or port invalidates visible results.

**Flow validation (2026-10-28):** 77 focused Angular tests passed and production build succeeded; no new Meta inference or CI. POST tests are mocked; existing cache is checked locally. The initial bundle budget warning remains (569.90 kB against 500 kB).

## Explicit video selection and loading states

The Laboratory opens empty even with a remembered study. **Test video** loads the configured local recording only after a click. **New trial** replaces the two top-card actions with **Upload video** and **Cancel**. Cancel preserves the currently selected video and result; choosing a valid file creates another study and preserves earlier studies on the server. There is no duplicate primary upload control below. Loading a video never runs SAM.

For a local installation, copy `ui/public/demo-video.example.json` to `ui/public/demo-video.json` and enter an existing study ID with a video and browser preview. Restart the Angular server after creating this file. Git ignores this local configuration; never include private study IDs, recordings or tokens in the public example.

Preview loading and cost checking proceed independently once video metadata is available. Each card has its own indeterminate activity bar; no fabricated percentages. Process requires a ready preview and a completed valid estimate, including cache HITs. Both cache reuse and Meta processing remain busy through result validation; duplicate clicks and source/mode/port changes are blocked. Errors stop activity and leave playback locked.

Local validation of this change: 58 focused Angular tests and a successful build. Paid requests are covered with mocked HTTP only. CI is not required.

## Original playback and synchronization (2026-10-28)

The player uses `GET/HEAD .../original/{role}`: exact preserved-file bytes, verified SHA-256, Range and ETag. Codec, resolution and FPS are unchanged. New uploads no longer generate the 12 FPS review proxy; historical files and endpoints remain for compatibility. Local-analysis FFmpeg sampling is separate from playback.

All three derived views draw the same video at its native decoded resolution. Masks use `requestVideoFrameCallback.mediaTime`, including pause and seek; manual frame stepping uses verified source PTS when available and nominal cadence otherwise. Seeking just inside the target frame avoids selecting the preceding frame due to rounded PTS. Vectors and trails remain local calculations from SAM masks, not graphics returned by Meta.

Playback or codec failure shows an explicit error and original-download link; no silent reduced-copy fallback occurs. Browser-reported dropped frames are indicated. Preserving a 4K 120/240 FPS file does not guarantee every frame can be displayed on arbitrary hardware/screens; existing upload and budget limits remain in force.

Local verification: original 59.94 FPS HEVC, identical bytes/SHA, rotated 1080×1920 decoded image and three native-resolution views. 13 HTTP checks, 12 browser checks and 77 focused Angular tests passed; after the PTS rounding adjustment, all 33 affected tests passed again. API and UI builds succeeded. UI initial size: 587.11 kB (existing warning against 500 kB). Unit tests cover 4K dimensions and 120/240 cadence; no real 4K/240 recording performance benchmark was run. No CI or new Meta calls.

### SAM measurement geometry

Saved positions and pivots retain their compatible 480×270 grid for storage/drawing only. Angles, angular derivatives, radius and vectors first recover the decoded original's `videoWidth/videoHeight` proportions, including orientation. Portrait and square videos are therefore not measured as 16:9. Pixel speed uses original image pixels; viewport size and zoom do not change measurements. Historical masks and pivots are not rewritten.

This correction applies to the Laboratory SAM flow. Historical local-tracking records retain their own coordinate system and provenance. Angles still reference image vertical: physical vertical, perspective and metric calibration remain pending. No m/s values are enabled and observations are not fitted to ideal predictions.

Local validation: all 55 focused tests passed, including 15 combinations of −30°/0°/30° angles and portrait/landscape/square/4K dimensions; these 15 regressions failed before the fix. Integration covers decoded orientation, tangent/radius orthogonality and stable pivots/zoom. Build passed with the existing initial-size warning (587.59 kB / 500 kB).

### String angle from vertical

The Laboratory highlights θ in degrees and the side of inclination: negative to the left, positive to the right and zero on the vertical. The combined view adds a dashed vertical from the suspension point, the suspension-to-bob-centre axis and the angle arc; the original stays unannotated. Other existing values are under “More measurements · experimental”.

This estimates the string axis using the SAM centroid and the user-selected suspension point, rather than detecting the string independently. The reference is image vertical; physical interpretation requires a level camera perpendicular to the plane of motion. atan2(Δx, Δy) uses decoded-original proportions, without metres, declared string length or fitting to the ideal initial angle.

The reading follows the decoded frame and a nearby mask within half a nominal frame interval (at most 55 ms). Missing detection, missing suspension or a degenerate axis shows “—”. A gap never retains the previous value or substitutes local tracking. One valid centre supports an angle even without derivatives; zoom and viewport size do not change the reading.

Local validation: 66 focused Angular tests and a successful production build. Coverage includes −30°/0°/+30° in portrait, landscape 4K and square images, endpoints without derivatives, gaps, the Process gate, ES/EN signs and the unannotated original. Existing initial bundle warning: 595.62 kB / 500 kB. No new Meta calls or CI.

### Angular velocity beside angle

The card shows ω in rad/s and its °/s equivalent next to θ, updated from the same decoded frame. Positive ω means θ is changing to the right; negative means left. Bob position and motion direction are distinct. Changing playback speed does not scale the measured value.

It reuses the angular derivative of three SAM positions with original, potentially unequal timestamp intervals, retaining angle unwrapping and existing quality controls. A value requires a current angle and a valid derivative at that same centre. Endpoints without neighbours, gaps, invalid observations or missing detection show “—”; neither the ideal model nor local tracking supplies replacements. Measured zero remains zero rather than missing data. This is an image estimate with the same camera reference as θ.

Local verification: 70 focused Angular tests and a successful build, including motion sign independent of θ, unit conversion, zero, missing data, unequal intervals and ±π wrapping. Existing initial bundle warning: 599.14 kB / 500 kB. No assistant-initiated Meta calls or CI.

## Manual key and Process admission (2026-10-28)

The key registered in Settings satisfies the manual request credential requirement without duplicating it in the server MODEL_API_KEY. The server still checks its format, confirmed credits, owner approval, the billing switch and the shared request budget before reservation. Cache admission and final reservation recognize the same one-use key; it is neither persisted nor used to enable other calls globally.

Readiness exposes manualTokenCallsPermitted as the ability to admit a manual-token request, not proof of a valid key or account balance. Registering a key does not enable billing: Process also requires a valid video/quote, transfer and credit confirmations, and open server controls. The blocking message identifies the pending requirement. Repository defaults remain closed; an enabled session requires owner authorization. The two-request process budget, US$0.50 estimated per-video cap and previous-attempt reconciliation remain.

Local verification: 40 focused .NET and 49 Angular tests passed, API and UI built successfully. The existing initial bundle warning remains (589.65 kB / 500 kB). Paid transport is mocked in tests; no assistant-initiated live calls or CI.

## Persistence and provenance

`SamVideoEvidenceIndex` uses Microsoft.Data.Sqlite at `.private/physics/sam-video-evidence.sqlite`, separate from Lean mathematical memory. SQLite stores **metadata only**: video SHA, model/protocol, prompt hash, response and receipt hashes, private relative path, analysis ID, timestamp, frame count and provenance. It does not store keys, videos, masks, coordinates, plaintext prompts or complete responses.

`PendulumSamCache` connects this index to private response and receipt files. Each execution is saved under `.private/physics/sam-cache/<key>/runs/<analysisId>/`; new inference does not overwrite earlier archives. The backend rechecks identity, hashes, receipt and video timing before reuse. It can recover a complete archived result after an interruption without another provider request. An isolated SQLite row or manually imported JSON does not certify Meta provenance.

Identity includes original video SHA-256, `sam-3.1`, protocol `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2`, cache key and prompt hash. Paths are confined to the permitted private archive; identity or provenance conflicts are rejected.

## Authorization and concurrency

Private-video transfer, credits, billing approval, estimated cost limit, source SHA/frame/PTS and earlier-attempt checks remain in place. Server guards between processes prevent incompatible simultaneous requests; there are no automatic retries. Manual reconciliation of a possibly billed attempt still requires explicit acknowledgement. A failed new analysis returns `NEW_ANALYSIS_REQUIRES_MANUAL_RECONCILIATION` and does not enable the exceptional retry of the historical attempt.

The key is configured in the **Settings** password field, which stays mounted across navigation. Only its availability is shared with Laboratory; its value is read and cleared on manual execution and never persisted. Read-only GET checks do not consume the key. Switching mode or port clears consent and invalidates older checks. Such changes are blocked during submission. The JSON inspector has an independent request generation and cannot leave status checks or submissions stuck.

## Local verification

- Angular: **27/27 initial integration tests and 14/14 final manual-gate tests passed** (28 unique cases), using mocked HTTP: two options, GET-only HIT, MISS with closed gates, explicit consent before one POST, no retries, mode/port switches, stale responses, independent JSON inspection and GET player reload.
- `npm run build`: passed. Initial bundle warning: 528.07 kB against a 500 kB threshold; compilation succeeds.
- Backend: **181/181 .NET tests passed**, including 15 Stage 4 integration cases and 11 index cases (26 total). API build: **0 errors and 0 warnings**. Tests use synthetic files and transport, covering persistence, exact identity, historical archives, interruption recovery, conflicts, paths and execution controls.
- Validation runs on the authorized physical PC. **CI is manual and runs only when the user requests it**; publishing changes to GitHub does not require running CI.

These tests are not real pendulum segmentations. Video SAM inference, human bob identification, calibration and uncertainty remain pending. No new Meta spending or scientific discovery is claimed. Technical Stage 4 validation does not complete those scientific stages.

**Español:** [SAM-TWO-MODES-SQLITE.es.md](SAM-TWO-MODES-SQLITE.es.md).