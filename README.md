# ProofLab AI — Turn the Real World into a Scientific Laboratory

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier · Built in Peru 🇵🇪**

Turn an ordinary experiment video into traceable motion measurements, a testable prediction, and a comparison with a second recording. **ProofLab AI is a working research prototype**, not a claim of a new physical law or independently certified measurement accuracy.

**[Try the live laboratory](https://prooflab-ai.com/)** · **[Watch the English 3-minute demo](https://youtu.be/75i78EO8xZo)** · [Español](README.es.md) · [Public results](docs/RESULTS.md) · [Evaluation protocol](docs/EVALUATION.md)

## Watch the three-minute demonstration 🌎

| Language | Demo |
| --- | --- |
| English | [Watch on YouTube](https://youtu.be/75i78EO8xZo) |
| Español | [Ver en YouTube](https://youtu.be/D1O1ZFvc0q0) |
| Português | [Assistir no YouTube](https://youtu.be/EXuD3RJayIk) |
| 简体中文 | [观看演示](https://youtu.be/quHyAHKxzZ8) |

These are language versions of the **demonstration**; the application interface and scientific reports support **English and Spanish**, not all four languages.

## From a video to a falsifiable experiment

1. **Prepare:** upload the first pendulum video and enter the measured string length, initial angle and bob mass. The original recording is retained with file identity and timestamps.
2. **Analyze:** Meta **Muse Spark 1.3** grounds the experimental object in the video and reviews visual references; **SAM 3.1** segments and tracks it through the recording.
3. **Understand:** ProofLab's **deterministic measurement engine** uses tracked geometry and original frame timestamps to estimate the observed oscillation period, angular position and angular velocity. Muse Spark interprets the evidence and limitations.
4. **Predict:** the system validates and **freezes** a numerical prediction and practical tolerance *before the second video's analysis*. A person confirms the experimental proposal.
5. **Compare:** the second recording is independently processed and its observed period is compared with the frozen prediction. The outcome can be **within tolerance, outside tolerance or inconclusive**.

The measurement engine **does not use the predicted period to fit the observed period**. SAM detections, calculated measurements, Muse interpretations, simulations and human review are kept distinct.

## Experimental results — updated 4 November 2026

A **read-only retrieval from the production application's stored records** confirmed 25 completed two-video contrast workflows. All 25 had both video identities, a frozen Muse prediction and a stored evaluation:

| Saved outcome | Cases |
| --- | ---: |
| Within the predeclared ±5% practical band | **18** |
| Outside the band | **6** |
| Inconclusive | **1** |
| **Total stored contrasts** | **25** |

These cases reused **eight previously recorded videos** labelled 10–80 cm; they are **not 25 independent or prospectively filmed experiments**, and 18/25 is **not a scientific accuracy rate**. In one outside-band case, the declared initial length did not match the recording label. The inconclusive case had only 3.07% second-video tracking coverage. No new Meta inference was made to retrieve these saved outcomes.

A separate **30 October** research campaign recorded **10 comparisons**: 7 within their practical bands (including a synthetic control), 2 inconclusive and 1 deliberately incorrect-length negative control outside. This earlier cohort is **not additive evidence of independent filming**. Provider request counts and automated software tests are reported separately, without double counting.

**[See all 25 per-pair results, failures and qualifications](docs/RESULTS.md)** · [Submission evidence](docs/SUBMISSION-EVIDENCE-2026-11-04.md) · [Evaluation and reproducibility](docs/EVALUATION.md)

**Public reproducibility pack:** [Sanitized 25-case CSV/JSON catalog and offline SQLite builder](demo-data/README.md). This standalone dataset lets judges inspect stored numerical outcomes without private recovery codes; it is not a replay of the original Meta inferences.

## How Meta's models are used

| Component | Responsibility |
| --- | --- |
| **Meta Muse Spark 1.3** | Multimodal scene and object interpretation, evidence assessment, hypothesis and follow-up planning, interpretation of the numerical comparison |
| **Meta SAM 3.1** | Object segmentation masks and tracking across the original video frames |
| **ProofLab measurement engine** | Original-timestamp trajectory/period calculations, quality checks, uncertainty indicators and reproducible reports; independent of model-generated numbers |
| **ASP.NET Core (.NET 10), Angular 20, SQLite, FFmpeg** | Experiment workflow, immutable prediction, consent and cost gates, source integrity, evidence persistence and ES/EN interface |

The documented **3 November** pipeline prepares a full-video MP4 and 50 original-frame references, uses one Spark object review, and sends one continuous full-video request to SAM. The estimated ceiling for **two Spark calls is US$0.35** under that documented plan; it is **not an invoice or a new accuracy result**. Existing segmented historical evidence remains readable.

Manual Meta requests require an API token, an explicit action and displayed cost consent, with backend authorization controls. The token is kept only in the session's password field and is cleared on request, connection change or reload; it is **not cleared after each successful request**. Failed paid requests are never retried automatically.

[Detailed Muse/SAM workflow](docs/MUSE-INVESTIGATOR.en.md) · [Period estimator](docs/SAM-PERIOD.en.md) · [Production operations](docs/PRODUCTION-LINUX.md)

## Scientific integrity and known limitations

- A practical **±5% tolerance band is not a 95% statistical confidence interval** or a measurement-accuracy certification.
- Effective string length, physical pivot position, camera perspective and systematic uncertainty still require **independent calibration**. Statistical repeatability alone does not cover those errors.
- Saved prediction timestamps establish ordering **inside the application**, not that the second physical recording was filmed after the prediction.
- Full raw provider replies, private video files, database records and recovery codes are deliberately **not published**. Public software tests and methods can be examined, but the original live-model cases cannot all be replayed from public assets alone.
- The latest processing changes have software/browser verification; that does **not** constitute new provider-backed scientific validation.

This project distinguishes an observed measurement, a generated interpretation, a synthetic control, a known theorem and a hypothesis. **No autonomous scientific discovery or universal physical accuracy is claimed.**

[Experimental protocol](docs/EVALUATION.md) · [Results with adverse outcomes](docs/RESULTS.md) · [Experiment provenance](docs/PROVENANCE.md)

## Code, documentation and local development

**Open-source project code: [MIT License](LICENSE).** Third-party components retain their own terms, including the [Meta SAM parser license](ui/third-party/META-SAM-LICENSE.txt).

**Prerequisites:** .NET SDK 10, Node.js and npm, and FFmpeg/FFprobe for video processing. For local development, install Angular dependencies before building the backend's video-measurement worker:

```powershell
cd ui
npm ci
cd ..
dotnet test tests/ProofLab.Tests/ProofLab.Tests.csproj
```

The main laboratory uses an ASP.NET Core API and Angular UI. Existing development defaults use ports **5391** and **4300**; **never start another instance on occupied ports**. Use isolated free ports and the [Edge local-testing guide](docs/EDGE-TESTING.en.md) when evaluating alongside another session. Testing a live Meta inference is **not required** for offline software tests.

Additional documentation: [Scientific method](docs/SCIENTIFIC-METHOD.md) · [Physical experiments](docs/EXPERIMENTS.md) · [Language policy](docs/LOCALIZATION.md) · [Historical pendulum player design](docs/PENDULUM-ZONE1-MASTER-PLAYER.md) · [Earlier mathematics/Lean research](docs/MISSION-001-MATH.md). These are **dated historical or parallel research modules**, not claims that all features are active in the present pendulum demonstration.

**Competition provenance:** the public competition codebase was created after the **26 October 2026** kickoff and is distinct from the pre-event private prototype. See [provenance](docs/PROVENANCE.md).

**Support the project:** [ProofLab AI's demo on X](https://x.com/vherediaE/status/2118103090998260005).
