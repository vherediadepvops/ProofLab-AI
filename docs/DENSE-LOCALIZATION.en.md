# Object and suspension localization across 50 images

> Update 2026-11-03: new localization always uses 50 original views, 50 ×1.5 crops and 50 ×3 crops of the same 50 frames. Both enlargements come from the original, without overlays, and map back to original coordinates. The first answer cannot skip the enlarged reviews. Suspension is estimated only when both zoom reviews have stable, agreeing consensus; otherwise it remains inconclusive. Four Spark requests cover initial localization, two suspension reviews and body review. The existing conservative budget already covers all four. No automatic retries. Historical results remain readable and are not recomputed. New validation uses local mocked responses, with no new live Meta calls.

The remainder records the previous protocol and its historical checks.


Protocol `SPARK_DENSE_OBJECT_AND_EXIT_50_V1`. The implementation was verified locally with a simulated provider; accuracy and latency against live Meta remain unmeasured. Publishing source does not deploy production.

## New workflow

1. Prepare the original video and 50 references distributed through time. Spark receives the complete video and references identified by frame and timestamp.
2. Request an oscillating-body box and a free-string exit point in **each of the 50 images**. Balls, coins, nuts and plumb bobs share the same contract. Cord, support, hand and shadow are excluded from the body.
3. If either target or suspension remains unresolved, extract another 50 distinct references. There are **at most three rounds total**, including the first, with early stopping when sufficient evidence is available. Fewer than 50 unused frames ends sampling rather than duplicating images.
4. Reuse the uploaded video file. Do not request token counting or delete the remote file. Preserve every round's responses, references, timestamps, receipts and hashes.
5. SAM receives time-specific object positions when target identity is sufficiently resolved. Suspension uncertainty can still leave θ/ω inconclusive despite valid object masks.

A completed but insufficient answer may trigger another round. Transport, receipt or parsing errors stop the operation and preserve the attempt; they do not automatically trigger another potentially billable request.

## Suspension consensus

- Requires a static-camera classification. `MOVING` and `UNCLEAR` remain inconclusive.
- Find a dominant spatial cluster and use its median, rather than averaging all points. Distances use oriented original-video pixels.
- Agreement radius: the greater of 3 px and 0.5% of the smaller dimension. Requires at least 10 observations, 60% of usable observations and 20% of submitted images; a comparable competing cluster prevents acceptance.
- Require coverage in at least three of five temporal bins and a minimum 60% time span. Outliers remain in preserved evidence but do not move the estimated suspension.
- These are diagnostic rules, **not a statistical 95% confidence interval**. Spark's reported confidence does not certify geometric accuracy either.

The body moves: **coordinates are never averaged across frames**. The first round that clearly resolves the target with sufficient time-distributed observations is retained. SAM masks and per-frame geometry checks still determine which values can be shown. Repeated answers can share a systematic error; agreement alone does not establish the physically correct point.

## Cost, progress and compatibility

New quotes include up to three Spark requests (currently US$0.25 estimated per round, up to US$0.75), plus SAM operations and the corresponding workflow reserves. These are authorization estimates, not an invoice or a provider-enforced billing cap. Early resolution uses fewer rounds.

Additional rounds appear in existing progress bars. No new laboratory sections are added. Owner manual-request permissions, counters and credentials are unchanged.

Historical three-point results and crop reviews remain readable; their coordinates are neither rewritten nor relabeled as new-protocol results. This workflow applies to new localizations. Recovery validates the derivation against preserved responses and hashes.

## Local verification

- Mocked .NET suite: **466/466** passing tests, covering consensus, ambiguous clusters, uncertain camera, three rounds without duplicate references, early stopping, provider failure, persistence and tamper detection.
- Angular suspension tests in headless Edge: **17/17** passing assertions. The runner remained open after reporting success and was interrupted; this is not recorded as a clean process exit.
- Production Angular build and API build to an isolated directory succeeded. Existing Angular size-budget warnings remain.
- **Zero live Meta requests** in this validation; no CI or deployment. Independent visual accuracy checks, effective cost and live-round timings remain outstanding.
