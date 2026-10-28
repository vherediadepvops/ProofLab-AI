# ProofLab AI — Pendulum pivot API: restored service and real HTTP smoke

**Status:** local API verified. Built on the deterministic kinematics core in `ProofLab-AI@6d2b15a`. **Click-to-place pivot in Angular and rendering angular vectors remain NOT implemented.**

## Function and evidence provenance

- `POST /api/science/director/pendulum/sessions/{id}/kinematics/{role}` accepts `{"pivotXpx":240,"pivotYpx":40,"videoSha256":"<original video's SHA-256>"}`.
- Requires a genuine persisted study's `baseline` or `followup` with original source X/Y candidate data; refuses mismatched video SHA with HTTP 400.
- Calculates image angle (degrees), angular velocity (rad/s), angular acceleration (rad/s²), apparent image tangent speed (px/s), tracking quality and a SEPARATE ideal theoretical maximum.
- `GET .../kinematics/{role}` reads the most recent append-only private kinematics revision. Each stored entry is tied to the source video ID and SHA, with up to 20 create-once revisions per trial under gitignored `.private/physics/director/{id}/kinematics/{role}/`; original video and research records are never overwritten.
- The integration always sets `FrontalCameraAssumptionConfirmed=false`. **No measured calibrated physical XY coordinates or m/s are returned.** The manually supplied pivot is a provisional image marker, NOT a verified SAM segmentation.
- Zero Meta requests and no billable usage; service operates on localhost only.

## Actual verification on authorized DESKTOP-ESGSLJH

- The previous API on `5391` had been stopped to release an assembly lock. Its earlier restart request was blocked by security controls. **After explicit owner instruction to retry, the normal start request was allowed**, and `dotnet .\bin\Debug\net10.0\ProofLab.Api.dll --urls http://127.0.0.1:5391` successfully started with Meta paid-request gates disabled.
- `GET /api/health` **HTTP 200**, Angular web `4300` HTTP 200, `liveCallsPermitted=false`; owner's separate process on `5393` remained untouched.
- Real privately stored QA study `4ccbdae4897a41878d91ad8319b2a60b`, video `real_50cm_qa.MOV`, had **316 local X/Y candidate samples**. Before registration GET returned 404, wrong video SHA POST returned 400; correct-SHA POST with **QA-only provisional pivot (240,40)** created an immutable revision, and subsequent GET retrieved the same revision and original SHA. Result included **220 candidate derivative points**, `LOCAL_2D_CANDIDATE_REQUIRES_VISUAL_REVIEW` quality, `physicalUnitsEnabled=false`. On-disk original MOV SHA matched the study, and `humanReview` stayed null. **The pivot was chosen as a connectivity test, NOT validated against the real physical bob.**
- Following restoration, full test suites passed **136/136 .NET Release** and **62/62 Angular**. No private video, credential or unrelated Muse edits were committed or sent to Meta.

## Still required

1. Bilingual click-to-place pivot in the shared-clock video, POST/GET integration and per-frame visible angle/velocity/acceleration.
2. Genuine bob/pivot visual validation, original camera timestamps, uncertainty, and eventual SAM usage under separately confirmed/approved credits. Never present local image positions as calibrated metric measurements.
3. Verify CI and sync published commit to private bilingual technical memory. Preserve unrelated Muse worktree edits.

**Español:** [PENDULUM-PIVOT-API.es.md](PENDULUM-PIVOT-API.es.md).
