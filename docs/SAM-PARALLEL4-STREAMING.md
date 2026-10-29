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


## Expanded results — Oct 29, 2026: REAL clips and isolated offline SSE QA

All code remains in `feature/sam-parallel4-streaming`; no changes to the billable Meta endpoints or the product UI.

- `SamFourWayClipBuilder.cs` and `SamFourWayOnePassClips.cs` cut the real 50 cm, 1,828-frame MOV into four lossless HEVC NVENC MP4 files with frame-exact selection. Decoded per-frame SHA-256 hashes match the original, with maximum FFprobe PTS difference **1.55 ms**. Four clips have lengths 119–160 MB and total **532,387,036 bytes**, compared to **45,708,115 bytes** for the MOV (~11.65× more to upload).
- Windows of 459/461/461/459 frames: 1,840 submitted frames including 12 overlaps. Estimated SAM cost **USD0.3680**, excluding Spark/provider-billing variability.
- Four separate preparations: **120.76 s**; a single original decode routed to four NVENC encoders simultaneously: **98.24 s**; single-source-framehash verification optimization: **90.208 s**, approximately **25.3% faster** than the first local method. These include local pixel and timestamp QA, not provider inference.
- An HEVC `-c copy` seek experiment was rejected: an expected 56-frame segment instead yielded **115** frames for input seek and **3** for output seek. Keyframe-aligned remux is not accepted without exact proven boundaries.
- `SamFourWayMaskAssembler.cs` preserves each original mask/box record and maps local indices back to original PTS. It strictly checks shared-overlap payload equality for the offline reference; an inconsistent mask invalidates the seam. Four independent provider responses may differ: raster mask-IoU matching and human review would be required before scientifically accepting such changes.
- `scripts/sam-parallel4-reference.mjs` decoded the authentic stored full-video SAM response via `@meta-sam/parser`: **1,781 masks**, zero warnings, frames 47–1827.
- `SamFourWayOfflineSseReplay.cs` routes four SSE sequences rebuilt from ONE previously completed SAM reference through the real `SamVideoSseParser` and the four-way coordinator, then recombines the masks. All **1,781 mask records match byte-for-byte**, 3/3 seams verified, centroid errors 0 px **because the same reference was reused**. This is **NOT** evidence that four independent Meta inferences are equally fast or accurate.
- `tools/SamFourWayQa` writes private manifests and benchmarks. `tools/SamFourWayQaServer` runs a read-only report on `127.0.0.1:5407/api/qa/report` and a visibly labeled simulated offline SSE stream on `/api/qa/stream` (16 progress events and one terminal event). No media upload, provider call, token, or spending route.
- **QA:** 19/19 focused .NET tests, clean CLI and QA-server builds. No new Meta calls or verified independent chunk masks.

Before owner-approved merge: authentic per-part immutable receipts/attempt locks, strict aggregate spending consent for four requests, independently validated official per-part masks and cross-seam raster-IoU/human checks, followed by a **real** A/B latency/cost/quality comparison. With 90.2 s local preparation and ~177 s monolithic SAM provider runtime, four remote calls need to finish in under ~87 s (ignoring Spark/other overhead) to win under a sequential scheduling assumption. No real speedup has yet been measured.
