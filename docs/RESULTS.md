# Results — ProofLab AI pendulum research

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier**
**Historical results compiled on 3 November 2026.** [Español](RESULTS.es.md) · [Evaluation protocol](EVALUATION.md).

**Interpretation:** These are recorded development/evaluation outcomes, **not independently audited scientific accuracy statistics** and not proof of a new discovery. The central ten-case campaign used retrospective real videos, an intentional negative control and a synthetic control.

## At a glance

| Measurement | Recorded result | Scope |
|---|---|---|
| Full Muse/SAM research cases (30 Oct) | **10** | 8 comparisons assembled from 6 existing physical recordings + 1 negative control + 1 synthetic control |
| Within predeclared practical band | **7/10** | **Includes** the synthetic control; not a 70% accuracy or success rate |
| Inconclusive | **2/10** | Uncertain suspension for real follow-up video 80CM_2 |
| Outside band | **1/10** | Deliberately mismatched 50 cm video declared as 80 cm (negative control, **−20.97%** comparison deviation) |
| Provider inferences in this campaign | **102 recorded** | Includes recovery attempts; does not mean 102 independent scientific studies |
| Sum of estimated per-case budgets | **US$7.1816** | **Estimate, not an invoice or confirmed spend** |
| Focused checks for 3 Nov release `b651232` | **153 .NET / 101 Angular tests passed** | Local checks and production UI inspection; **0 new Meta inferences**; not a certification of later commits |

**Case breakdown:** of the eight real-video comparisons, six were within their practical bands and two were inconclusive; the synthetic control was within band; the deliberate wrong-length negative control was outside. Different pairings reuse recordings, so the outcomes must not be treated as statistically independent.

## Individual scientific controls and observed values

| Evidence | Observed result | What it means / does not mean |
|---|---|---|
| Synthetic 50 cm control (known period) | **1.419011 s** measured vs **1.418746 s** known (**+0.019%**) | Numerically close period; pivot and tracking problems remained |
| Synthetic 80 cm control (known period) | **1.793957 s** measured vs **1.794587 s** known (**−0.035%**) | Numerically close period, **not** proof of real-world angular accuracy |
| Real 50CM_1 after body-review changes (separate 5-call validation) | **1.415824 s** observed period; type-A standard uncertainty **0.000424 s** from 10 of 20 alternating complete cycles | Real source recording and genuine SAM tracking; no independent pivot ground truth |
| Real 50CM_1 SAM result in that validation | **1,828/1,828** masks returned; historical continuous SAM request **193.55 s** | Mask coverage and latency, **not** proof that each mask follows the correct body |
| Synthetic tracking comparison in that separate validation | Centre mean error **30.39 → 1.04 px**, angular mean error **7.82° → 1.47°** | Also changed segmented → continuous SAM and reference region, so the improvement **cannot be attributed to the crop alone** |

The separate five-call validation used three Spark and two SAM inferences (estimated **US$0.75621375**, not a billing receipt). It is **not** five additional full scientific studies.

## Failures and inconclusive evidence

- **Suspension uncertainty:** two retrospective comparisons involving `80CM_2` remained inconclusive. We did not force a pass by altering the preregistered criteria.
- **Intentional negative control:** the deliberately incorrect follow-up length produced an outside-band discrepancy of **−20.97%**. This is a useful rejection test, not a failure of a real 80 cm experiment.
- **Synthetic geometry gap:** even when the synthetic periods differed from truth by less than 0.04%, Spark displaced the pivot and SAM missed **13.4%** of frames in the first control. A good period does not establish correct angle or angular velocity.
- **Browser playback:** original HEVC/MOV recordings showed audio without video in two isolated Edge profiles during the October campaign; original timestamps/masks still supported analysis. This does not generalize to all browsers.
- **Physical calibration:** real pivot coordinates, lens/perspective effects, length measurements and the complete systematic uncertainty budget remain unverified.

See [SAM period method](SAM-PERIOD.en.md), [Muse research workflow](MUSE-INVESTIGATOR.en.md), and [body-review validation](BODY-CROP-VALIDATION.en.md) for the dated underlying claims and qualifications.

## Recent engineering changes are not new scientific experiments

On **3 November 2026**, release [`b651232`](https://github.com/vherediadepvops/ProofLab-AI/commit/b6512323e7f72f0d718af30be648c8a59baaf648) changed the new-video plan to **one full video + 50 original references, one object review and two Spark calls**, removing automatic zoom-contact review rounds. The quoted maximum for those Spark calls decreased from **US$0.85 to US$0.35** in the documented plan. Background preparation and historical evidence compatibility were preserved.

That release passed **153 .NET and 101 Angular** focused tests, build/publish checks, and local/production ES/EN browser verification at desktop and mobile widths. **No new provider inferences or independent pivot-accuracy study were run for that release**. Later `main` changes are not automatically covered by that verification.

## Evidence, privacy and reproducibility

- **Public:** source and tests, [experimental protocol](EVALUATION.md), [Muse investigator](MUSE-INVESTIGATOR.en.md), [SAM period estimator](SAM-PERIOD.en.md), [body-review measurements](BODY-CROP-VALIDATION.en.md), and dated public commit links.
- **Private:** original recordings, full provider payloads/receipts, per-case ledger and SQLite. No confidential media, credentials or private histories are embedded here.
- **Reproducibility limitation:** public automated tests and formulas can be rerun; **the complete ten-case live-provider experiment cannot currently be independently replayed from public data**.

**Next evidence needed:** independent physical pivot calibration, genuine prospective second recordings after prediction freeze, and provider-backed end-to-end testing on the latest review pipeline. No Meta requests or local experiment runs were made to create this document.
