# SAM: original segments and Spark references

Verified on 2026-10-29 (America/Lima). **Meta API · segments** uses the existing processing card.

## Flow

1. Read the original presentation timestamps and packets. Choose up to three windows starting at keyframes, with full coverage, verifiable overlaps and at least 50 core frames per part. Use fewer parts when keyframes or verified references are insufficient.
2. Remux the complete Spark video and extract 50 oriented reference images in one pass. Prioritize shared seam frames, cover both endpoints and distribute remaining references across the timeline. Selection is deterministic and auditable.
3. Send the complete video plus 50 images to Spark. Request boxes for the same moving object without assuming shape/material. Retain three suspension estimates at reference ordinals 0, 25 and 49. Sending the complete file does not prove that the provider examines every native frame.
4. Create segments with `-c:v copy`: no encoder, scaling or FPS conversion. Bitstream filters select packets and rebase timestamps. One compressed read feeds all outputs. SAM does not need audio/other tracks; playback still uses the original file.
5. Verify geometry, frame count, decoded pixels and source PTS (maximum tolerance 5 ms). Send valid parts, up to three concurrently, with Spark boxes from their own reference frames.
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

## October 30 follow-up: timing and target quality

In API Meta/API Meta + cache, the SAM timer stops when its request completes. Subsequent Spark suspension review and playback preparation have separate timings in the existing card. Reused SAM shows cache with zero inference seconds. A failed review does not mark the saved SAM response as failed. 56 local Angular tests cover these states and the segmented flow; no Meta calls were made for this fix.

Coherent segment seams verify continuity, not correct object identity. Syntactically valid, high-confidence Spark boxes can still identify the cord or background. SAM may follow those boxes consistently across parts while producing an incorrect trajectory. Missing θ/ω with TRACK_INCONSISTENT_WITH_SUSPENSION must not be repaired by disabling validation or only moving the pivot manually. That diagnosis led to the visual verification described below.

## Visual body review before SAM — October 30

Spark first localizes the apparatus using the complete video and 50 references. A second inference receives oriented source images annotated with draft boxes and a numbered grid. It independently locates the body and confirms/corrects each box or declares uncertainty. Any shape/material/color is allowed (nut, coin, ball, plumb bob, etc.); there is no metal-plumb template. Model confidence is not a calibrated probability.

Review covers beginning/middle/end and exact references shared by segments: up to 16 of the 50 images, typically 9–10 with seven/eight cuts. It does not recheck every native frame. Only reviewed boxes seed SAM; discarded draft boxes are never silently restored or interpolated. Uncertain identification preserves evidence and prevents SAM submission for that target.

Original response, annotated images, review attempt/response, derived evidence and hashes remain separate. Suspension is bound to the exact localization used and geometric checks are unchanged. Historical archives remain readable. Old unreviewed localization can receive only the new review without another upload/initial inference; saved SAM results are not retroactively corrected.

The existing card adds **Verify object with Spark**, duration and indeterminate activity. Quotes include an estimated US$0.10 for that call: two Spark inferences for new analysis, one for a reusable draft, zero for a saved review. This is not a provider-enforced cap. Up to eight non-reencoded segments, no token pre-count, remote deletion or automatic retries remain unchanged. Historical cache keys explicitly normalize CRLF independently of source checkout.

Local verification: 391 .NET and 56 Angular tests passed; 43 Assembly tests passed again after the final recording adjustment. Two Edge replays of real provider archives showed changing θ/ω at 2, 4 and 6 seconds, masks, vectors and trails: a real plumb bob (1938/1938 frames) and an animated ball (1800/1800). The plumb bob radial residual decreased from about 211 to 16.4 px after box correction; review alone took 18.5 s and SAM parts took 25.6–32.2 s. These are observations for these files, not universal precision or model training.

Code: ObjectVisualReview.cs, AssemblyObjectReview.cs, AssemblyVideoGrounding.cs and VideoMediaDecoder.cs. Tests cover uncertainty, exact frame correspondence, tampered boxes, receipts and unreviewed drafts. No new interface section, CI run or owner permission changes.

Additional test: a small animated nut. Spark localized/reviewed it in 43.4 s (13.1 s for review), but seven SAM cuts disagreed at four seams. The result remained inconclusive. An authorized continuous recovery reused the review (no further Spark), obtained 1799/1800 masks in 160 s of SAM time and restored θ/ω, vectors and trail in Edge. Continuous tracking cost/latency remains a limitation for small objects; seam validation was not weakened.

This update used five additional live tests: seven Spark and 23 SAM inferences including the inconclusive segmented attempt and its recovery. The campaign ledger has 19 of 30 entries (one ended before any provider call); the runner conservatively allows 11 more entries. Do not spend them routinely. Cumulative total: 19 Spark and 94 SAM. Angular production build passed with the existing initial-bundle warning: 703.54 kB / 500 kB.

## Three-part limit — 2026-10-30

New plans use at most three parts and three concurrent SAM requests, hence at most two seams. Original packet copying, full coverage, PTS, authentic Spark references and continuity thresholds are preserved. Sparse keyframes/references may reduce the count further.

A historical failure with more than three parts now quotes the current three-part plan without submitting it. A failed two/three-part batch may offer the existing continuous recovery through a new manual action. Eight-part archives and their outcomes remain intact. Refresh the quote before submission; stale windows are rejected before Meta calls.

Fewer seams do not guarantee accuracy or lower total latency: each request processes a longer fragment. A real comparison requires another authorized run; this change is checked without new inference.

Local verification for this update: 55 .NET planning/archive/seam tests. The real 80CM_1.MOV (1845 frames) produces 621/621/613-frame clips, 1855 billable frames with overlaps, estimated SAM US$0.371 and 45,345,235 total bytes. Decoded pixels match the original; maximum PTS error 0.001 ms. Local preparation took 24.8 s in this run. Zero Meta calls.
