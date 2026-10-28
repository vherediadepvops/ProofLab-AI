# ProofLab AI — Stage 2 pendulum geometric motion engine (partial increment)

**Status:** deterministic .NET engine built and tested; **NOT wired into the visual player**, no real SAM 3.1 video inference or Muse Spark reasoning. No claims of validated physical evidence.

## Measurement method

`PendulumVideoKinematics.Analyze` consumes ONLY previously recorded local `(time, Xpixel, Ypixel)` candidates together with a **user-selected image pivot**. It estimates image-plane angle with `atan2(Xbob-Xpivot, Ybob-Ypivot)`, angular velocity in `rad/s`, angular acceleration in `rad/s²` using five-sample local quadratic fits, and apparent tangential speed in `px/s`. Predictions never enter estimation of observed motion. Missing or unreliable derivatives are reported as null/inconclusive.

Five-point median smoothing applies to **interior points only**, avoiding biased start/end release angles. The estimator rejects large time gaps, angular jumps, large radius inconsistency and implausible local derivatives. It reports radius variation and confidence limitations; a periodic track **does not verify that the physical bob was correctly identified**.

A separate ideal model computes **theoretical maximum angular speed at the lowest point** from declared length and release angle. This is clearly a **THEORETICAL PREDICTION**, not a correction to observations. Neither zero observed endpoint speed nor maximum center speed is forced.

## Units and limits

Without independent physical spatial calibration, **do not display measured metric XY or claim m/s**. The current mathematical type has an explicit conditional approximation under human front-facing-camera acknowledgement and consistent radius, but that is **not metrological calibration**, and the planned integration endpoint leaves this physical-unit mode OFF. Observations currently originate from the **LOCAL FFmpeg/background pixel tracker** at 480×270 and 12fps, **NOT SAM**; timestamps are sampling-grid times, not native PTS.

## Verification and current operational blockers

- Authorized device: `DESKTOP-ESGSLJH`, installed **.NET SDK 10.0.401**.
- Incompatibility `Math.Hypot(dx,dy)` was replaced with supported **`double.Hypot(dx,dy)`** using the permitted edit operation.
- Fixed a biased median filter at the initial/last observations; **10/10 focused tests** and **136/136 overall .NET Release tests** passed.
- An append-only pivot-revision store bound to the original SHA-256 and local endpoints were drafted in `PendulumDirectorKinematicsStore.cs` and `Program.cs`; **API Debug compiled cleanly (zero warnings/errors)**, but the endpoints **have not yet been exercised through HTTP**.
- Security controls then rejected the attempt to write the Angular integration module and an attempt to restart the main API. The controls were **not bypassed or disabled**. UI port 4300 and owner's unrelated Meta test port 5393 remained available; main port 5391 was stopped. No paid Meta requests.
- Existing unpublished Muse files, private original videos and previous evidence were preserved.

**Remaining before completion:** restore port 5391 by an expressly permitted procedure, smoke-test pivot endpoints against genuine source video, enable click-to-mark pivot and frame-synchronized vectors in Angular, manually validate visual bob identity and timing, test real SAM under separately verified/approved credits, CI and scientific provenance updates. **Stage 2 is NOT complete.**

**Español:** [PENDULUM-KINEMATICS.es.md](PENDULUM-KINEMATICS.es.md).
