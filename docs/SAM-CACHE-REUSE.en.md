# Reusing SAM measurements

Updated: 2026-10-30. [Español](SAM-CACHE-REUSE.es.md).

Process in automatic mode and **Meta API · segments** first checks saved evidence for the exact file SHA-256. A usable analysis costs US$ 0.00 and loads its exact identifier without a token or Meta calls. Otherwise, the existing Spark and up-to-three-segment SAM workflow is quoted, including its single continuous retry when eligible. Explicit Meta API mode retains fresh analysis.

## Reuse criteria

Hashes, receipts, original timestamps and segmented seams are validated. The existing player/report calculations must resolve an automatic suspension and return a CANDIDATE period: positive period and at least three used cycles. Geometry checks are not relaxed. Older segmented and continuous results are searched; a faulty recent attempt does not automatically hide an older usable one.

SQLite stores quality verdicts keyed by a fingerprint of policy, analysis, receipt, suspension, review and derivation. Corrections produce a different fingerprint and require reassessment. Initial inspection may take seconds; subsequent checks reuse the verdict while still validating provenance. Receipts and responses remain in the existing private archive indexed by SQLite.

A valid mask alone is insufficient. A manual pivot saved in another session is not assumed for a new study. Usable means measurable, not scientifically certified or independently accurate. Reuse does not train Meta models or make an old Muse evaluation apply to a different analysis.

## Rejected evidence and validation

Incomplete, altered and measurement-insufficient results are excluded from automatic selection. Original videos, immutable responses, receipts, history and payment attempts are preserved. There is no bulk SQLite reset. Failure to check cache quality displays an error instead of silently billing a new request.

Implementation: SamCacheQuality.cs, PendulumSamCache.cs, SamVideoArchive.cs, SamAdaptiveLiveService.cs, scripts/sam-period-worker.ts and sam-video-replay.ts. Read endpoint: GET /api/science/director/pendulum/sessions/{id}/sam/{role}/reusable. Derived table: sam_cache_quality.

Local validation: 64 focused .NET tests and 71 Angular tests passed; production build passed with existing size warnings. Dedicated headed Edge recovered actual stored 50/80 cm evidence, with angle/angular velocity, vectors and trail; periods approximately 1.416/1.798 s. Original HEVC 1080×1920 played correctly; the headless profile could not decode HEVC and was not used to claim visual success.

Meta counter stayed 149→149 for both flows. No new provider inference or Muse assessment; none of the five authorized live tests consumed. No CI or owner-permission changes.
