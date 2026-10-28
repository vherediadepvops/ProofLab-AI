# ProofLab AI — Direct mobile video uploads (Mission 2)

**October 27, 2026 · first media interoperability increment.** A user or judge should upload original pendulum clips without preparing CSV files. **Accepting a video is NOT a guarantee of reliable measurements, nor proof of SAM 3.1 video tracking.**

## File and codec policy

Accepted file containers: **MOV, MP4, M4V, WebM, MKV, AVI, 3GP, 3G2** (case-insensitive). The real stream is independently inspected by **FFprobe** instead of trusting the extension or browser MIME declaration. Accepted codec policy: **H.264, HEVC/H.265, AV1, VP8, VP9, MPEG-4 Part 2, MJPEG, ProRes, H.263**. Actual decoder support depends on the installed FFmpeg build, not just this allowlist. Importantly, direct upload works even where Chrome itself cannot play an HEVC MOV file.

**Current limits:** 240 MB per video; 5–80 seconds; minimum 160×120; maximum 8192×8192 with up to 35.4 megapixels; source 5–240 nominal FPS. Up to three source clips per condition (50 cm and 80 cm), or six per preregistered study. Standard 1080p60/4K sources are downsampled to **480×270, normalized 12 FPS** by the local preliminary tracker, without sending all frames to Meta.

## First increment

1. The user freezes the 50→80 cm theoretical prediction with local UTC/ID/SHA-256 prior to submitting data (already implemented).
2. The **Flagship Demo · Pendulum** UI now uploads video files directly, without requiring native browser codec playback. The user, not the software, must decide whether a clip is from 50 cm or 80 cm.
3. `POST /api/science/pendulum/preregistrations/{id}/video/{condition}/{take}` uploads the file's **raw binary body** using a safe query `?filename=...`. Filename, byte size, video count, source hash and stream metadata are validated. No arbitrary path or executable is supplied by the user.
4. `VideoMediaDecoder` uses **FFprobe** metadata and a server-side **FFmpeg** child process: fixed command arguments, no shell, no network protocols and no Meta provider calls. Executables resolve from the operator's PATH, local WinGet Links or the explicit `Media:FfmpegDirectory`. Decoding limits frames, duration, concurrency and execution time. It also produces a local browser-compatible **JPEG thumbnail** from the original footage.
5. Experimental `PendulumMotionTracker` attempts deterministic median-background subtraction and extracts horizontal moving-object positions. **This is NOT SAM 3.1.** It cannot prove the tracked object is the actual bob. Motion blur, people, shadows or camera movement can produce incorrect observations. The UI shows the thumbnail, preliminary motion trace and how many frames had detections.
6. Original uploaded media and local JPEG/tracking metadata are kept only under the **gitignored** `src/ProofLab.Api/.private/physics/pendulum/video-evidence/` tree with a SHA-256 of source bytes. The prototype never claims automatic physical verification, never calls paid Meta APIs and can truthfully report **UPLOADED_NEEDS_MANUAL_TRACKING_REVIEW** when no reliable period is available.
7. Once manually reviewable trajectories exist for both lengths, `POST /api/science/pendulum/preregistrations/{id}/compare-videos` produces **preliminary descriptive** mean period/change across up to three trials per condition and sample spread where replicates exist. This is not scientific certification, SAM video tracking or novel physics.

**Additional endpoints:** `GET /api/science/pendulum/video-formats`, `GET /api/science/pendulum/preregistrations/{id}/videos`, `GET /api/science/pendulum/preregistrations/{id}/videos/{videoId}/poster`.

## What has actually been checked

- **Real iPhone 14 Pro Max media:** HEVC in MOV, 1920×1080, nominal ~60 FPS, 30.5 seconds, ~45 MB. Local test API accepted the raw upload and successfully probed/decoded it. The experimental motion tracker detected components in 316/366 sampled frames, **but it could not establish a credible period** and properly returned `UPLOADED_NEEDS_MANUAL_TRACKING_REVIEW`. This validates upload/decode, **not scientific tracking accuracy**.
- **Synthetic Android-like compatibility fixture:** locally generated H.264-in-MP4, 640×360, 8 seconds. Accepted by API; FFmpeg generated a JPEG thumbnail; poster returned HTTP 200 and the video ledger retained its entry. **This is not actual Samsung footage or a physical experiment.**
- **Synthetic WebM/VP9 fixture:** 320×240, six seconds, uploaded/decoded with a JPEG poster. Local tracker flagged manual review (it was not footage of a pendulum). Codec interoperability only, not physical evidence.
- Other formats on the software allowlist remain untested end to end. No Muse Spark successful inference or SAM 3.1 video motion pipeline is claimed.

## Operating/security constraints

FFmpeg is a **server dependency**; users do not need to convert files on the phone. It was installed only on the authorized `DESKTOP-ESGSLJH` via WinGet (`BtbN.FFmpeg.LGPL.8.1`, reported version 8.1.3). A judge-facing Internet service additionally requires FFmpeg, secure authentication, authorization, upload quotas, privacy review and lifecycle controls. **The local API is not yet safe for anonymous public video uploads.** Do not use footage containing identifiable people without appropriate consent.

Future work: integrate real **SAM 3.1** image segmentation on selected video frames and reuse the masks for locally tracked trajectories with timestamps, manual baselines and uncertainty checks. No paid Meta calls without verified credits and owner authorization.

## Milestone: period retrieval from all six genuine user videos (October 27, 2026)

The prior ascending-midline crossing detector could not yield periods for three 50 cm videos because local tracking contained discontinuities and early release artifacts. The new deterministic `PendulumSpectralEstimator` (`LOCAL_ROBUST_SINUSOID_V1_NOT_SAM`) **does not receive or use the theoretical length/period**. It uniformly excludes the first two seconds, applies a five-point moving median, 8th/92nd percentile clipping and a sinusoidal frequency sweep; checks R², amplitude, detection coverage and first/second-half agreement. Even an excellent periodic fit **might track a shadow, person or background**: visual verification of the real bob is required.

Legacy footage is reanalyzed using `POST /api/science/pendulum/preregistrations/{id}/videos/{videoId}/reanalyze` from **stored coordinates only**, without video re-upload/decoding or Meta calls. A new immutable `period-reviews/{videoId}-spectral-v1.json` audit record is linked to the original media hash; original records are **never overwritten**. `GET .../videos` joins this independent review, providing R² and half-study estimates. New uploads include the same preliminary local analysis.

**Actual local API smoke test using six genuine uploaded clips:** `50CM_1/2/3.MOV` estimated **1.4156 / 1.4172 / 1.4180 seconds**; `80CM_1/2/3.MOV` estimated **1.7976 / 1.7984 / 1.7996 seconds**. Group means **1.41693 s** and **1.79853 s**; preliminary observed increase **26.9314%**, versus frozen theoretical **26.4911%**, a difference of +0.4403 percentage points. All six passed the internal spectral consistency checks. **These are unverified local track-derived candidates, NOT authenticated physical bob measurements, SAM 3.1 outputs, or a new physics discovery.** Between-take spread is not a complete uncertainty budget.

Bilingual Angular includes **Reanalyze saved videos (no Meta)**, keeps old evidence intact and allows **Compare processed videos** once each condition contains candidates. No automated scientific approval is implied.

**Español:** [VIDEO-UPLOAD.es.md](VIDEO-UPLOAD.es.md).
