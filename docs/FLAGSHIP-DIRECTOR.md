# ProofLab AI — Correct flagship jury experience (Mission 2)

**October 27, 2026 — guided one-or-two-video research journey.** **Update D-027:** the ideal nonlinear model computes three theoretical charts before upload. The user sets length/angle/mass and optionally enables a second length (local non-AI control or manual choice). One trial compares theory with a single video; two trials freeze the follow-up prediction before accepting the second video. Three additional plots from local video positions (px/px/s) are NOT SAM outputs. [Physical modeling and limits EN](PENDULUM-MATHEMATICAL-WORKBENCH.md).

## Why this change was needed

A judge must NOT be required to upload six videos. Three takes of each pendulum length are a useful **technical repeatability benchmark**, not the agreed central demonstration. The intended flagship starts with **one real video**. ProofLab analyzes it, requests a second experiment, **freezes a new prediction before receiving the second video**, then compares independent observations and reports uncertainty. The existing six-video laboratory remains available under **Advanced technical mode**, with earlier data preserved.

At the beginning the user sets **length, initial angle and bob mass**, chooses **one or two trials**, and examines three nonlinear theoretical plots **before uploading any video**. The user does not supply formulas or period values. An optional second length is a locally suggested control or human choice, never a fake Muse recommendation. [Mathematical workbench details](PENDULUM-MATHEMATICAL-WORKBENCH.md). A new **ProofLab Research Director** UI displays the initial local motion trace, requests explicit human confirmation, and advances step by step. **Muse Spark does NOT autonomously control this increment; SAM 3.1 does NOT yet track genuine video**. Neither simulated AI actions nor paid API requests are performed.

## What the current, Meta-free prototype actually does

1. `POST /api/science/director/pendulum/sessions` creates a persistent study from `baselineLengthMeters` (e.g. 0.5), **without recording any follow-up prediction yet**.
2. `POST /api/science/director/pendulum/sessions/{id}/video/baseline?filename=...` uploads one original phone video; FFprobe validates actual codec, FFmpeg decodes it, and experimental **local non-SAM background tracking** extracts a candidate period with the theory-independent `PendulumSpectralEstimator`. Source file, timestamp, SHA-256, coordinates, quality diagnostics and JPEG poster remain local.
3. `POST /api/science/director/pendulum/sessions/{id}/review-baseline` with `{"bobConfirmed":true}` records user acknowledgement after checking the trace follows the hanging bob instead of a hand/shadow. This self-attestation is **not** independent scientific verification.
4. `POST /api/science/director/pendulum/sessions/{id}/propose-local-control` records a **PRESET LOCAL CONTROL, NOT AN AI DECISION** — for instance, 0.50 → 0.80 m — with explanation, predicted period/change, UTC timestamp, source-video SHA-256 and a frozen SHA-256 prediction. The server rejects this step until after the initial real upload/review. The local timestamp establishes the order of **submitting evidence**, not when an unseen camera recording was made.
5. Only after step 4, `POST /api/science/director/pendulum/sessions/{id}/video/followup?filename=...` accepts **one second real video**. `POST /api/science/director/pendulum/sessions/{id}/compare` contrasts observed signal change with frozen theory and labels it `PRELIMINARY_LOCAL_COMPARISON_NOT_AI_SAM_OR_CERTIFIED`. The original video and prediction cannot be overwritten by subsequent requests.
6. `GET /api/science/director/pendulum/sessions/{id}` restores the study after browser refresh; `GET .../poster/{baseline|followup}` serves JPEG stills of original media. Every step is a separate create-once file under gitignored `src/ProofLab.Api/.private/physics/director/{id}`. No videos or secrets are uploaded to public Git or Meta.

## Browser-compatible full-motion review

FFmpeg locally produces a **lightweight H.264/MP4 review proxy** (640px-wide, about 12 fps) from HEVC/MOV footage, served via `GET /api/science/director/pendulum/sessions/{id}/preview/{baseline|followup}`. The web player includes normal playback controls so a human can check whether the tracked motion follows the actual bob rather than shadows or hands. Byte-range HTTP **206** responses are supported. Review proxies are **not substitutes for the unchanged original scientific input**, whose SHA-256 is kept. If a proxy cannot be generated, the JPEG poster remains and the UI explicitly requests a separate viewing of the original video.

**Actual local QA observation:** one original `50CM_1.MOV` iPhone HEVC/1080p clip was uploaded to a fresh QA session. The server returned `AWAITING_HUMAN_BOB_REVIEW`, local period candidate ~1.4156 s and R² ~0.970, an exact match to the source file SHA-256, and `previewAvailable=true`. The H.264 review proxy was ~1.67 MB and its endpoint answered HTTP **206** to `Range: bytes=0-1023`. No human sign-off or follow-up proposal was issued, and Meta was not called.

## What remains before claiming the original hackathon vision

- **True Muse Spark 1.3 scientific autonomy**: an actual audited inference that chooses the next experimental variable based on evidence and competing hypotheses, with an uncertainty-aware rationale, reproducible token usage and price. We have **not** verified this inference. Real paid model calls require the owner's independent confirmation of credits and explicit authorization; a local hardcoded control may never be presented as Muse AI.
- **Actual SAM 3.1 video observations**: cost-controlled frame sampling, real segmentation of the hanging bob, temporal identity tracking and validation against a manual baseline. Currently local FFmpeg/background subtraction only.
- **Uncertainty and evidence quality**: validate bob identity in footage, manually count cycles and quantify timestamp/frame calibration, segmentation failures and measurement uncertainty. A high spectral R² alone proves neither object identity nor original physics.
- **Five-stage Scientific Method Engine, bilingual reports and decision history**: these exist partly for mathematics but are not integrated with this new physics flow.
- **Mathematical Discovery support demo**: Lean 4 for known results, rejected/accepted tactics and bounded Goldbach checks exist. A Muse agent that reads Lean errors and chooses retry strategies **has not been verified live**. Do not advertise it as solved autonomous mathematics.

## Acceptance criteria for this increment

At the beginning the UI presents **one video picker** rather than six. After the first real upload, it shows a local track and requests human acknowledgement. Only then does a clearly labeled **local/non-AI** proposal become available, and it must be frozen before uploading the second video. The API rejects out-of-order calls, does not trigger Meta billing and preserves all scientific evidence. The previous 3+3 video calibration workflow remains accessible only in the advanced section. **The local API is not ready for anonymous public uploads.**

**Español:** [FLAGSHIP-DIRECTOR.es.md](FLAGSHIP-DIRECTOR.es.md).
