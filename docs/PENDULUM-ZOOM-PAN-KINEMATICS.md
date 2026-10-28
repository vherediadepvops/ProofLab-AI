# ProofLab AI — Draggable zoom and observed angular pendulum motion

**Mission 2 · Stage 2.** UI integration with the previously shipped deterministic `PendulumVideoKinematics` and SHA-linked pivot revision API. **Not SAM 3.1 inference or independent physical calibration.** ES/EN controls and provenance warnings.

## Zoom and drag

**Zoom image 1.6×** enables mouse/touch dragging of the zoomed image on all four perspectives, clamped to ±30% of the viewport so the image cannot drift permanently out of sight. **Fit image** restores 1× zoom and centered pan. All four views share the same pan offset for side-by-side inspection. Pointer events with capture preserve the single `HTMLVideoElement`, currentTime, playbackRate, measured samples and exact thumbnail swaps; a drag must not accidentally activate a thumbnail.

## Human pivot selection and measurements

When a study has actual stored local 2D video samples, **Select pivot** becomes available. It pauses the master, resets zoom/pan, expands the **Source video** panel and asks the human to click the physical suspension point. The UI maps the click into the local tracker coordinate plane **480×270**, accounting for `object-fit:contain` black letterboxing. Clicks outside the actual image fail.

The browser POSTs pivot pixel coordinates plus the original video SHA-256 to `/api/science/director/pendulum/sessions/{id}/kinematics/{baseline|followup}`. The backend writes an append-only revision without modifying original footage. The client accepts only matching study, role, SHA, format/version and nonmetric source data; GET restores the latest revision upon revisiting a study, with stale responses discarded when the trial changes. **Adjust pivot** creates another auditable revision, not an automatic human-validation signature.

Three derived canvases annotate the candidate with a yellow pivot/connecting radius. Only on frames with valid **observed local derivatives** are the direction/length-varying vectors drawn: cyan tangential velocity (image px/s), amber tangential acceleration (px/s²) and magenta centripetal acceleration (px/s², toward the pivot). The dashboard labels **OBSERVED · IMAGE CANDIDATE** for θ°/ω rad/s/α rad/s²/tangential speed px/s; missing valid samples become dashes. A separate **IDEAL MODEL · NOT OBSERVED** section shows theoretical center maximum angular speed and ideal speed evaluated at the current image angle. Ideal theory predicts maximum angular speed at the bottom and zero at ideal extremes; the estimator **does not force those results on video observations**.

## Scientific restrictions

2D tracking is **LOCAL FFmpeg background-subtraction candidates, NOT SAM 3.1**, without a verified physical bob identity or pivot until the human checks. The API explicitly fixes `physicalUnitsEnabled=false`. The UI NEVER presents calibrated metric XY positions or m/s or m/s² as measurements. Browser video remains H.264 **640px/12fps**, private originals and SHA-256 untouched; the image estimator's 12fps sample grid is not native camera PTS. Display coverage/quality and missing derivatives honestly, never invent samples or bill Meta.

## Verification and remaining acceptance

Angular headless tests: clamped mouse/touch pointer pan and fit without seeking, letterbox mapping, gap rejection, proper tangent/radial direction, separate ideal speed behavior, SHA-matched pivot GET/POST, and stale-response isolation between trials. Run full tests, production build and GitHub CI for the published SHA. The underlying API had already been smoke-tested with a real MOV (316 local candidate positions), but **the owner must still mark the actual suspension point and visually confirm vectors track the real bob**. Independent error assessment, original PTS and genuine consented SAM analysis remain outside this verified UI increment. Do not claim the scientific Stage 2 is fully complete.

**Español:** [PENDULUM-ZOOM-PAN-KINEMATICS.es.md](PENDULUM-ZOOM-PAN-KINEMATICS.es.md).
