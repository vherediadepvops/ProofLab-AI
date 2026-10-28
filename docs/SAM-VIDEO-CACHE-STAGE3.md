# Stage 3 — SAM 3.1, safe caching and scientific playback

**October 28, 2026 · In progress.** The one-shot inference and replay cache workflow is implemented. One explicitly approved real attempt was subsequently made: Meta Files and Responses returned initial HTTP 200, but ProofLab returned HTTP 502 after approximately 116 seconds, without saved masks. Visible free credits changed from USD 149.99 to USD 149.62, not proof of exact per-request usage. **There is NO scientifically verified pendulum segmentation**; paid access in the main API remains disabled.

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
- The original MOV becomes a **private remux MP4, stream-copy without re-encoding**. We verify submitted MP4 frame count and normalized presentation timestamps match the original, otherwise abort. We hash both files. Meta Files API upload uses `purpose=user_data`, 24-hour expiration, followed by SAM `input_video.file_id` and streaming response. The provider upload/inference path was tried once live (initial HTTP 200 for Files/Responses), but no complete usable segmentation was received. The corrected stream reader has only been tested offline.
- `attempt-started.json` is durably written before outbound calls. After a completed provider response, `response.json` and `receipt.json` preserve provider response ID, model/prompt/protocol, hashes of original MP4 and response, source-frame PTS, timestamp and billable-frame usage when provided. Network faults require manual reconciliation, never blind retries.
- In the master player, authenticated cached SAM mask centroids become the source of motion vectors and ghost trails, with deterministic image-space derivatives. If a mask is missing or identity ambiguous, no synthetic SAM point is filled. Legacy local FFmpeg coordinates remain explicitly marked as **provisional controls NOT SAM**.
- Stage 4 will add **SQLite indexing** of this private archive, without a second SAM request.

## Pre-inference LOCAL media QA (at that point, zero Meta requests)

A genuine stored 50 cm MOV/HEVC is **1920×1080, 30.496667 s, exactly 1,828 decoded frames**. The stream-copy MP4 has **the same 1,828 frames and 0 s maximum normalized PTS difference**, verified by FFprobe. At US$0.20/1,000 frames, the theoretical charge would be **US$0.3656**, NOT an invoice or confirmed charge.

Isolated local API smoke: status GET200 uncached, preflight GET200 (1,828 frames), evidence GET404, one run POST with matching original SHA and confirmations **blocked HTTP409** by paid Meta gates; `usedRequests=0`. No video sent to Meta and no scientific SAM evidence produced. All endpoints are loopback-only; do NOT make them public without authentication/security review.

## SSE diagnostics, timeouts and the one real attempt

The single approved live test after local QA received **Files HTTP200 and initial Responses HTTP200**, but the ProofLab endpoint later returned **HTTP502** without a usable terminal response. The old SSE event sequence was not captured. Free-credit movement USD149.99 → USD149.62 does not establish billed frame count. The private `attempt-started.json` marker **remains locked** against automatic retry.

Subsequent diagnostic hardening:
- SAM video HTTP timeout **15 → 30 minutes**, plus an explicit **linked 30-minute cancellation deadline for the SSE body**: with `ResponseHeadersRead`, `HttpClient.Timeout` alone does not cover subsequent body reads. Client disconnects can still cancel earlier. Other Meta requests (`meta`) **45 → 180 seconds**. This does NOT establish timeout as the cause of the previous HTTP502.
- Separate testable `SamVideoSseParser` handles output deltas, text-done fallback, completed, failed/incomplete, keepalives, EOF, and safe size bounds. A legitimate completed **zero-match** response is `NO_MATCH`, never an HTTP502 just because there are no masks.
- Future explicitly authorized attempts will write private `attempt-telemetry.jsonl` next to the durable attempt marker: UTC timestamp, whitelisted stage, elapsed milliseconds, event/delta counts and output character counts. **No API keys, provider file IDs, prompts, SSE payloads, original footage or masks are logged.** Progress snapshots occur every 25 deltas and at completion/failure. Previous SSE diagnostics cannot be reconstructed.
- Angular differentiates a completed valid no-match from invalid cache and does not fabricate SAM vectors or ghost points.
- Offline-only synthetic .NET/Angular tests verify these cases. Existing marker and paid gates remain unchanged.

## Remaining scientific gates

Verify actual Meta Usage and privacy terms; only consider another provider request after NEW explicit authorization and permitted safety gates, validate actual masks/PTS and bob identity on the original video, compare with human-labelled points and FFmpeg baseline, quantify uncertainty and document failures. A cached raw result is **not** scientific sign-off for Stage 2 or 3.

**References:** [SAM overview](https://dev.meta.ai/docs/sam/overview), [video prompts](https://dev.meta.ai/docs/sam/segmenting), [file expiry](https://dev.meta.ai/docs/file-handling), [pricing](https://dev.meta.ai/docs/pricing-rate-limits).

**Español:** [SAM-VIDEO-CACHE-STAGE3.es.md](SAM-VIDEO-CACHE-STAGE3.es.md).


## One expressly approved manual second attempt, with retained history (Oct 28, 2026)

After the first attempt returned HTTP502 and the owner supplied Billing screenshots showing an approximate **USD 0.37 free-credit decrease** (the exact per-call Usage is not independently confirmed), the owner explicitly authorized **one additional experiment**. A bounded, auditable reconciliation mechanism was added:

- `POST .../sam/{role}/analyze` never auto-retries. Only a loopback operator request with `ownerApprovesOneReconciledRetry=true` and `previousAttemptBillingEvidence` matching the documented `ManualRetryReconciliationPolicy.OwnerAuthorization` may admit one manual retry, if all paid-access gates and request-budget checks pass.
- Previous `attempt-started.json` is **never deleted or moved**. Before a second provider transfer, exclusive creation of `manual-retry-started.json` occurs. Any existing manual-retry marker prohibits a third attempt, including across process restarts.
- Original `sam-upload.mp4` is reused **without new encoding**, after comparing every PTS and checking its SHA against the initial attempt record. No private videos are published.
- Second-attempt sanitized telemetry uses `manual-retry-telemetry.jsonl`; it neither overwrites nor fabricates the previous run's events. A successful provider response would be saved with a private hash-linked receipt.
- A payment-disabled local smoke test verified HTTP409 both for incorrect reconciliation and for correct owner acknowledgment without billing permission; no second marker or provider call was created.

New owner approval **does not prove the precise billing of the first attempt** and does not authorize further calls.

## Manual Zone 1 control: ephemeral key and explicit credit confirmation (Oct 28, 2026)

When a study has video, **Stage 3 · SAM 3.1 and persistent evidence** is visible immediately below the pendulum configuration. The operator selects an **already authorized local API process** (allowlisted ports 5391, 5393 or 5398), performs a free cache/frame/permission check, then enters the Model API key in a protected password field. Its contents are **never saved to Angular signals, localStorage, sessionStorage, SQLite, source files, or logs**. The value is read only on a click and cleared from the input before submitting exactly one backend request: `POST .../sam/{role}/manual-one-shot`, restricted to loopback, browser origin `127.0.0.1:4300` or `localhost:4300`, custom anti-CSRF `X-ProofLab-Manual-Action` header, a 4 KiB body limit, and `Cache-Control: no-store`. The browser NEVER sends credentials directly to Meta: the backend uses HTTPS to `https://api.meta.ai/v1`.

To perform a test, the operator confirms the private-media transfer, clicks **Confirm Meta credit usage**, then **Run ONE manual SAM 3.1 test**. An existing failed attempt additionally requires acknowledgment of the prior billing and approval of ONE further attempt. No automatic retries. The request preflight retains its estimated US$0.50/2,500-frame bound, which is NOT a provider-enforced spending cap; a second durable attempt marker prevents any third call without deleting prior evidence.

**Mandatory security gate:** pasting a key NEVER turns on server-paid access. A previously approved backend session still needs `Meta:CreditsConfirmedByOwner=true`, `Meta:OwnerApprovedPaidRequests=true`, `Meta:AllowPaidRequests=true`, a backend environment key as a defense-in-depth prerequisite, and available budget. Normal API port 5391 remains paid-disabled; the port chooser can only point to localhost, not external services. This UI MUST NOT be used to bypass a tool or organization security restriction.

**Credential risk:** Meta recommends storing keys in backend environment variables or a secret manager. Even with a password field and no persistence, entering a key in a browser increases exposure to extensions and developer tools. Only use the owner's trusted PC and a dedicated, revocable key; never paste the key into ChatGPT, logs, screenshots or code. No model call happens until the user presses the final button with backend gates already enabled.

**Sanitized private logs:** future authorized requests record safe stages (`UPLOAD_STARTED`, `UPLOAD_HTTP_ACCEPTED`, `INFERENCE_REQUEST_STARTED`, `INFERENCE_HTTP_ACCEPTED`, `SSE_DELTA_PROGRESS`, `SSE_COMPLETED`, failure categories and receipt committed), timestamp, elapsed milliseconds, event counts and output character count. Second attempt `.private/physics/sam-cache/<cache-key>/manual-retry-telemetry.jsonl`; a first attempt uses `attempt-telemetry.jsonl`. No bearer keys, raw prompts, provider file IDs, source images/coordinates/masks or SSE payload. The previous first attempt predates this telemetry. Free preflight does not contact Meta or create a new attempt marker.
