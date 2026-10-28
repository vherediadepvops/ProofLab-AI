# ProofLab AI — Interactive mathematical pendulum workbench (October 27, 2026)

**Mission:** Physics Discovery (M2). **Status:** working deterministic ideal-model workbench within the guided flagship; authentic SAM 3.1 video tracking and autonomous Muse Spark reasoning **NOT implemented yet**. Zero paid Meta requests. No claim of original discovery.

## Approved user experience

The researcher sets pivot-to-bob-centre **length** (0.10–5.00 m), initial **angle** (1–85°), bob **mass** (10–5000 g) and may check **Add a second trial**. The optional follow-up uses a clearly labeled **local non-AI control** (50→80 cm is the default example) or a **researcher-selected length** such as 90 cm. A real Muse-controlled option must remain unavailable until authorized, verified model inference exists. The application computes values and **three dynamically regenerated theoretical plots BEFORE video upload**.

## Physical model and provenance

Assumes an ideal planar simple pendulum, point-mass bob, massless inextensible string, fixed frictionless pivot, no air drag and release from rest:

```text
theta¨ + (g/L) sin(theta) = 0
g = 9.80665 m/s²
theta(0) = theta0; theta'(0) = 0
x(t) = L sin(theta(t))
h(t) = L (1 - cos(theta(t)))    [height above lowest point]
K(t) = 1/2 m (L theta'(t))²
U(t) = m g h(t); E(t) = K(t) + U(t)
```

Small-angle approximation: `Tsmall = 2*pi*sqrt(L/g)`. Exact finite-amplitude period of this ideal nonlinear pendulum: `T = Tsmall / AGM(1, cos(theta0/2))`, using radians and the arithmetic-geometric mean.

The time-domain solution uses deterministic **fourth-order Runge–Kutta**, internal integration step **0.01 s**, exported point spacing **0.04 s**, **20 s** total and **501 time samples per trial**. Every result carries the maximum relative numerical energy drift. SVG plots use solver-returned samples, not static illustrations. Mass modifies the energy, but does not alter an ideal pendulum period. Initial angle changes the amplitude and nonlinear period. The second trial assumes the **same initial angle**.

## Three fully recomputed theoretical plots

1. **Horizontal displacement `x(t)` vs time**, in cm and s; first length in blue and optional second length in orange.
2. **Physical XY trajectory**, in cm: `x=L sin(theta)` and vertical height `h=L(1-cos(theta))`. Optional second arc is overlaid on common axes.
3. **Kinetic, potential and total energy**, in J vs time for the first length. Total stays constant in the ideal model. Mass scales all energies linearly, not the period.

Export button **prooflab-modelo-teorico.csv** writes data from both lengths, full time series and energy values. Deterministic local endpoint: `POST /api/science/pendulum/mathematical-model`, JSON `{"lengthMeters":0.5,"angleDegrees":15,"massGrams":300,"followUpLengthMeters":0.8}`. The second length is optional.

**Verified example:** 50 cm, 15°, 300 g gives **Tnonlinear ≈ 1.424847 s**, versus **Tsmall ≈ 1.4185 s**. The optional 80 cm, 15° model gives **Tnonlinear ≈ 1.802305 s**, a predicted change of **26.4911%**. These are mathematically calculated **predictions, NOT observations or novel physical discoveries**.

## Optional real-video workflow

- Starting a study freezes length, angle, mass, choice of one/two trials and follow-up mode in private storage with a 64-character `modelSha256` checked on read. This records theoretical parameters **before any video is uploaded**. The local hash is not an external timestamp attestation.
- **One trial:** upload one genuine MOV/MP4. The existing experimental local tracker estimates motion from decoded video coordinates; a browser H.264 proxy lets the researcher review the bob visually. After **declared human acknowledgement**, a button compares the local period against nonlinear theoretical period and computes `100*abs(Tvideo-Ttheory)/Ttheory`. No second video is required.
- **Two trials:** the second length is either manually selected or recommended by a **local non-AI heuristic**. After the first video and declared review, the same finite-angle mathematical model freezes a follow-up prediction (source, timestamp, SHA). Only then can one second video be uploaded and a preliminary change in period be compared. For genuine prospective science, **record the second video AFTER this prediction**, rather than replaying a clip already filmed.
- Once a video is uploaded, three **additional observation-derived plots** show: tracked horizontal pixel position `x(t)` in **px**, estimated horizontal pixel velocity in **px/s**, and pixel-position-vs-pixel-velocity **image phase space**. These are **NOT calibrated physical XY**: the current tracker only provides horizontal positions. For legible display only, visualization clips raw pixel values to the 2nd–98th percentiles; stored evidence remains unchanged. These plots are **FFmpeg/local background tracker outputs, NOT SAM masks and NOT Muse-generated measurements**.

## Quality and remaining work

- Predicted curves are **independent of video coordinates**; observed curves come **independently from the submitted videos**, never from the theoretical equation. The AI cannot bias data to match the predictions.
- Relative period discrepancy and the gap between predicted/observed percentage changes are descriptive differences, **not statistical confidence intervals or a complete uncertainty budget**.
- No independently verified bob identity, calibrated spatial XY, real source-frame timestamp check, drag estimation, systematic uncertainty or scientific peer review. A physical string and bob may depart from the ideal point-mass model.
- Original user video, private ledgers and previous three-plus-three benchmark data remain intact. Previous technical mode stays accessible and optional.
- Localhost-only prototype: **do not expose anonymous Internet uploads** without authentication, quotas, secure retention and privacy policies. Paid Meta remains gated.
- **Next:** real cost-controlled SAM 3.1 segmentation and temporal tracking (with calibrated XY); an actual approved Muse Spark next-experiment decision; independent manually timed cycles, uncertainty and the five-stage bilingual Scientific Method Engine reports.

**Español:** [PENDULUM-MATHEMATICAL-WORKBENCH.es.md](PENDULUM-MATHEMATICAL-WORKBENCH.es.md).
