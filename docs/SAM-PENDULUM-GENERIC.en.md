# Pendulums with different shapes and materials

Direct inference uses the noun phrase **`pendulum bob`**, without filtering by metal, color, shape or a reference video. SAM segments the requested object; ProofLab derives centroids, vectors and trails from its masks and the original timestamps.

Protocol `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V3_GENERIC_BOB` and the cache key distinguish this target from the former `metal pendulum bob`. API Meta mode always requests a fresh analysis. Cache mode may reuse historical evidence containing mask records, retaining its original description and provenance; an empty historical response cannot replace the new analysis. The parser still validates masks and identity before rendering. Receipts and outstanding attempts are preserved.

## Motion interpretation

The existing scientific panel evaluates compatibility with simple pendulum motion using cycles, coverage, stability and a resolvable arc around the pivot. Periodic straight-line motion is insufficient. The assessment and its criteria are included in the reproducible report.

- **Compatible** means geometric and temporal compatibility; it does not certify a cord or prove the physical mechanism.
- **Inconclusive** applies when detections, a usable pivot, sufficient cycles or resolvable curvature are missing, or tracking is ambiguous.
- Material and shape are not inferred from a centroid. A silhouette centroid is not necessarily the center of mass.
- Perspective, camera motion, a rotating coin or occlusion can affect the trajectory. Double pendulums and other arrangements are not automatically accepted as simple pendulums.

No UI sections, Muse calls or automatic retries are added. Each manual submission retains its visible cost. Local tests use synthetic masks and trajectories; actual detection of a new setup requires its SAM response and review.

[Meta's official documentation](https://dev.meta.ai/docs/sam/segmenting) recommends a single short noun phrase for text segmentation. Material lists, long instructions and alternatives do not replace scene understanding.

[Español](SAM-PENDULUM-GENERIC.es.md).