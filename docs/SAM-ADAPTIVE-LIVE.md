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

## Real tests: 4 of 5 authorized

Total: one Spark call plus 29 SAM calls. SAM estimate USD0.8086; Spark approved maximum USD0.25. These are not invoices.

| Test | Partition | Backend time | Quality |
|---|---|---:|---|
| 951-frame ball video, experimental 21 Spark references | 18 parts, concurrency 4 | 62.575 s | 888 masks; inconsistent seams |
| Same source, existing Spark references | 4 parts, effective concurrency 4 | 32.903 s | 951 masks; two inconsistent seams |
| 182-frame WhatsApp video, actual Edge workflow | 3 concurrent parts | 20.071 s + 0.933 s UI | All 182 masks; verified seams, playback, angle, angular velocity, vectors and trail |
| 1845-frame portrait MOV, 80 cm pendulum | 4 concurrent parts, saved Spark | 170.660 s | Preparation passed; 1845 masks; two inconclusive seams |

Short-video preparation was 2.293 s versus 3.602 s in the previous test. Individual observations do not establish a universal end-to-end speedup; provider latency varied.

The dense-reference Spark experiment returned misplaced ball boxes and **was not shipped**. Seam thresholds were not weakened. Real concurrency 8/12 was not benchmarked because available trustworthy anchors limited the cases to 3–4 parts. The scheduler's 4/8/12 bounds were tested with synthetic transport.

95 targeted .NET tests for segments, orientation and Spark passed for this correction. The earlier 42 Angular tests and Edge replay remain recorded; this backend correction was tested through HTTP. No CI or new UI sections. Private evidence stays on the authorized device.
Remaining work: improve dense grounding and validate the long video's seams before recommending 19/28 requests universally. One of the five authorized tests remains unused.

Sources: [Meta billing](https://dev.meta.ai/docs/pricing-rate-limits), [SAM prompts](https://dev.meta.ai/docs/sam/segmenting), [Spark grounding](https://dev.meta.ai/docs/image-understanding).

## Portrait geometry correction and Spark timings

The portrait MOV stores 1920×1080 with −90° rotation; FFmpeg emits an oriented 1080×1920 intermediate with no rotation metadata. The previous stored-dimensions/rotation equality rejected it incorrectly. Display dimensions are now compared while retaining SHA-256 checks of **every** decoded frame and source PTS. The original remains unchanged.

Lossless encoding now forces IDR at planned segment starts instead of every two frames. This video's intermediate shrank from 1,016,300,036 to 491,210,226 bytes. All four clips preserved pixels and timing (maximum PTS difference 1.501 ms). Actual preparation took 42.693 s, which cannot be compared to the earlier 15.9 s failure because that run aborted before all checks.

All four SAM requests completed with all 1845 frames covered. Minimum seam IoUs were 0.8837 / 0.9989 / 0.8212. Two fail the unchanged 0.90 threshold, so the scientific view remains inconclusive. No threshold was weakened or paid request retried.

The previous Spark UI total of 72.3 s included upload 15.136 s, token counting 6.420 s, inference 25.880 s and cleanup 4.903 s; the remainder includes local preparation and workflow overhead. Native references now use one decode pass. An offline comparison on the same MOV measured 9.275 s versus 5.524 s, with identical JPEG hashes, frame indices, dimensions and timestamps. This is a 40% reduction for that local phase, not a demonstrated end-to-end provider speedup.

Private timings now separate remux/timestamp validation, anchors, upload, token counting, inference and cleanup. Full video, original quality, budgets and reasoning effort are preserved. Test 4 reused Spark and made four SAM calls (estimated USD0.3718, not an invoice).
