# Stage 5 — Muse investigator using SAM evidence

Protocol V3 adds an explicit classification issued by Muse Spark: PENDULUM_COMPATIBLE, OTHER_MOTION or INCONCLUSIVE, with ES/EN reasoning. ProofLab supplies metrics and up to 48 observed centroids with PTS; it does not present local geometry diagnostics as a verdict. SAM with no detections supplies insufficient evidence. The same Muse evaluation interprets those data and proposes or rejects the next experiment; there is no extra automatic inference. V1/V2 responses retain their format and hash without an invented classification. See [responsibilities](SAM-PENDULUM-GENERIC.en.md).

The lab log below the observed-period panel prepares evidence locally. The server verifies the saved original and authentic SAM response, then runs the same TypeScript estimator used by the UI. Building the API requires Node and UI dependencies installed with `npm ci`; MSBuild generates the worker. No model-generated code or credentials enter the worker.

**Ask Muse** sends measured summaries, coverage, gaps, statistical uncertainty, declared physical parameters and limitations, without uploading the video again. It uses `muse-spark-1.3` with strict JSON Schema. One token-count request precedes at most one inference; both HTTP calls are included in the execution record. Estimated ceiling: US$0.05. No automatic retries; server credit, credential and per-request cost checks remain active.

Protocol V2 supplies experiment options computed by ProofLab. Muse selects and justifies one; JSON Schema limits numeric fields to those values and the server checks the matching length/period pair. Protocol version is part of observation identity, preserving earlier attempts.

Decisions are a conditional experiment proposal, repeat recording, or inconclusive. A proposal is neither scientific certification nor discovery. The app independently validates candidate measurements, a 0.10–2 m follow-up length at least 15% different, and T₂ = measured T₁ × √(L₂/L₁). The predeclared 1–10% comparison band is practical, not a confidence interval. Bilingual justification, limitations, falsifiable hypothesis and protocol are required.

## Evidence and freezing

Private SQLite at `<ContentRoot>/.private/physics/muse-experiments.sqlite` stores immutable observations and predictions plus raw provider attempts. Records retain provider ID/model/usage, response hash, payload hash and local timestamp. Failed or ambiguous attempts require reconciliation; refreshing cannot retry a charge.

Evaluation and prediction commit atomically. A study lock prevents a concurrent second upload. Source video, SAM analysis and pivot are checked before and after inference. The upload API requires a persisted Muse prediction and rejects the same video SHA. Historical records remain readable, but an old local proposal cannot authorize a new upload.

Hashes verify local integrity, not independent timestamps or filming chronology. Follow-up length must be measured from suspension to visible object center; this is not an independently measured center of mass.

## Server allowance

At the user’s request, `Meta:UnlimitedManualRequests=true` allows manual submissions without a session quota. Each click requires a valid key and authorizes the displayed cost; it never starts automatic retries. Admitted inferences remain counted beyond the process limit and are restored through `Meta:AlreadyUsedRequests`. Calls without a manual token retain `Meta:MaximumRequestsPerProcess` (default 2). This counter is neither an invoice nor an individual HTTP-request count.

## Local API

Base: `/api/science/director/pendulum/sessions/{id}/muse`.
GET reads state; POST `/prepare` reconstructs evidence without Meta; POST `/evaluate` requires explicit transfer/cost consent; GET `/report` downloads reproducible JSON. Writes require loopback, the local 4300 origin and `X-ProofLab-Manual-Action: muse-research`. Credentials are never persisted in SQLite or reports.

## Historical validation on 2026-10-28

Offline HTTP fixtures test strict output, arithmetic, immutable storage, atomic writes, concurrency, cost limits and absence of retries. Fixtures are not live Muse results. The live test on 2026-10-28 recorded a real bilingual Muse hypothesis, justification and protocol: change length from 0.50 to 0.80 m, predicted period 1.7909424812 s with a practical ±5% band. The prediction was frozen before video two and survived an API restart. An earlier inconsistent numeric response was rejected and retained. Two inferences plus two token-count calls cost an estimated US$0.0239455 total. Cached rereads consumed no allowance. This verifies integration, not the hypothesis against a second experiment. Repeat/inconclusive decisions do not unlock video two or fabricate a prediction.

Stage 6 still requires independent second-video SAM measurements, comparison between lengths, uncertainty and a reproducible conclusion. This implementation does not silently reuse the legacy local tracker as that scientific comparison.

Integration references: [models](https://dev.meta.ai/docs/models), [structured output](https://dev.meta.ai/docs/structured-output), [pricing](https://dev.meta.ai/docs/pricing-rate-limits). Token cost is an estimate, not an invoice.

## Second-video comparison — 2026-10-30

The existing lab log now accepts segmented SAM batches. adaptiveBatchId binds the original SHA, provider receipts and verified joins before measurement. An operator-selected follow-up length is respected; Muse must not claim autonomous selection. The second video uses its own authenticated tracking, never the legacy local trajectory.

After freezing a prediction and uploading video two, Verify comparison prepares its measurements, declared length and frozen band locally. Ask Muse sends this summary in one manual inference, estimated ceiling US$0.05, without a preliminary token-count call or automatic retry. Outcomes are within the predeclared band, outside it, or inconclusive. Muse may abstain but cannot reverse the numerical comparison or revise the frozen prediction. Differences cannot be attributed to friction without evidence.

Additional routes under the same base: GET /contrast, POST /contrast/prepare and POST /contrast/evaluate, with the existing local-origin protections. Private files at .private/physics/muse-contrasts/{id}/ retain evidence, attempt, raw response and evaluation using new files without overwrite. Predictions remain in SQLite. Repeating a completed evaluation reads its receipt without another inference; changing source, analysis or length of stored comparison evidence is rejected. A new experiment requires a new study.

Preexisting recordings support retrospective comparison. Uploading after JSON freezing establishes application order, not blinded prospective acquisition. Synthetic controls named synthetic- are identified as software validation; this marker does not detect synthetic videos. Length remains user-declared and statistical uncertainty excludes camera, pivot and calibration errors. Agreement does not prove a physical law or constitute discovery.

This update completes the SAM→Muse comparison inside the existing lab log, without new UI sections. Calibration and scientific validation remain separate. The ten-case campaign and limitations are retained in private evidence and shared memory.


## Full live workflow validation — 2026-10-30

Ten authorized completed cases: eight comparisons of six preexisting 50/80 cm recordings (three primary pairs and five cross combinations), one negative control using a 50 cm recording declared as 80 cm, and one known-period synthetic control. Cross combinations reused exact-SHA SAM evidence; every case requested fresh Muse hypothesis and contrast responses. These are not ten independent physical experiments.

Outcomes: seven within the predeclared band (one synthetic), two inconclusive because 80CM_2 lacked reliable suspension localization, and the negative control outside the band (-20.97%). All predictions and hashes preceded the second upload. Source/length rebinding rejection and idempotent reads without another inference were verified.

102 inferences were recorded, including two continuous recoveries after inconsistent SAM seams (80CM_1 and the first synthetic). Summed estimated budgets were US$ 7.1816, not an invoice or confirmed spend. Videos, full responses and receipts remain private.

Synthetic measured periods were 1.419011 s and 1.793957 s against known 1.418746 s and 1.794587 s (+0.019% and -0.035%). This does NOT validate angular accuracy: Spark displaced the pivot and SAM missed 13.4% of the first control's frames. Next priority is image-grounded pivot verification and tracking gaps; do not weaken validation to display values.

Validation: 401 .NET tests, 46 Angular tests and production build passed (existing bundle warning). Isolated Edge checks covered original H.264 playback, changing θ/ω, gaps without invented values, ES/EN contrast and a non-overflowing card at 390 px. HEVC originals decoded as audio without image in these profiles; this is not a claim about all browsers. No CI. Owner service permissions and counter preserved.
