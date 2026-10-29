# SAM, local measurements and the Muse Spark assessment

Direct SAM inference uses **`pendulum bob`**, without material, color or shape filters. SAM provides masks and tracking; ProofLab calculates centroids, angles, period, coverage and geometric residuals using original timestamps. These calculations are input data, **not a phenomenon verdict**.

**Muse Spark interprets the SAM evidence** in the existing lab log and returns `PENDULUM_COMPATIBLE`, `OTHER_MOTION` or `INCONCLUSIVE`, with ES/EN reasoning. Its actual response, identifier and provenance are recorded. Classification is not inferred from the prompt name, an isolated mask or a historical evaluation lacking this field.

Muse receives a bounded summary of measurements and centroids with original frame indices and timestamps, dimensions, pivot and limitations. Samples are selected from actual observations without bridging gaps. The reduced sample does not replace measured cycles. Muse has not viewed the video images and must frame its conclusion as an inference from those data, without inventing observed materials, knots or forces. An empty SAM response only establishes detection failure and supports an inconclusive report.

## Flow and preservation

The existing **Prepare evidence → Ask Muse** flow retains its visible cost. There is no new section or automatic inference after SAM processing. Classification is recorded in the same Muse evaluation before proposing another experiment. Only a compatible classification with sufficient measurements can support a conditional prediction; the app verifies the data and arithmetic.

SAM protocol `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V3_GENERIC_BOB` distinguishes the generic target from the former `metal pendulum bob`. Historical mask archives retain their provenance; old empty responses do not replace generic analyses. Research protocol `MUSE_SAM_INVESTIGATOR_V3` requires Spark's explicit classification. Historical responses and predictions retain their JSON and hash without acquiring a new classification.

Identification of each setup depends on actual evidence. Perspective, camera motion, occlusion, multiple objects and an inaccurate pivot can limit it. A mask centroid does not certify the center of mass.

[Official Meta segmentation documentation](https://dev.meta.ai/docs/sam/segmenting). [Español](SAM-PENDULUM-GENERIC.es.md).
