# A/B experiment — SAM 3.1 in four streamed segments (FEATURE BRANCH)

**Status:** frame partitioning, concurrency and boundary verification tested with **synthetic fixtures only**. NOT wired into the billable inference path. No Meta calls and no `main` modification.

## Planned pipeline

1. Spark grounds the objective using the **entire original video ONCE**, producing an authenticated scene and target. All four fragments must retain the same SceneKey.
2. ProofLab uses source PTS (not nominal FPS), makes **four consecutive core windows** with two overlapping source frames on either side to verify target continuity.
3. Generate four private MP4s and verify **decoded frame count, PTS, orientation and pixels** before any uploads. **Physical cutting is NOT implemented yet:** the local FFmpeg build has h264_nvenc but no libx264; lossless equivalence has not been established. Never claim approximate time cuts are scientific frame-exact evidence.
4. `Task.WhenAll` orchestrates four independent tasks; each reports its own streaming SSE event/delta counters. Production must durably create **four separately traceable attempt/receipt records** before claiming completion. Current orchestrator only calls injected synthetic delegates: **NO provider HTTP, credentials or budget gates are changed**.
5. Convert part-local indices to global source frame indices and preserve original PTS. Different SAM IDs across fragments are acceptable ONLY with matching same-frame overlap geometry: centroid distance no greater than 45% of the smaller object span (minimum 3 px) and bounding-box IoU at least 0.40. Missing overlap yields an **unverified boundary**; contradictory overlap is rejected. Never invent masks/centroids or interpolate observations.
6. Once four real, verified provider results exist, compare full-track coverage and centroid errors (mean and p95 image pixels) against an already persisted monolithic SAM result. Do not mark completion with missing receipts or unresolved seams. Streaming progress alone is not verified inference output.

## 1,800-frame example

Four 450-frame cores; SAM inputs of 452, 454, 454, and 452 frames = **1,812 total billable candidates**. Nominal SAM estimate US$0.3624, compared with US$0.3600 for a single run (US$0.0024 overlap overhead), excluding Spark and actual billing variances. For the previously captured 1,828-frame recording the total would be 1,840 with overlap, nominal US$0.3680 vs US$0.3656 before Spark.

There is NO verified 4× speedup: the provider may throttle or serialize streams. Four streamed requests require separate explicit cost/transfer approval and a combined cap. Real event counters must not be rendered as fake completion percentages.

## Feature source

- `src/ProofLab.Api/SamFourWayPlan.cs`: exact windows and source PTS SHA, aggregate estimate, frame mapping and conservative overlap checks.
- `src/ProofLab.Api/SamFourWayStreaming.cs`: four concurrent tasks, per-segment SSE progress, no automatic retries or premature sibling cancellation, complete-track comparison against a separate full-run reference.
- `tests/ProofLab.Tests/SamFourWayTests.cs`: deterministic synthetic tests for cost, PTS, seams, changed IDs, missing data, failures, parallel progress, and image-space baseline comparison.
- **No changes** to production paid path, `PendulumSamCache`, Meta transports, server policy, SQLite or UI in this feature.

## Merge gates

1. Generate four real MP4s and prove source-frame count/PTS/pixel mapping vs the actual original, never the 12 fps proxy.
2. Store an immutable per-segment attempt/receipt and data SHA while keeping incomplete attempts and costs traceable.
3. Decode real provider masks with `@meta-sam/parser` and produce ONE verified combined result containing the real masks and global timestamps. Only sparse centroid observations are merged today.
4. Run offline full-vs-four comparisons and no-billing server/UI tests; obtain NEW separate approval before any four-call provider experiment and measure actual cost/latency.
5. Do not merge this feature into `main` without owner approval. No routine CI.

**Español:** [SAM-PARALLEL4-STREAMING.es.md](SAM-PARALLEL4-STREAMING.es.md).
