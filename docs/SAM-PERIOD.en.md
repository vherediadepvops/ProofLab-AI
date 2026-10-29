# Observed period from SAM

The laboratory derives period from object centroids in an authenticated SAM response and the original video's frame timestamps. It does not use string length, theoretical period, local FFmpeg tracking or assumed FPS. This is a preliminary local estimate, not approval by Muse.

## V1 method

1. Restore source-image geometry for the centroid and pivot, then calculate angle from vertical.
2. Set the center level halfway between the 5th and 95th angle percentiles. Detect upward crossings with hysteresis.
3. Interpolate crossing time only between consecutive detected source frames. Missing detections, invalid geometry or a timestamp gap larger than three median intervals split segments.
4. Form periods from consecutive same-direction crossings within each segment. Never bridge segments.
5. Select alternating cycles to avoid shared crossing endpoints. Calculate the mean, sample standard deviation s and u = s/√n, requiring at least three selected cycles.

Reported uncertainty describes repeatability only, assuming approximately independent errors. Alternation does not establish independence. Segmentation bias, pivot error, perspective, camera motion and clock calibration are excluded. This is not a 95% interval. Numerically zero dispersion does not imply perfect accuracy. See [NIST: standard uncertainty](https://physics.nist.gov/cuu/Constants/definitions.html).

## Quality and limits

Versioned SAM_PERIOD_POLICY thresholds require amplitude ≥0.5°, at least 20 frame intervals per cycle, usable coverage ≥80%, period dispersion and drift ≤5%, and apparent-radius MAD ≤10% of median radius. These are engineering criteria, not physical laws or certification.

The result is Inconclusive when criteria fail, cycles/pivot are missing, or the assembly is not a simple pendulum. Diagnostic values remain visible; inconvenient cycles are not removed to manufacture agreement. Detection gaps are reported even when enough complete cycles remain. Visual review of object identity and suspension is still required.

## Reproduction

Download reproducible report creates a private JSON export containing video/analysis/provider-response identity, object and pivot revision, geometry, timestamps, centroids, cycles, policy and ES/EN limitations. Its envelope contains payloadJson and SHA-256 of that exact UTF-8 string. The checksum establishes integrity, not a digital signature or cryptographic provider authenticity.

After checking the hash, measureSamPeriod(JSON.parse(payloadJson).input) reproduces assessment. This step does not write new results to SQLite. Muse evaluation and prediction freezing require a subsequent workflow.

Local tests: sam-period.spec.ts, sam-period-panel.spec.ts, pendulum-master-player.spec.ts and sam-playback.spec.ts. They cover 60/120/240 FPS, irregular timestamps, gaps, geometry, drift, missing data, pivot changes and report reproduction.

Saved SAM evidence checked on 2026-10-28: 1781/1828 detections, 20 complete cycles, 10 alternating cycles selected; T = 1.415864 s and statistical u = 0.000432 s. This is not total uncertainty. Period calculation took approximately 2 ms after mask decoding. No new Meta calls.
