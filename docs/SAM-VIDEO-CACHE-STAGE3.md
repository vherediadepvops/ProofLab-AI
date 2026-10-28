# Stage 3 — SAM 3.1, safe caching and scientific playback

**October 28, 2026 · In progress.** The one-shot inference and cached replay workflow is implemented; **SAM has NOT yet run on the real pendulum video**, because paid requests on the main API remain disabled. This document states implementation and limitations, not verified physical discovery.

## Model responsibility and provenance

1. **SAM 3.1** produces frame-wise bob masks, persistent object identifiers and image-space candidates. An object detection does not independently certify physical bob identity.
2. **ProofLab deterministic engine** computes foreground-mask centroids, image trajectories and angular/tangential/radial derivatives, and renders vector and ghost-trail canvases. Muse does not measure or render these.
3. **Muse Spark** is intended to evaluate evidence, uncertainty and next-experiment hypotheses in Stage 5; it has not yet made a verified scientific decision here.
4. **Human review** checks the pivot, bob identity and uncertainty; provider responses do not self-approve scientific validity.

## Single inference followed by unlimited local replay

- `GET /api/science/director/pendulum/sessions/{id}/sam/{role}/status` checks a SHA-256-linked cache without contacting Meta. A prior incomplete attempt yields `PREVIOUS_ATTEMPT_REQUIRES_MANUAL_RECONCILIATION` and cannot auto-retry.
- `GET .../sam/{role}/preflight` reads genuine MOV decoded-frame PTS using FFprobe, never assuming nominal FPS or 12FPS browser proxy timestamps. This is free.
- `GET .../sam/{role}/evidence` requires an authentic backend provider archive with private receipt and verified hash/protocol/model. Manually imported JSON in the separate offline inspector NEVER drives physical overlays.
- `POST .../sam/{role}/analyze` is loopback-only, manually triggered and requires explicit confirmation of private-video transfer and one potentially billable SAM call, backend API key, owner-confirmed credits/approval, paid-request switch and request budget. Maximum **2,500 frames** and estimated **US$0.50** per request (NOT a provider account spending cap). Cache hits do not invoke Meta.
- The original MOV becomes a **private remux MP4, stream-copy without re-encoding**. We verify submitted MP4 frame count and normalized presentation timestamps match the original, otherwise abort. We hash both files. Meta Files API upload uses `purpose=user_data`, 24-hour expiration, followed by SAM `input_video.file_id` and streaming response. This provider path is implemented but NOT live-tested.
- `attempt-started.json` is durably written before outbound calls. After a completed provider response, `response.json` and `receipt.json` preserve provider response ID, model/prompt/protocol, hashes of original MP4 and response, source-frame PTS, timestamp and billable-frame usage when provided. Network faults require manual reconciliation, never blind retries.
- In the master player, authenticated cached SAM mask centroids become the source of motion vectors and ghost trails, with deterministic image-space derivatives. If a mask is missing or identity ambiguous, no synthetic SAM point is filled. Legacy local FFmpeg coordinates remain explicitly marked as **provisional controls NOT SAM**.
- Stage 4 will add **SQLite indexing** of this private archive, without a second SAM request.

## Verified LOCAL media QA (zero Meta requests)

A genuine stored 50 cm MOV/HEVC is **1920×1080, 30.496667 s, exactly 1,828 decoded frames**. The stream-copy MP4 has **the same 1,828 frames and 0 s maximum normalized PTS difference**, verified by FFprobe. At US$0.20/1,000 frames, the theoretical charge would be **US$0.3656**, NOT an invoice or confirmed charge.

Isolated local API smoke: status GET200 uncached, preflight GET200 (1,828 frames), evidence GET404, one run POST with matching original SHA and confirmations **blocked HTTP409** by paid Meta gates; `usedRequests=0`. No video sent to Meta and no scientific SAM evidence produced. All endpoints are loopback-only; do NOT make them public without authentication/security review.

## Remaining scientific gates

Verify Meta balance and privacy terms, run ONE explicitly authorized provider request with enabled safety gates, validate actual masks/PTS and bob identity on the original video, compare with human-labelled points and FFmpeg baseline, quantify uncertainty and document failures. A cached raw result is **not** scientific sign-off for Stage 2 or 3.

**References:** [SAM overview](https://dev.meta.ai/docs/sam/overview), [video prompts](https://dev.meta.ai/docs/sam/segmenting), [file expiry](https://dev.meta.ai/docs/file-handling), [pricing](https://dev.meta.ai/docs/pricing-rate-limits).

**Español:** [SAM-VIDEO-CACHE-STAGE3.es.md](SAM-VIDEO-CACHE-STAGE3.es.md).
