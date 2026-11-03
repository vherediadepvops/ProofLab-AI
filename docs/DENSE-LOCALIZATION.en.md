# Localization and suspension review

Current protocol: `SPARK_ORIGINAL_50_CONTACT_AUDIT_V3` (2026-11-03).

## Workflow

1. Local preparation produces a compatible video and 50 original images spread across its timeline. It makes no Meta calls.
2. The first Spark request receives the full video and those 50 labeled images. It proposes body boxes and suspension positions as image estimates.
3. The second request reviews the body and suspension contact on nine original references (ordinals 0, 6, 12, 18, 24, 31, 37, 43 and 49). It receives nine full scenes with a grid and proposed suspension marker, each immediately followed by its matching body crop. There are no suspension crops or ×1.5/×3 rounds.
4. Review follows the loaded cord to its effective support contact across different times. It distinguishes that contact from knot/protractor centers, shadows and a moving lower loop. Spark returns the independently observed location, visibility and a short visual explanation. ProofLab computes whether it agrees with the candidate (CONFIRMED), changes position (CORRECTED), or is unusable (UNCERTAIN); that comparison is no longer delegated to the model. Contact coordinates always refer to the full scene; body boxes refer to their paired crop.
5. SAM retains its single full-video request when the body is resolved. Suspension must pass review and geometric checks before producing angular values. An unresolved suspension can use existing manual marking; it does not trigger another automatic inference.

## Evidence and acceptance

The initial 50 observations and consensus remain unchanged. The audit retains its own nine observations, decisions, original references/times and consensus; nine observations are never represented as fifty. Marked-scene pixels, crops and responses are hash-bound, and readers reconstruct derivations from preserved receipts.

Acceptance requires a static camera, at least six usable observations out of nine, and a dominant cluster covering at least 60% of usable observations. Maximum spread is the greater of 3 px or 0.5% of the smaller original-image dimension; accepted points must span at least three temporal bins and 60% of the video. A moving contact, contradictory confirmation, competing clusters or insufficient evidence leaves suspension unresolved. A stable correction can replace the position used for calculations without deleting the original proposal. A missing audit is never filled with the initial consensus or a circle fit.

Agreement, reported confidence and review by the same model do not establish physical accuracy. Estimates still require human confirmation; mocked responses test software behavior, not Spark accuracy on new videos.

## One principal SAM reference

New reviews use `SAM_SINGLE_PRINCIPAL_REFERENCE_V2`. Spark explicitly reports whole-outline visibility, occlusion (such as a hand) and clipping. At least two complete, unobstructed, unclipped references with coherent geometry and size/aspect are required. Selection takes the earliest eligible reference, even if it follows the start of the video. Partial observations remain preserved but cannot initialize SAM. Its exact source frame and coordinates are retained; boxes are never invented or interpolated. Selection is an image heuristic, not a guarantee of object identity or physical accuracy.

Only that box enters the SAM prompt. Other observations remain in the scene to corroborate reference selection and compare masks at their exact frames; they do not give SAM another starting reference. The policy and exact reference are bound to the quote and receipt. Historical receipts reconstruct the prompt actually sent.

## Tracking continuity review

A local check looks for abrupt area changes accompanied by an unexpected centroid displacement that persists across several frames. It uses native timestamps and reports missing detections separately. The existing status line shows the first time requiring review, missing detections and unresolved suspension separately. Comparisons with other Spark boxes are advisory: those boxes may themselves be wrong and are not human labels.

This warning does not prove that SAM selected a different object, and does not change original masks, measurement filters or saved reports. Gaps retain missing values and derivatives require valid neighbours. It makes no Meta calls, retries nothing and does not fill missing positions. Single-reference effectiveness requires an authorized live test; local tests verify selection, integrity and discontinuity detection.

## Cost and compatibility

Two Spark requests remain: US$0.25 initial estimate and US$0.10 review (US$0.35 total estimate), plus quoted SAM and Muse costs. Review keeps 18 images and an 8192-token output cap; additional explanations can change actual usage. These are not invoices or provider-enforced billing limits. No automatic retries.

`SPARK_ORIGINAL_50_CONTACT_AUDIT_V2` and `SAM_SINGLE_PRINCIPAL_REFERENCE_V1` retain their exact readers, decisions and hashes; contradictory V2 confirmations remain rejected. Three-anchor results, dense rounds, progressive zoom and `SPARK_ORIGINAL_50_OBJECT_REVIEW_V1` retain their historical readers. Saved analyses are not modified or relabeled as audited results. Explicit additional suspension review remains bound to its exact SAM result; it is not an automatic fallback.

### Visible point in the first source frame

`SPARK_ORIGINAL_50_BODY_POINT_REVIEW_V4` adds an independently observed interior body pixel in original frame 0, using the same scene/crop pair and review request. A point can remain visible when a hand hides part of the outline; this does not make the box a complete-body reference or reconstruct hidden pixels. Ambiguous points must be marked invisible. A box center is never substituted.

The point maps from crop coordinates to native pixels and remains bound to verified response, image and scene provenance. With at least two clear observations of the same body, `SAM_INITIAL_BODY_POINT_WITH_CLEAR_REFERENCES_V3` submits that initial point as the sole tracking prompt; later boxes remain audit evidence. SAM receives the entire video starting at its first frame, without resets from later boxes. Without a usable point, V4 permits only a clear complete-body box in the first frame. If neither exists, it requests a visible initial reference and does not dispatch SAM; it never silently falls back to the backward path that failed in this case. Historical V2/V3 records retain their protocols and hashes. Suspension auditing continues to use V3.
