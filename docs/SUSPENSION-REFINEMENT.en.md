# Visual suspension refinement

The existing body-box visual review also receives three enlarged suspension crops. It asks for the free cord exit, preserves uncertainty for occlusion/camera motion/disagreement, and adds no separate inference or UI section. Extra reference images do add tokens.

## Geometry and provenance

Crops cover 40% of each oriented frame dimension (minimum 96 px, clipped to image bounds). The coarse pivot only centers the search. Reference crops are enlarged for inspection; the source video is unchanged. Crop-normalized coordinates map back using the exact crop origin and dimensions. JPEG hashes and source-frame/time associations are preserved.

Three references closest to 20/50/80% of the available reviewed time interval are chosen before the response. V2 retains source ordinals in its manifest and compacts derived point indices to 0/1/2. Readers retain V1 compatibility and verify receipts, hashes and derivation. V2 playback never replaces the visual exit with a fitted circle center; agreement threshold is max(3 px, .5% of the shorter side). SAM radial consistency is still checked.

## Evidence: 2026-10-30

Five live Spark reviews, zero new SAM requests. Historical masks were compared offline without replacing their receipts. Usage estimate: USD 0.201404, not a verified invoice. Cases 1–4 used initial/middle/final references; only case 5 exercised final interior-time selection live. This is not five complete scientific workflow validations.

| Case | Pivot error before → after | Outcome |
|---|---|---|
| Synthetic 50 cm | 39.99 → 6.43 px | SAM tracking remains wrong; angle MAE 7.82 → 8.37 degrees |
| Synthetic 80 cm | 39.65 → 8.39 px | Angle MAE 4.48 → 1.32 degrees; omega MAE .0557 → .0327 rad/s |
| Real 50CM_1 | No ground truth | Inconclusive: about 30 px disagreement |
| Real 80CM_1 | No ground truth | Inconclusive: occluded/uncertain initial anchor |
| Real 80CM_2 | No ground truth | Interior references recover an estimated pivot; spread 3.39 px |

411 .NET tests and 96 Angular tests pass. Production build passes with an initial bundle size warning; no CI.

Remaining: diagnose incorrect SAM body tracking in the 50 cm control, validate seeds/masks against known truth, and repeat the test matrix. Do not lower uncertainty checks to display numbers. Real-video pivot accuracy is not certified.
