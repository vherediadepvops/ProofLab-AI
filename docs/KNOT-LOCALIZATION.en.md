# Knot localization with Meta

ProofLab uses Muse Spark 1.3 to propose where the free pendulum string leaves its suspension knot/support. This candidate visual inference is separate from the bob's SAM masks. It never updates the pivot, θ or ω without human review.

## Workflow
After processing a video, open **Locate knot with Meta**. A local preflight checks the original SHA-256, model and protocol cache without transferring images or spending credits. On a miss, configure a one-use token, consent to three-image transfer and an **estimated maximum US$0.05 budget**, then click **Detect knot**.

The server extracts original frames at 10%, 50% and 90% of the frame index sequence. FFmpeg applies display rotation and preserves image resolution; analysis uses JPEG stills without changing source video playback. One Muse inference returns three normalized 0–1000 points. No tools, loops or automatic retries.

Review three enlarged images. Click or use arrow keys (one source pixel) to adjust the string exit, confirm visual review, and select **Use this point**. An append-only pivot revision references the Meta evidence, preserving earlier revisions.

Occlusion, self-reported confidence below 0.8, invalid coordinates or disagreement exceeding the greater of 3 pixels and 0.5% of the shorter image side prevents applying the candidate. Model confidence is not a calibrated accuracy probability. Manual selection remains available. Moving suspension points need a different method; this feature does not establish scientific validity, metric calibration or frontal camera alignment.

## Budget and provenance
Standard pricing checked October 28, 2026: US$1.25/M input tokens and US$4.25/M output tokens. The displayed amount is an **estimated budget**, not an exact quote or provider-enforced billing cap. Before generation, the transport submits the same images to `responses/input_tokens` as a conservative context admission check, not billed usage. Context above 32768 tokens is rejected; output is capped at 2048 tokens, without tools/history. Actual provider usage remains authoritative.

Backend transfer, credit, spending-consent and request-budget gates remain enforced. Knot inference shares the existing two-request session allowance with SAM. A failed token preflight conservatively consumes a reservation; an incomplete attempt requires manual reconciliation. One-use keys are never persisted.

Images, raw response and receipt stay in `.private/physics/knot/`, verified with source/image/response hashes. This evidence has a separate file cache, not the SAM-video SQLite index. The normalized point maps to persisted 480×270 coordinates; the player computes geometry in native image proportions.

`Meta:AlreadyUsedRequests` carries used allowance (0–2) during process replacement; it does not enable paid requests. Operators must carry the actual current count and preserve existing gates, never renew authorization through restarts.

## Validation
Local tests cover coordinates/orientation, ambiguity, bounds, gates, cache integrity, no retry, concurrency, mocked HTTP transport and Angular review. Automated tests never call Meta. Real-video detector quality still requires a manual execution and review of all three crosses.

Sources: [Image understanding](https://dev.meta.ai/docs/image-understanding), [Structured output](https://dev.meta.ai/docs/structured-output), [Token counting](https://dev.meta.ai/docs/token-counting), [Pricing](https://dev.meta.ai/docs/pricing-rate-limits).

[Español](KNOT-LOCALIZATION.es.md).
