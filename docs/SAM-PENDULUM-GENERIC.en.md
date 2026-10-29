# Spark locates; SAM tracks; Muse interprets

Pressing **Process** for a new video first asks Muse Spark to examine three original frames and locate the same suspended object: ball, coin, washer, weight or another shape/material. It returns full oriented-image boxes and a clear/ambiguous/not-visible decision. Tracking no longer depends only on the phrase `pendulum bob`.

With at least two clear observations, ProofLab converts normalized coordinates to native pixels and sends SAM visual boxes using one stable object ID and actual source frame indices. SAM propagates tracking throughout the clip. Visual grammar is not mixed with a text target phrase.

## Interface and cost

The existing Process button and cost card are retained. Parallel local GET checks do not call Meta or send tokens. The displayed cost includes SAM and, when localization is not cached, up to US$0.05 for Spark. SAM retains its US$0.50 limit; the combined estimate may reach US$0.55, shown before the click. A usable SAM cache HIT costs US$0.

The same explicit click shows Spark localization followed by SAM tracking. Ambiguity, errors or changed source/budget stop the sequence; no automatic retries occur. Saved Spark localization for the exact video can be reused. No section or extra assembly confirmation is added.

## Provenance and limits

Localization uses `ASSEMBLY_OBJECT_GROUNDING_3_NATIVE_FRAMES_V1`. Its scene is marked `SPARK_GROUNDING`, not human-reviewed. Only the object role is seeded; cord/knot/pulley identification is not required. The suspension point is not automatically confirmed: angular measurements still require a defined pivot.

Spark responses, images and hashes are retained independently of SAM receipts and masks. Usable historical masks remain replayable; empty text searches no longer block visual grounding or masquerade as a cached trajectory.

Three images locate an object but do not establish pendular motion. After tracking, local code computes measurements, coverage and uncertainty. The existing **Ask Muse** research step lets Spark interpret temporal evidence as pendulum-compatible, other motion or inconclusive, with bilingual justification. Historical frozen predictions remain unchanged.

Ambiguity and occlusion can prevent detection. Masks, centers of mass and human review are never fabricated.

[Official SAM video prompt format](https://dev.meta.ai/docs/sam/segmenting#prompt-several-video-frames). [Español](SAM-PENDULUM-GENERIC.es.md).
