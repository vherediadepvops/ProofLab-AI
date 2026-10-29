# Stage 5 — Muse investigator using SAM evidence

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

## Validation boundary

Offline HTTP fixtures test strict output, arithmetic, immutable storage, atomic writes, concurrency, cost limits and absence of retries. Fixtures are not live Muse results. The live test on 2026-10-28 recorded a real bilingual Muse hypothesis, justification and protocol: change length from 0.50 to 0.80 m, predicted period 1.7909424812 s with a practical ±5% band. The prediction was frozen before video two and survived an API restart. An earlier inconsistent numeric response was rejected and retained. Two inferences plus two token-count calls cost an estimated US$0.0239455 total. Cached rereads consumed no allowance. This verifies integration, not the hypothesis against a second experiment. Repeat/inconclusive decisions do not unlock video two or fabricate a prediction.

Stage 6 still requires independent second-video SAM measurements, comparison between lengths, uncertainty and a reproducible conclusion. This implementation does not silently reuse the legacy local tracker as that scientific comparison.

Integration references: [models](https://dev.meta.ai/docs/models), [structured output](https://dev.meta.ai/docs/structured-output), [pricing](https://dev.meta.ai/docs/pricing-rate-limits). Token cost is an estimate, not an invoice.