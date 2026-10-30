# Body review with wider context — 2026-10-30

## Behavior

The existing Spark visual review receives the full scene, one unmarked body crop per reference, and three suspension crops. It adds neither an inference nor a UI section. A coarse proposal may point to the cord: the search region therefore retains at least 60% of original height and 40% of width, enlarged further by candidate size. Spark identifies the entire suspended body regardless of shape, material or color. Cords, hands and shadows are excluded.

Boxes use 0..1000 crop-relative coordinates and are mapped with exact crop dimensions and offsets. Edge-touching, uncertain or off-crop bodies cannot seed SAM. Crop geometry, image hashes and the provider response remain bound. V1/V2 archives stay readable; new reviews use SPARK_VISUAL_BODY_AND_EXIT_CROPS_V3. Reviewed pivots, originals, receipts and historical caches are preserved. No model training or retroactive rewriting of old masks.

## Five authorized inference calls

| # | Request | Request time | Estimated USD | Result |
|---|---|---:|---:|---|
| 1 | Spark, synthetic 50 cm control | 11.88 s | 0.0317795 | 10 references |
| 2 | Continuous SAM, control | 94.80 s | 0.2400000 | 1200/1200 masks |
| 3 | Spark, real 50 cm recording | 23.82 s | 0.06111875 | 4 references; 6 uncertain due to crop truncation |
| 4 | Spark, real with wider region | 18.74 s | 0.0577155 | 10 references, consistent suspension |
| 5 | Continuous SAM, real | 193.55 s | 0.3656000 | 1828/1828 masks |

Total estimated USD 0.75621375, not an invoice. The batch is exhausted; further agent inference requires renewed authorization. These are not five complete scientific studies. Previous whole-video Spark proposals were reused. Timings exclude offline preparation/validation. Requests were explicit, without automatic retries. Owner manual-call permissions remain enabled.

## Measurements and limits

- Synthetic 640×480, 60 FPS: centroid MAE 30.39→1.04 px, angle MAE 7.82→1.47 degrees, angular velocity MAE 0.385→0.059 rad/s; coverage 86.58→100%. Pivot error 39.99→7.18 px. Observed period 1.421054 s versus ground truth 1.418746 s.
- Both references/pivot and segmented-versus-continuous SAM changed; this does not isolate the causal effect of the crop. The synthetic live run used the initial 40%-height region. Only the real video live-tested the final 60% rule; offline tests cover final geometry.
- Real 50CM_1.MOV: 1828 masks, estimated pivot spread 3.32 px; period 1.415824 s and statistical uncertainty 0.000424 s (10 of 20 alternating complete cycles). This is type-A repeatability, not total uncertainty. No independent coordinate ground truth is available to certify angular accuracy. Visual inspection of original frames 300/600/900/1400 found body-aligned contours.
- Continuous SAM still took 193.55 s; latency is not solved and arbitrary object accuracy is not guaranteed.
- No new Muse verdict or frozen prediction; 80 cm and all prior videos were not rerun.

## Local verification

419 .NET tests and 97 Angular tests in Edge passed. .NET build/publish and Angular build passed; existing bundle warning 709.58/500 kB. No CI.

Edge component QA replayed the genuine saved SAM response for the H.264 control using isolated CDP interception with all POST requests blocked. Playback and changing angle/omega at 2/4/6 s, vectors and trail were verified. The component was enabled to consume this unregistered archive; no server study was created. This is not a new production end-to-end study. The real HEVC recording was audited offline on original frames, without claiming HEVC playback in the isolated Edge profile.

Private evidence is in .private/body-crop-results: five attempt/result pairs, raw replies, receipts, images, measurements.json, browser.json and science.json. Do not publish private replies, tokens or recordings. A user-facing numerical and visual report is available in the chat as prooflab-5-pruebas-cuerpo.html. See [Edge testing](EDGE-TESTING.en.md).
