# ProofLab AI — Zone 1: three-view scientific master player

**Date:** October 28, 2026. **Mission:** Autonomous Physics Discovery (M2). **Scope:** synchronized video playback and local visual candidate overlays; **NOT actual SAM/Muse inference or validated physics**.

## Jury interaction

On entering ProofLab AI, **Pendulum** is now the default and the only visible sidebar navigation button. The existing mathematics, physics and Meta test pages, APIs and all source code have been **hidden but NOT deleted**. ES/EN languages remain available.

After the user sets string length, release angle, bob mass, and optional second trial, each available browser-compatible recording renders as a three-view laboratory using precisely **one HTMLVideoElement**:

1. **Main video**: the single video element displays the compatible original-video proxy and owns time, play/pause, seeking and speed.
2. **Motion vectors**: one canvas draws exactly that video element using `drawImage(video)`, adding the candidate pixel centroid and a visualization arrow calculated from changes of image coordinates in **px/s**. These are not calibrated tangential/angular physical velocities.
3. **Ghost trail**: a second canvas draws **the very same decoded video frame** and transparent circles for candidate positions in the preceding 1.5s. Missing 2D coordinates produce no invented motion.

**One clock**: `requestVideoFrameCallback` is the preferred rendering callback, with `requestAnimationFrame` fallback. Video `pause`, `seeked`, `loadeddata` and `timeupdate` redraw the two canvas views from the same media source. Controls include play/pause, common scrubber, single-frame backward/forward on the decoded **proxy frame grid**, and 0.25×/0.5×/1×/1.5×/2× speed. There are NOT three independent playback engines.

## Honest source fidelity and science limitations

- Original iPhone HEVC/MOV and Android MP4 sources remain **unaltered**, protected locally with source SHA-256. The browser currently displays a **640px-wide H.264 proxy sampled at 12 FPS**. **This is NOT 1080p60/4K native playback**, even when the source metadata correctly says 1920×1080/60 FPS. The interface explicitly distinguishes original metadata from actual proxy properties. Higher fidelity decoding/transcoding is future work.
- Current FFmpeg/local median-background tracker processes `480×270 at 12 FPS`. An optional backwards-compatible `LocalMotionTrack.PixelSamples` property now contains time, candidate X/Y centroid and approximate radius in local pixel coordinates. Existing x-only scientific period observations remain untouched. Candidate blobs may follow hands/shadows/other objects, therefore **NOT SAM 3.1 masks or verified physical bob positions**.
- Vectors visualize approximate **image-space velocity**, not physical angular or tangential velocity. The trail illustrates previously detected candidates, not actual bob afterimage photography.
- Older saved experiments lacking PixelSamples still show synchronized identical video panels but **no fabricated vectors/trails**. A locally reprocessed study/new upload is required to populate actual 2D tracking candidates, preserving original footage.
- The backend allows only the authorized localhost Angular origin via CORS for reading the proxy as canvas source. No Meta provider requests, billable calls or billing gate changes.

## Implementation and tests

- `ui/src/app/pendulum-master-player.{ts,html,scss,spec.ts}` — one video decoder, two synced canvases.
- `ui/src/app/pendulum-master-sync.ts` — temporal sample lookup, candidate pixel velocity, bounded ghost history and proxy-frame stepping.
- `src/ProofLab.Science/PendulumMotionTracker.cs` — optional backward-compatible 2D candidate point export.
- `ui/src/app/pendulum-director.{ts,html}` — use master player for one or two selected trials.
- `ui/src/app/app.{ts,html,scss}` — pendulum default, other navigation hidden, all code preserved.
- Local tests: **126/126 .NET Release**, **51/51 Angular ChromeHeadless**, Debug API and Angular production builds. The automated player tests assert a **single <video>**, both canvases drawing the **same source element**, shared timestamps, pause, proxy-frame stepping and honest no-data handling.

## Actual iPhone interoperability QA

After updating the authorized localhost API with all billable Meta switches OFF, a **separate QA study** (without falsely accepting human review) processed the user's existing `50CM_1.MOV` using the updated 2D local candidate tracker. Verified source: original HEVC MOV **1920×1080**, **59.94 FPS**, **45,708,115 bytes**, exact match to original SHA-256. The tracker produced **316 candidate X/Y points**, and the local H.264 proxy was ~1,666,406 bytes. HTTP `Range: bytes=0-1023` returned **206 Partial Content**, `video/mp4`, valid 1024-byte content range and `Access-Control-Allow-Origin: http://127.0.0.1:4300`. Research state remained **`AWAITING_HUMAN_BOB_REVIEW`**; no fake human sign-off. This checks ingestion/provenance/web compatibility, **not genuine SAM masks or actual physical bob identity of all 316 samples**.

**Next stages:** genuine SAM 3.1 bob masks and object identity, playback at actual native source quality/frame rate when decoders and resources allow, calibrated pivot and angular velocity, physics SQLite cache, actual evidence-led Muse Spark decisions, measurement uncertainty and five-stage bilingual reports. No paid Meta calls are authorized for this stage.

**Español:** [PENDULUM-ZONE1-MASTER-PLAYER.es.md](PENDULUM-ZONE1-MASTER-PLAYER.es.md).
