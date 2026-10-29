# Pendulums of different shapes: Spark + SAM

## Flow and modes
The existing **Process** button and cards remain. **Meta API** runs a fresh Muse Spark evaluation of the complete video, followed by fresh SAM analysis. **Meta API + cache** reuses compatible authentic masks; otherwise it can reuse valid localization or start the complete flow. Empty SAM responses remain historical evidence, not useful cache hits.

Spark receives a private MP4 containing the complete recording, without re-encoding or dropping frames, plus three original images labeled as spatial anchors. MOV-to-MP4 remux verifies original frame counts and timestamps. Full context helps identify the object and visible limitations; anchors locate precise boxes for SAM. This does not guarantee that the provider examines every native frame.

The target is independent of material, color or shape: ball, coin, weight or another suspended body. Spark returns CLEAR_SINGLE_OBJECT, AMBIGUOUS or NOT_VISIBLE. No clear target means no SAM submission. Boxes use oriented native dimensions and source frame indices; SAM keeps one object ID and receives the full video.

## Cost and consent
Before clicking, **Up to US$…** shows SAM plus the server-quoted maximum Spark budget (default US$0.25). This is an estimated ceiling, not an invoice. SAM retains its US$0.50 per-operation limit. Local upload and quotes perform no inference.
Clicking Process authorizes full-video and reference-image transfer. The actual payload context is counted before Spark inference and checked against the budget. There are no automatic retries. A valid SAM cache hit costs US$0.00.

## Measurement and provenance
SAM supplies masks; ProofLab computes centroids, angles, derivatives and trails using original timestamps. The visual center does not certify the physical center of mass. Automatic localization has SPARK_GROUNDING provenance and never confirms a human pivot.
The existing scientific Muse consultation interprets measurements, uncertainty and limitations. It is an independent request: no shared localization memory is assumed, and no third inference is added automatically.

Each Spark run and SAM receipt retains its original identity and response. Full-video protocol is distinct from historical three-image evidence; reuse does not relabel old results as full-video analysis.
An absent observation with a zero box may express confidence in absence. It must not invalidate another object's detection. Local recovery preserves raw responses and hashes; it never invents boxes or makes another Meta call.
Spark/SAM errors or ambiguous targets close the loading indicator while retaining the error message.

## Limits
Occlusion, small objects, perspective and camera movement can prevent valid measurement. Universal detection and scientific accuracy are not guaranteed. Local format, frame-count and budget limits remain; arbitrary 4K/240 FPS videos are not promised.

[Muse video](https://dev.meta.ai/docs/video-understanding) · [SAM prompts](https://dev.meta.ai/docs/sam/segmenting#prompt-several-video-frames) · [Español](SAM-PENDULUM-GENERIC.es.md).
