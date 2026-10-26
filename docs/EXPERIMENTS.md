# ProofLab AI — Scientific protocols

## 001 · Pendulum

**Question:** Does length change period according to `T = 2π√(L/g)`?

Measure from the **pivot to the mass centre**. Record 20-second clips at
50 cm and 80 cm, **three repetitions at each length**, under 15-degree initial
deflection and with a stable camera. At 60 fps, each 20-second clip may contain
up to 1,200 frames. Mark the actual source and camera metadata.

Predictions at standard gravity:
- 50 cm: about 1.419 s per complete cycle.
- 80 cm: about 1.794 s per complete cycle.

For a fair verification, locate returning midline crossings using only measured
position data. Do not use the expected period to detect events. Compare the
measured period, independently timed oscillations, and theory. Document
measurement uncertainty and depth motion.

## 002 · Rolling sphere on a ramp

**Question:** Does increasing inclination increase the acceleration of a solid sphere?

Record three clips at 10 degrees and three clips at 20 degrees, using the
same rigid board and sphere. Measure the ramp angle and calibrate image position
against a visible scale. Compare observations with the no-slip theoretical model
`a=(5/7)g sin(θ)`.

Only measure acceleration from **metres along the incline** versus timestamp;
pixels alone cannot directly give SI acceleration. Avoid slipping, bouncing,
camera movement, and uncontrolled rolling.

## Reporting and evidence

Keep experiment videos separate from source control, preserve source ownership,
document provenance, compare manual and automated methods, and never describe
synthetic generated coordinates as physical data.

Any claim of **5x faster** requires timed tests against a documented manual
baseline on the same experiment recordings.

**Español:** [EXPERIMENTS.es.md](EXPERIMENTS.es.md).
