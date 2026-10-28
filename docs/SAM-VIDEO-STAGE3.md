# Stage 3 — SAM 3.1 video: first increment WITHOUT Meta calls

**Date:** 2026-10-28 · **Status:** VIDEO decoder and local viewer implemented; **NOT** a real SAM pendulum experiment. Stage 2 is technically integrated, but physical validation remains pending.

## What is implemented

- `ui/src/app/sam-video-evidence.ts` uses pinned official `@meta-sam/parser@0.1.0`, `parseVideoStream` and `decodeMaskToRaster`; no network requests.
- Accepts a complete `sam-3.1` Responses JSON and preserves `<Nf>` frame indices, stable object IDs, source-pixel boxes and masks, out-of-order frames and gaps, without filling missing observations.
- Computes the centroid from positive segmentation-mask pixels rather than assuming the bounding-box center; retains optional confidence, mask area, SVG path and source geometry.
- Rejects invalid grammar, duplicate object/frame entries, conflicting boxes/masks or dimensions, incomplete responses and excessive input. A valid empty result remains `NO_MATCH`.
- `ui/src/app/sam-video-replay.ts` introduces an on-demand **Stage 3 / Inspect SAM masks (no cost)** panel in the pendulum's Zone 1. The chosen JSON is parsed **locally in the browser**, and the masks are shown on a **neutral background**, not the real source frame.
- Switching videos resets an imported result; changing ES/EN language does not.

**Provenance limitation:** imported JSON is user supplied and **not authenticated**; the output has not been matched to the exact video or its SHA-256. Frame indices are **not PTS timestamps**. This feature proves neither real provider inference on the pendulum nor scientifically valid bob tracking. It is not a calibrated image-to-metre conversion.

## Free local inspection

1. Visit `http://127.0.0.1:4300/` and load/reopen a study containing video.
2. Expand **Stage 3 / Inspect SAM masks (no cost)** beneath Zone 1.
3. Select a previously obtained, complete SAM 3.1 video response JSON (never include credentials). The browser shows frame indices, IDs, mask-derived centroids and errors, without contacting Meta.
4. If no real video response exists, do not manufacture one. Unit tests use **synthetic parser fixtures only**.

## Next REAL test — the Stage 2/3 bridge

The shared acceptance test is to track the real bob and compare SAM detections with (a) local FFmpeg candidates and (b) human-annotated positions. Before a billable request:

1. Select a short genuine 50 cm pendulum clip with privacy consent; preserve original bytes/SHA-256 and record the controlled derivative clip, its hash and transformation.
2. Record exact **frame PTS for the submitted clip** with FFprobe and document how they map, or fail to map, to the original MOV PTS; never simply infer timestamps from index and nominal FPS.
3. Use one short object phrase (`metal pendulum bob`) **or** an annotated point/box prompt, not both.
4. Verify Meta credits, current pricing and privacy, and obtain specific permission for **third-party clip upload and one billable SAM request**. Keep `Meta:AllowPaidRequests=false` until granted. Video billing has a 50-frame minimum.
5. For real responses persist response/model/parser provenance, result hash and diagnostics privately (durable physics SQLite caching is planned for Stage 4).
6. Compare SAM centroids and human-marked locations in **identical frames and pixel spaces**; report distances and failures. Calibrated units, error analysis and Stage 2 scientific approval remain separate.

## Verification and guardrails

- Angular ChromeHeadless suite includes synthetic mask tests for frame ordering/gaps, IDs, invalid responses and switching source videos; check the exact commit for run counts.
- This increment makes **zero paid Meta requests**, writes no experimental video and does not claim authentic physical SAM tracking.
- Original MOV evidence, port 5393 and uncommitted Muse work are preserved.

**Official references:**
- https://dev.meta.ai/docs/sam/overview
- https://dev.meta.ai/docs/sam/segmenting
- https://github.com/meta-models/meta-sam/blob/main/protocol/sam3.md
- https://dev.meta.ai/docs/pricing-rate-limits

**Español:** [SAM-VIDEO-STAGE3.es.md](SAM-VIDEO-STAGE3.es.md).
