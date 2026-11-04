# Results — ProofLab AI pendulum research

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier**
**30 October results and new production-record reconciliation on 4 November 2026.** [Español](RESULTS.es.md) · [Evaluation protocol](EVALUATION.md).

**Interpretation:** These are recorded development/evaluation outcomes, **not independently audited scientific accuracy statistics** and not proof of a new discovery. The central ten-case campaign used retrospective real videos, an intentional negative control and a synthetic control.

## New cohort: 25 contrasts from existing real recordings (4 November 2026)

**Source:** the 25 distinct owner-supplied recovery codes were queried **read-only** through HTTPS GET at `https://prooflab-ai.com`, backed by AWS Lightsail persistence (SQLite and files). This was **not** direct access to the AWS SQLite database file or an audit of raw Meta receipts/replies. Codes are withheld because they allow retrieval of private sessions.

**Checks:** all **25/25** records contained baseline and follow-up videos, a frozen Muse prediction, and a `COMPLETED` contrast evaluation. Saved verdicts were **18 within the predeclared ±5% practical band**, **6 outside**, **1 inconclusive**. These are not 25 scientifically correct predictions or a 72% independent physical accuracy rate.

| Recording pair (cm) | Observed T₁ (s) | Frozen predicted T₂ (s) | Observed T₂ (s) | Difference | Saved verdict |
|---|---:|---:|---:|---:|---|
| 50→80 | 1.457416 | 1.843501 | 1.816953 | −1.44 % | Within |
| 30→60 | 1.146180 | 1.620944 | 1.584520 | −2.25 % | Within |
| 20→40 | 0.962780 | 1.361576 | 1.310054 | −3.78 % | Within |
| 40→70 | 1.310054 | 1.733039 | 1.700859 | −1.86 % | Within |
| 20→30 | 0.962786 | 1.179167 | 1.146172 | −2.80 % | Within |
| 20→50 | 0.962790 | 1.522305 | 1.457424 | −4.26 % | Within |
| 20→60 | 0.962785 | 1.667593 | 1.584553 | −4.98 % | Within |
| 20→70 | 0.962790 | 1.801216 | 1.700904 | −5.57 % | Outside |
| 20→80 | 0.962786 | 1.925572 | 1.816956 | −5.64 % | Outside |
| 30→40 | 1.146179 | 1.323494 | 1.310066 | −1.01 % | Within |
| 30→50 | 1.146183 | 1.479715 | 1.457433 | −1.51 % | Within |
| 30→70 | 1.146163 | 1.750793 | 1.700876 | −2.85 % | Within |
| 30→80 | 1.146177 | 1.871699 | 1.816953 | −2.92 % | Within |
| 40→50 | 1.310060 | 1.464691 | 1.457446 | −0.49 % | Within |
| 40→60 | 1.310069 | 1.604501 | 1.584534 | −1.24 % | Within |
| 40→80 | 1.310054 | 1.852696 | — | — | Inconclusive |
| 50→60† | 1.457394 | 1.784936 | 1.584539 | −11.23 % | Outside |
| 50→70 | 1.457453 | 1.724482 | 1.700863 | −1.37 % | Within |
| 60→70 | 1.584567 | 1.711527 | 1.700871 | −0.62 % | Within |
| 60→80 | 1.584525 | 1.829652 | 1.816965 | −0.69 % | Within |
| 70→80 | 1.700897 | 1.818335 | 1.816948 | −0.08 % | Within |
| 10→20 | 0.713668 | 1.009279 | 0.962787 | −4.61 % | Within |
| 10→30 | 0.713714 | 1.236189 | 1.146177 | −7.28 % | Outside |
| 10→40 | 0.713674 | 1.427349 | 1.310047 | −8.22 % | Outside |
| 10→50 | 0.713724 | 1.595935 | 1.457416 | −8.68 % | Outside |

**How to read the figures:** observed T₁ and T₂ are stored video-derived estimates; predicted T₂ comes from the frozen Muse prediction, not from the workbook's theoretical column. Difference = `100 × (observed T₂ / predicted T₂ − 1)` (rounded to 2 decimals). The ±5% band is a practical predeclared tolerance, **not** a statistical confidence interval or independent accuracy certification. Valid second-video periods carry the estimator status `CANDIDATE`.

**Preserved exceptions and limitations:**

- **† 50→60 cm recording pair:** the session declared an initial length of **40 cm** even though the initial video is named `50CM_15G_272.4g.MOV`. The frozen prediction used the **declared** 40 cm and was **−11.23%** outside band; do not interpret this as a demonstrated failure of a correctly configured physical model.
- **40→80 cm:** the follow-up video was **inconclusive**, with `GAPS`, `TOO_FEW_CYCLES`, `LOW_COVERAGE` (just **3.07%** tracking coverage), and no valid observed T₂. Muse preserved `INCONCLUSIVE`.
- These 25 pairings reuse **eight existing recordings** labelled 10–80 cm. They are not 25 independently filmed physical trials. Pairings 10→60, 10→70 and 10→80 cm were absent from the owner-selected 25-case cohort.
- Effective length, pivot, camera geometry, systematic uncertainty and actual filming chronology remain uncalibrated or independently unverified. No retrievable IDs, private videos, raw provider replies, receipts or SQLite rows are published. GET reads caused **no new Meta requests**.

## Earlier 30 October campaign — at a glance

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

## Meta request usage and historical campaign register (28 October–3 November)

The owner's **Meta Developer Usage** screenshot for **28 October–3 November 2026**, with **All API keys** and **All models** selected, displayed **550 requests**, **11.5 million input tokens**, **621.1 thousand output tokens** and **9% cache hit rate**. The screen displayed **S/0.00 spent**; this is **not** a verified invoice, billing reconciliation or proof of zero cost. The screenshot is an account/project usage snapshot, **not** a publicly exportable request-by-request dataset. We cannot attribute all 550 requests uniquely to ProofLab experiments or prove that every request succeeded.

The table below lists **historical campaigns**, not independent experimental subjects or additive account totals. Counts are **model inferences recorded for that campaign**, except where explicitly distinguished from HTTP token counts. Two private JSON ledgers within the authorized project were summarized read-only on 3 November: the `fast-eight` ledger contains **19 Spark + 94 SAM = 113** call records, and the `full-science` ledger contains **16 Spark + 66 SAM + 20 Muse = 102**. Their statuses and grouped outcomes were checked without exposing IDs, private media or provider replies.

| Period / cohort | Recorded model inferences | Evidence and outcome | Do not double count |
|---|---:|---|---|
| 28 Oct — initial Muse investigator | **2 Muse** | First real response failed numerical validation; second was accepted and frozen. **4 Meta HTTP calls** including 2 token-count requests | 4 HTTP requests are **not** 4 inferences; separate integration test |
| 28 Oct — first real pendulum SAM video | **1 SAM** | 1,781 masks from 1,828 source frames; 97.43% tracking coverage; visual/manual verification still limited | Tracking coverage is not physical accuracy |
| 29 Oct — automatic values on new videos | **12: 6 Spark + 6 SAM** | Six test flows; some videos displayed angle/velocity, others were inconclusive | Possible cross-campaign reuse is not resolved |
| 29–30 Oct — segmented videos and object review | **113: 19 Spark + 94 SAM** | Final `fast-eight` ledger: 18 API test runs, one additional record without provider calls; mixed successful/inconclusive object tracking | Earlier **83** (12 Spark + 71 SAM) is a **subset of 113**, not another campaign total |
| 30 Oct — ten research contrasts | **102: 16 Spark + 66 SAM + 20 Muse** | Separate `full-science` ledger: **7 within band, 2 inconclusive, 1 intentional negative outside**; six real recordings reused | Reused media/cache and 10 cases do **not** mean 10 independently filmed experiments |
| 30 Oct — pivot refinement | **5 Spark** | Synthetic 80 cm pivot improved; some real suspension cases remained uncertain | Reuses older SAM masks; do not count five new SAM inferences |
| 30 Oct — body-region validation | **5: 3 Spark + 2 SAM** | Real 50 cm recording and synthetic control; closer tracking but no independent angular ground truth | Five calls, **not** five complete scientific studies |
| 3 Nov — full video / moving-contact investigations | **42: 28 Spark + 7 SAM + 7 Muse** | Seven technically completed pipelines; **one candidate measurement**, six unresolved/repeat or inconclusive decisions; no new verified two-video contrast | Initial **30** calls in five runs are **included in 42** after two additional runs (12 more) |

References: [automatic pendulum values](AUTOMATIC-PENDULUM-VALUES.en.md), [segmented-video architecture](SAM-NATIVE-SEGMENTS.en.md), [Muse scientific contrasts](MUSE-INVESTIGATOR.en.md), [pivot refinement](SUSPENSION-REFINEMENT.en.md), [body validation](BODY-CROP-VALIDATION.en.md), and the [3 November frame-index correction](https://github.com/vherediadepvops/ProofLab-AI/commit/8a75fff0615f712ae496bf55919a916c34143b00). Detailed request-level ledgers are intentionally private.

**Do not add the rows into one claimed total.** Some rows are known cumulative subsets; cross-campaign request-ID reconciliation is incomplete. In particular, **550** is the provider-UI request count in a selected date range, **113** and **102** are validated local-ledger cohort totals, and **10** is a count of test cases. These measure different things. Some other documented experiments are not itemized here, so the register is **not an exhaustive enumeration** of all 550 requests.

**Automated software tests are separate:** the 3 November `b651232` check passed 153 .NET and 101 Angular tests; an earlier 3 November review documented 539/539 .NET tests. Test suites, revisions and fixtures overlap and **must not be summed as unique tests, physical trials or Meta requests**.

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

**Next evidence needed:** independent length/pivot/camera calibration, per-case receipt reconciliation, genuinely prospective second recordings after prediction freeze, and complete systematic uncertainty. The 4 November review read existing data by GET only, without new Meta inferences.
