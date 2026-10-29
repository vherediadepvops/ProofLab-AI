# Assembly recognition and per-element tracking

> Current status: **Recognize assembly** was removed from the lab at the user’s request. **Process** runs SAM directly using the session key and video estimate, without preliminary recognition or an extra US$0.05. The extended workflow below remains in the code; existing reviewed definitions and their cache remain valid. New interface components require prior consultation with the user.

The lab can propose identities for a suspended object of any color/material/shape
(such as a coin), cord, upper knot, actual pulley and support. Live Meta accuracy
for this extended workflow remains unverified.

## Retained extended workflow (not shown in the lab)

1. Load the original and open **Recognize assembly**. There is no automatic paid call.
2. Muse Spark 1.3 proposes role-specific boxes and the exact free-string exit in
   three native frames (10%, 50%, 90% of source indices). Maximum estimated
   recognition budget: US$0.05; input admission ceiling 24,000 tokens, output cap
   4,096 tokens. Token counting is not an invoice. Explicit consent and a key are required.
3. Inspect all three images, exclude wrongly identified optional elements, then
   confirm. Do not confirm if the object is wrong. The reviewed definition is immutable.
   Missing/unclear elements are not fabricated; a protractor is not a pulley.
4. Configure the single-use key again, review the separate video quote and press
   **Process**. SAM 3.1 receives source-frame boxes with distinct IDs: object 1,
   cord 2, knot 3, pulley 4, support 5. Only reviewed roles with at least one clear
   observation (self-reported confidence >= 0.8) are submitted.
5. Playback draws each role's mask and the object's silhouette centroid. A missing
   frame observation stays missing; another ID or an old position cannot replace it.

Muse identifies roles; SAM segments and tracks regions. The silhouette centroid
is not a certified physical center of mass. Knot center, pulley center and the
free-string exit are distinct. Only a stable, reviewed string exit in a simple
pendulum supplies the proposed pivot. Pulley/unclear arrangements retain masks
and positions without applying simple-pendulum angle/angular velocity.

## Evidence and limitations

Original resolution, orientation and timestamps are preserved. SAM's SQLite
cache is keyed by the source and reviewed scene; protocol, hashes, role IDs,
dimensions and original times are verified. Historical bob cache remains usable
without payment; new inferences can process a bob directly without prior assembly review.

Muse stores private original frames, response, hashes, attempt and review under
`.private/physics/assembly`. SAM retains its durable journal/receipt/SQLite flow.
The two separately quoted manual actions share the authorized request allowance.
Recognition never starts SAM, resets the allowance or retries a failed attempt.

Self-reported confidence is not calibrated accuracy. Thin strings, blur, glare,
occlusion and similar objects can prevent recognition. No guarantee is made for
every object or frame. Available angles remain suspension-to-visible-center axis
estimates; bent cords or offset attachments need a different geometric model.

No live paid calls were made for this multi-object implementation. Synthetic
offline fixtures are explicitly labeled. Local checks: 68 focused .NET tests,
83 Angular tests, Angular build and clean .NET publish. No CI. Existing initial
bundle warning remains (about 626 kB against 500 kB).

References: [SAM prompts and multi-object boxes](https://dev.meta.ai/docs/sam/segmenting),
[image understanding](https://dev.meta.ai/docs/image-understanding),
[token counting](https://dev.meta.ai/docs/token-counting),
[pricing](https://dev.meta.ai/docs/pricing-rate-limits).