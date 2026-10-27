# ProofLab AI — Flagship pendulum demo (2026 competition)

**Status: first working increment, NOT the finished demo.** Built October 27, 2026 during the hackathon. The owner explicitly chose the pendulum as the project's **flagship** demonstration. This **supersedes D-004** in the private project memory, which described the pendulum as only a calibration control. The rolling sphere can remain a secondary control.

## Scientific demo goal

Judges should see **ProofLab Research Director** guide a supervised pendulum study: a genuine 50 cm video, SAM 3.1 visual measurements, independent motion analysis, a Muse Spark decision proposing an 80 cm experiment, a **prediction frozen BEFORE processing follow-up observations**, a real second-video comparison, and five bilingual evidence reports.

The key moment is not a graph: an agent must request a new, falsifiable experiment after reviewing evidence, commit to a quantitative prediction, and report uncertainty or inconclusive results honestly. **This complete workflow does not yet exist.** No successful Muse inference has been verified; SAM has segmented a synthetic red ball in a single image.

## Implemented first increment (0 Meta tokens)

- Deterministic prediction: `T = 2π√(L/g)` and `T₂/T₁ = √(L₂/L₁)`, reusing the existing `ScientificModels` physics engine.
- `POST /api/science/pendulum/preregister`, body `{"baselineLengthMeters":0.5,"followUpLengthMeters":0.8}`, saves a local UTC-stamped theoretical prediction, study ID and SHA-256 under `.private/physics/pendulum/`.
- `GET /api/science/pendulum/preregistrations/{id}` returns the frozen prediction and checks its SHA-256. This is NOT externally notarized or tamper-proof research provenance.
- `POST /api/science/pendulum/preregistrations/{id}/compare`: accepts two `EvidenceRequest` bodies under `baseline` and `followUp`, with matching `setup.kind="Pendulum"` and lengths, `origin`, `reference`, and 120–30,000 `observations` consisting of `{"timeSeconds":...,"position":...}` for each series.
- Observed periods come from position-based midline crossings, independently of predicted periods. Outputs measured/theoretical percentage changes and their percentage-point gap. Stores evidence privately with checksums, never claiming original discovery.
- New Angular **Flagship Demo · Pendulum** screen in ES/EN: enter lengths, preregister prediction, inspect timestamp/hash, load two CSV coordinate sequences (`time_seconds,position`; optional column header), compare.
- CSVs are **user-submitted data**, not authenticated physical video, regardless of their claimed source. Synthetic sequences remain labeled nonphysical.

**Illustrative predicted values, NOT measured evidence:** 50 cm → ~1.419 s; 80 cm → ~1.795 s; expected change ~26.49%. Agreement alone does not establish a new scientific discovery.

### Local reproduction

Use the authorized `DESKTOP-ESGSLJH` machine at `C:\PROOFLAB-AI-HACKATHON`, existing competition API `127.0.0.1:5391` and Angular UI `127.0.0.1:4300`. Open **Flagship Demo · Pendulum**, preregister 0.5→0.8 m, preserve its ID/hash, then supply both CSV files. For artificial input via API, explicitly use `origin="Simulated"`; the current UI conservatively sends `ManuallyEntered`.

These endpoints and UI make NO paid Meta requests and do not change `Meta:AllowPaidRequests=false`. The owner's optional 5393 temporary testing session is separate.

## Required before the real flagship demo

1. Film a **genuine** 300 g pendulum with pivot-to-bob-centre lengths of 0.5/0.8 m, fixed camera, real scale/time data, and three 20-second repetitions at each length; use a separate pilot for calibration.
2. Obtain a real successful **Muse Spark 1.3** response and diagnose the previous local HTTP502 through permitted owner-approved methods. The model must propose/justify the next length before the second trial; never retroactively change a frozen prediction.
3. Integrate actual **SAM 3.1** video-frame segmentation/tracking with frame timestamps, identities, masks, geometry and calibration. Preserve video source/hashes and negative evidence. A single successful segmentation is NOT video tracking.
4. Measure uncertainty, compare automated results to a manual baseline on identical video footage, and preregister the evaluation criterion before revealing follow-up evidence.
5. Integrate the five-stage Scientific Method Engine, model-led decisions, human scientific review and ES/EN evidence reports with genuine token/cost telemetry.
6. Complete complementary demos: controlled Lean 4 mathematical proofs and finite Goldbach study; time-blinded NASA Kepler data analysis; a bounded autonomous Research Director coordinating tools and requesting human measurements.

## Three-minute English video outline

- 00:00–00:20 — research initiated by the director.
- 00:20–01:20 — real 50 cm video → SAM → trajectory → measured period.
- 01:20–01:45 — Muse proposes 80 cm, freeze prediction, evaluate second video.
- 01:45–02:10 — known Lean theorem: accept and reject attempts.
- 02:10–02:35 — blinded historical data analysis with holdout.
- 02:35–03:00 — director shows decisions, evidence, limits, token budget and report.

**Never present an unimplemented video/agent step as working.** English submission materials must show authentic executions and limitations. Source of truth: `vherediadepvops/ProofLab-AI`. Never commit private experiment footage, credentials or datasets.

## Mobile-video interoperability increment — October 27, 2026

ProofLab now implements **direct video upload** for up to three baseline 50 cm and three follow-up 80 cm clips. The local API accepts MOV, MP4, M4V, WebM, MKV, AVI and 3GP/3G2 containers, checks actual codecs through FFprobe and decodes with server FFmpeg without any Meta provider calls. The server generates a browser-compatible JPEG thumbnail and an *experimental local* motion trace, retaining original bytes and source SHA-256 privately in the gitignored evidence directory. The user must assign clips to their actual length; coordinate CSV remains an advanced manual fallback.

**Actual codec-validation evidence:** a real 1920×1080, ~60 FPS, 30.5-second iPhone HEVC MOV was uploaded and decoded successfully. Local candidates were detected in 316/366 normalized sampled frames, **but no trustworthy period was established**: `UPLOADED_NEEDS_MANUAL_TRACKING_REVIEW`. A synthetic H.264 MP4 fixture successfully exercised the poster HTTP 200 path. This does NOT demonstrate SAM video tracking or universally reliable device/codec processing. See [VIDEO-UPLOAD.md](VIDEO-UPLOAD.md). A future public judge-facing deployment will require upload authentication, quotas and privacy controls.

**Español:** [FLAGSHIP-DEMO.es.md](FLAGSHIP-DEMO.es.md).
