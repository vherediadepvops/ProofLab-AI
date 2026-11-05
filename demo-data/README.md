# ProofLab AI — Public pendulum demo data (4 November 2026)

[Español](README.es.md) · [Main project](../README.md) · [Judge installation guide](../JUDGE-SETUP.md) · [Evidence scope](../docs/SUBMISSION-EVIDENCE-2026-11-04.md)

This is a **standalone, sanitized, reproducible result catalog**, extracted through read-only GET requests from ProofLab AI's AWS Lightsail production application. The owner selected **25 research records**. Their private recovery identifiers are intentionally **not published**; cases use aliases `PEND-01` through `PEND-25`.

## What the 25 records actually say

| Stored comparison outcome | Records |
| --- | ---: |
| Within predeclared practical tolerance | 18 |
| Outside predeclared practical tolerance | 6 |
| Inconclusive (no accepted follow-up period) | 1 |
| **Total** | **25** |

**24 records contain all three numerical periods:** measured baseline T₁, frozen prediction for T₂, and measured follow-up T₂. The 25th retains its inconclusive decision with a missing measured T₂ rather than inventing a result.

Every case was checked against its saved prediction and contrast, including study identity, original-video SHA-256 identity, prediction hash, **prediction frozen before the follow-up was uploaded into ProofLab**, and source SHA-256 agreement with the public eight-video gallery manifest. This does **not** prove the second recording was filmed after prediction.

These are **25 pairwise uses of eight previously filmed source recordings**, not 25 independent physical experiments. A stored in-band verdict is not independently calibrated pendulum accuracy. Human pivot calibration and the complete physical uncertainty budget remain open where indicated.

## Contents

- `pendulum-cases.json`: complete curated numeric summaries, declared bands, observation statuses, source-media hashes, chronology/review flags, and recorded decisions.
- `pendulum-cases.csv`: one flat row per case, ready for a spreadsheet.
- `build-demo-db.mjs`: offline validation and creation of an independent SQLite database (no npm dependencies, **Node.js 24+**).

The eight original videos are **downloaded from the project's existing public gallery**, not committed to Git or bundled with this database. Each row links to the corresponding already-public video and gives its manifest SHA-256.

## Rebuild the SQLite demonstration database (Windows, macOS or Linux)

With Node.js 24+ installed, from the repository root:

```sh
node demo-data/build-demo-db.mjs --verify
node demo-data/build-demo-db.mjs prooflab-demo.sqlite
```

The tool refuses to overwrite an existing database. The generated `prooflab-demo.sqlite` is deliberately ignored by Git to protect operational databases. Open it with any SQLite viewer and run:

```sql
SELECT case_id, baseline_length_m, followup_length_m,
       measured_t1_s, predicted_t2_s, measured_t2_s, recorded_verdict
FROM pendulum_cases ORDER BY case_id;
```

Tables: `pendulum_cases` (25), `public_recordings` (8), `dataset_metadata`. No network connection or Meta API key is required to rebuild or inspect them.

## Important limits and privacy

This SQLite file is an **independent review catalog**, not the application's private operational SQLite or a replay of the original per-frame SAM masks and provider receipts. Starting the ProofLab UI does **not** automatically list these 25 studies; the CSV/SQLite gives judges an offline way to inspect their recorded numerical comparisons. An authentic application session replay would require a separately authorized migration of private original evidence and is intentionally **not** performed here.

No access tokens, recovery codes, personal records, raw provider replies, unpublished footage, or production databases are included. Values are copied from stored study state, not re-inferred or corrected, and no new paid model request was made during export.

**Evidence level:** reproducible public *catalog of stored outcomes*, not independent re-execution of the paid scientific experiments.
