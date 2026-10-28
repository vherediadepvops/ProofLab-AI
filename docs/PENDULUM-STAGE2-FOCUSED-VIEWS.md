# ProofLab AI — Four synchronized perspectives with interchangeable hero view

**Mission 2 / Stage 2 · Owner-approved UI improvement.** This is a presentation/synchronization increment, NOT a validated scientific discovery, metric calibration or real SAM 3.1 video tracking.

## User experience

- On desktop, a **70% main view / 30% sidebar**. Default hero is **Vectors + ghost trail**, with three selectable thumbnails at right: **Original video**, **Motion vectors**, **Ghost trail**.
- Clicking **Expand** or a thumbnail **swaps that exact thumbnail position with the previous hero**; the other two thumbnails retain their positions. None of the four display elements is unmounted; choosing a view does not restart/pause/seek the actual video.
- Exactly **one HTMLVideoElement and three derived canvases**, sharing the decoded master frame and playback timeline. Pause, scrub, frame stepping and 0.25×–2× playback remain controlled by the master clock.
- **Zoom image 1.6× / Fit image** adjusts presentation of all panels uniformly; zoom may crop image edges but **never changes stored samples or physical units**.
- Responsive: desktop thumbnails right, narrow screens below the primary. Controls and status are bilingual ES/EN. Initial three placeholders are retained until a file is uploaded.

## Scientific boundaries

Cyan arrows denote LOCAL APPARENT image-space velocities (**px/s**) computed from the experimental background tracker. Violet trails are past candidate image positions over 1.5 seconds. Overlay combination does **not** prove bob identity; missing samples do not generate fictional vectors. **NOT SAM 3.1, NOT metric XY, NOT calibrated angular physics.** The browser preview remains H.264 at **640px / 12FPS**; private original MOV/MP4 bytes and hashes are unchanged.

## Verification

ChromeHeadless regression tests cover **one video + three canvases**, default combined hero and three thumbnails, **exact slot swapping** without changing the source video node, current time or playback rate, synchronized draws from the single video, and zoom without seeking. Full .NET Release/Angular test/build and CI are separately checked per commit.

**Human acceptance still required:** examine the actual wide layout and visually confirm that local arrows/trail track the real pendulum bob. Independent pivot selection and calibrated angular motion still require additional validated work before Stage 2 can be called scientifically complete.

**Español:** [PENDULUM-STAGE2-FOCUSED-VIEWS.es.md](PENDULUM-STAGE2-FOCUSED-VIEWS.es.md).
