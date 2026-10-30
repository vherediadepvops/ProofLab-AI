# Segmented SAM: verified state (2026-10-29)

Integrated in main through the existing “API Meta · segments” selector.
Local web: http://localhost:4300/; main API: 127.0.0.1:5391.
Owner payment permissions stay enabled across branches; see [Meta authorization](META-API-CONNECTION.md). There is no fixed three-batch limit for manual requests.

## Planning and parallel work

The nominal planner targets about 53 core frames (1000 → 19 parts; 1500 → 28).
Fifty frames is a billing floor, not a speed guarantee. Every part must contain a genuine Spark box at its own source frame; sparse references reduce the partition count. Boxes are never interpolated to invent anchors.

The UI follows the server quote. `SamAdaptive:Concurrency` defaults to 8, accepts 1–12 and is capped by available parts. This bounds simultaneous work, not manual submissions.
Preparation shares one intermediate, hashes source/intermediate concurrently and verifies up to four clips concurrently (two for images ≥8 megapixels).
Original geometry, decoded pixels and timestamps remain verified. Seams require at least two shared observations and mask IoU ≥0.90 on every shared frame. Independent provider receipts remain linked to the derived output. No automatic paid retries.

## Real tests: 3 of 5 authorized

Total: one Spark call plus 25 SAM calls. SAM estimate USD0.4368; Spark approved maximum USD0.25. These are not invoices.

| Test | Partition | Backend time | Quality |
|---|---|---:|---|
| 951-frame ball video, experimental 21 Spark references | 18 parts, concurrency 4 | 62.575 s | 888 masks; inconsistent seams |
| Same source, existing Spark references | 4 parts, effective concurrency 4 | 32.903 s | 951 masks; two inconsistent seams |
| 182-frame WhatsApp video, actual Edge workflow | 3 concurrent parts | 20.071 s + 0.933 s UI | All 182 masks; verified seams, playback, angle, angular velocity, vectors and trail |

Short-video preparation was 2.293 s versus 3.602 s in the previous test. Individual observations do not establish a universal end-to-end speedup; provider latency varied.

The dense-reference Spark experiment returned misplaced ball boxes and **was not shipped**. Seam thresholds were not weakened. Real concurrency 8/12 was not benchmarked because available trustworthy anchors limited the cases to 3–4 parts. The scheduler's 4/8/12 bounds were tested with synthetic transport.

39 targeted .NET tests and 42 Angular tests passed. Actual Edge replay had no console/network errors. No CI or new UI sections. Private evidence stays on the authorized device.
Remaining work: improve dense grounding and validate the long video's seams before recommending 19/28 requests universally. Two authorized tests remain unused.

Sources: [Meta billing](https://dev.meta.ai/docs/pricing-rate-limits), [SAM prompts](https://dev.meta.ai/docs/sam/segmenting), [Spark grounding](https://dev.meta.ai/docs/image-understanding).
