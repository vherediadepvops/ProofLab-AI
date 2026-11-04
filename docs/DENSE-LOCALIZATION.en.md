# Localization and suspension review

Current protocol: `SPARK_ORIGINAL_50_VISIBLE_BODY_POINT_REVIEW_V5`. Suspension auditing retains `SPARK_ORIGINAL_50_CONTACT_AUDIT_V3`.

## Workflow

1. Local preparation produces a compatible video and 50 original images spread across its timeline. It makes no Meta calls.
2. The first Spark request receives the full video and those 50 labeled images. It proposes body boxes and suspension positions as image estimates.
3. The second request reviews the body and suspension contact on nine original references (ordinals 0, 6, 12, 18, 24, 31, 37, 43 and 49). It receives nine full scenes with a grid and proposed suspension marker, each immediately followed by its matching body crop. V5 tightens the frame-0 crop when a visible initial proposal has sufficient reported confidence; the other eight references retain their broad context. This remains the same review, with no additional images or requests. There are no suspension crops or ×1.5/×3 rounds.
4. Review follows the loaded cord to its effective support contact across different times. It distinguishes that contact from knot/protractor centers, shadows and a moving lower loop. Spark returns the independently observed location, visibility and a short visual explanation. ProofLab computes whether it agrees with the candidate (CONFIRMED), changes position (CORRECTED), or is unusable (UNCERTAIN); that comparison is no longer delegated to the model. Contact coordinates always refer to the full scene; body boxes refer to their paired crop.
5. SAM retains its single full-video request when the body is resolved. Suspension must pass review and geometric checks before producing angular values. An unresolved suspension can use existing manual marking; it does not trigger another automatic inference.

## Evidence and acceptance

The initial 50 observations and consensus remain unchanged. The audit retains its own nine observations, decisions, original references/times and consensus; nine observations are never represented as fifty. Marked-scene pixels, crops and responses are hash-bound, and readers reconstruct derivations from preserved receipts.

Acceptance requires a static camera, at least six usable observations out of nine, and a dominant cluster covering at least 60% of usable observations. Maximum spread is the greater of 3 px or 0.5% of the smaller original-image dimension; accepted points must span at least three temporal bins and 60% of the video. A moving contact, contradictory confirmation, competing clusters or insufficient evidence leaves suspension unresolved. A stable correction can replace the position used for calculations without deleting the original proposal. A missing audit is never filled with the initial consensus or a circle fit.

Agreement, reported confidence and review by the same model do not establish physical accuracy. Estimates still require human confirmation; mocked responses test software behavior, not Spark accuracy on new videos.

## One principal SAM reference

The box policy `SAM_SINGLE_PRINCIPAL_REFERENCE_V2` remains available to reconstruct its receipts and select complete references. Spark explicitly reports whole-outline visibility, occlusion (such as a hand) and clipping. At least two complete, unobstructed, unclipped references with coherent geometry and size/aspect are required. Selection takes the earliest eligible reference, even if it follows the start of the video. Partial observations remain preserved but cannot initialize SAM as complete boxes. V5 requires the admissible first-frame interior point described below. A complete box, even in the first frame, does not replace that V5 validation; earlier policies retain their own rules when reconstructing historical receipts. Its exact source frame and coordinates are retained; boxes are never invented or interpolated. Selection is an image heuristic, not a guarantee of object identity or physical accuracy.

When the box policy is used, only that box enters the SAM prompt. Other observations remain in the scene to corroborate reference selection and compare masks at their exact frames; they do not give SAM another starting reference. The policy and exact reference are bound to the quote and receipt. Historical receipts reconstruct the prompt actually sent.

## Tracking continuity review

A local check looks for abrupt area changes accompanied by an unexpected centroid displacement that persists across several frames. It uses native timestamps and reports missing detections separately. The existing status line shows the first time requiring review, missing detections and unresolved suspension separately. Comparisons with other Spark boxes are advisory: those boxes may themselves be wrong and are not human labels.

This warning does not prove that SAM selected a different object, and does not change original masks, measurement filters or saved reports. Gaps retain missing values and derivatives require valid neighbours. It makes no Meta calls, retries nothing and does not fill missing positions. Single-reference effectiveness requires an authorized live test; local tests verify selection, integrity and discontinuity detection.

## Cost and compatibility

Two Spark requests remain: US$0.25 initial estimate and US$0.10 review (US$0.35 total estimate), plus quoted SAM and Muse costs. Review keeps 18 images and an 8192-token output cap; additional explanations can change actual usage. These are not invoices or provider-enforced billing limits. No automatic retries.

`SPARK_ORIGINAL_50_CONTACT_AUDIT_V2` and `SAM_SINGLE_PRINCIPAL_REFERENCE_V1` retain their exact readers, decisions and hashes; contradictory V2 confirmations remain rejected. Three-anchor results, dense rounds, progressive zoom and `SPARK_ORIGINAL_50_OBJECT_REVIEW_V1` retain their historical readers. Saved analyses are not modified or relabeled as audited results. Explicit additional suspension review remains bound to its exact SAM result; it is not an automatic fallback.

### Visible point in the first source frame

V5 reviews the interior pixel in original frame 0 introduced by `SPARK_ORIGINAL_50_BODY_POINT_REVIEW_V4`. In the tighter initial crop, Spark must identify the point material and a small rectangle entirely on a visible surface of the same body observed later. It must exclude hands, nails, cord, holes, shadows, background and added markings. The point is observed independently with clearance from every edge: it is not calculated as the rectangle center or copied from another frame. A hand can hide part of the outline without hiding all of the surface; this does not make a partial box a complete-body reference.

ProofLab maps the point and surface from crop to original coordinates and locally checks material, visibility, reported confidence, surface size, interior clearance of the exact native pixel sent to SAM, and agreement with the initial location. Unresolved identity or consistency produces `INITIAL_POINT_IDENTITY_UNRESOLVED` without sending SAM. These checks do not certify that every pixel belongs to the body or guarantee physical accuracy.

The point remains bound to verified response, image and scene provenance. With at least two clear complete-body references, `SAM_INITIAL_BODY_POINT_WITH_CLEAR_REFERENCES_V3` submits the admissible initial point as the sole tracking prompt; later boxes remain audit evidence. SAM receives the entire video starting at its first frame, without resets from later boxes. Without a usable V5 point, SAM is not sent: coordinates are not invented and a box or later start is not activated automatically.

Stored points from before V5 cannot authorize a new SAM request or a retry that reuses that reference. `SAM_INITIAL_REFERENCE_REVIEW_REQUIRED` asks the user to keep Meta API enabled, turn off Cache + SQLite, refresh the cost and confirm Process for a new review. Complete, valid saved SAM results remain available without new inference. Historical V2/V3/V4 protocols, hashes and responses are preserved and are not relabeled as V5 review. Suspension auditing continues to use V3.
