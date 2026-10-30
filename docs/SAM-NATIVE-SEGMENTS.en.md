# SAM: original segments and Spark references

Verified on 2026-10-29 (America/Lima). **Meta API · segments** uses the existing processing card.

## Flow

1. Read the original presentation timestamps and packets. Choose up to eight windows starting at keyframes, with full coverage, verifiable overlaps and at least 50 core frames per part. Use fewer parts when keyframes or verified references are insufficient.
2. Remux the complete Spark video and extract 50 oriented reference images in one pass. Prioritize shared seam frames, cover both endpoints and distribute remaining references across the timeline. Selection is deterministic and auditable.
3. Send the complete video plus 50 images to Spark. Request boxes for the same moving object without assuming shape/material. Retain three suspension estimates at reference ordinals 0, 25 and 49. Sending the complete file does not prove that the provider examines every native frame.
4. Create segments with `-c:v copy`: no encoder, scaling or FPS conversion. Bitstream filters select packets and rebase timestamps. One compressed read feeds all outputs. SAM does not need audio/other tracks; playback still uses the original file.
5. Verify geometry, frame count, decoded pixels and source PTS (maximum tolerance 5 ms). Send valid parts, up to eight concurrently, with Spark boxes from their own reference frames.
6. Join only results whose continuity passes validation. Preserve inconclusive responses. A continuous recovery requires a new manual action and displayed estimate; it is not an automatic retry.

There is no lossless-transcode fallback. Failed original-copy validation produces an explicit error. Historical batches remain readable. Existing laboratory frame/duration/size limits still apply; this does not promise unrestricted 4K/240 FPS support.

## Timing, reuse and cost

- Quotes use native windows rather than the historical roughly-50-frame splitting rule.
- The optional token-count request was removed from this flow. Spark cost is an **estimate**, not a provider-enforced billing ceiling. Returned usage is retained.
- A private preparation manifest validates source, PTS, MP4 and image hashes before reuse. Original decoded-frame hashes remain cached in service memory.
- Spark uploads are reused for the same credential/content while the transport instance lives. Hashes identify entries; tokens are not stored. Restarting loses the upload index, while the local preparation manifest persists. Provider failure does not trigger another inference automatically.
- No upload deletion or expiration is requested. This follows the owner's instruction; final retention remains subject to the provider.
- The existing card shows Spark preparation, reference extraction, upload, inference and verification, plus each SAM request, joining and playback. All stages show duration. Upload percentage counts sent bytes; inference remains indeterminate because Meta does not report a percentage. Upload 100% does not imply a final server response.

## Fixes discovered during live testing

Parsing/rendering now accepts SAM mask rasters whose resolution differs from their original-image bounding box. Contours, centroids and areas map to source pixels; seam validation compares both masks in that coordinate space. Original responses and continuity thresholds are unchanged. Suspension evidence accepts both historical ordinals 0/1/2 and new ordinals 0/25/49.

## Validation and limitations

- Ten private videos: six MOV and four MP4. MOV files produced eight parts each; MP4 files produced 4, 5, 2 and 7 according to keyframes. All ten preserved decoded pixels/PTS. MOV sources of approximately 46–48 MB produced approximately 47–49 MB in total, versus the previous 524 MB case. Overlap may cause larger overhead in other files; exact byte equality is not guaranteed.
- 13 real tests out of 30 authorized: 12 Spark and 71 SAM inferences. Nine videos passed segmented joining; the tenth was recovered with one continuous inference after an inconclusive join. Failed attempts remain recorded privately.
- Reusing preparation/upload reduced one MOV's Spark stage from 79.2 to 33.4 seconds. This is a single comparison; approximately 33 seconds remained provider inference.
- 96 targeted .NET tests, 138 Angular tests in Edge, local build/publish and visual playback checks passed. The published seam worker accepts scaled masks and rejects disjoint boxes. No CI runs.
- **Valid tracking does not certify physical measurements.** All ten final archives decode, but only one passed automatic suspension estimation in this campaign. The other nine retain inconclusive θ/ω until suspension/camera/trajectory review. Existing manual correction controls remain available.

## Files and local reproduction

- `SamAdaptiveNativeCopy.cs` / `SamAdaptiveSegmentPlan.cs`: planning, packet copying and verification.
- `AssemblyDenseGrounding.cs`, `AssemblyVideoGrounding.cs`, `AssemblyMetaTransport.cs`, `AssemblyProgress.cs`: Spark references, reuse and telemetry.
- `sam-video-evidence.ts`, `sam-playback.ts`, `sam-suspension.ts`, `scripts/sam-seam-worker.ts`: mask geometry and continuity.
- `sam-video-replay.ts`: indicators within the existing panel.
- Offline preparation: `dotnet run --project tools/SamAdaptiveQa -- --verify-clips <file> <sha256> <output-under-.private>`; no Meta calls.
- See [Edge testing](EDGE-TESTING.en.md). Use a dedicated profile/CDP and archived responses to check playback without another inference. Never commit tokens, videos or private receipts.

The owner's three Meta permission flags and unlimited manual requests remain enabled. The agent's authorized test allowance is tracked separately from the user's service.
