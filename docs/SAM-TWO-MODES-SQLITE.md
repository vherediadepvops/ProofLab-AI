# ProofLab AI — Two SAM modes and physical-video SQLite (offline increment)

**Date:** Oct 28, 2026 · **Mission:** Autonomous Physics Discovery / pendulum · **Status:** isolated SQLite index implemented and tested; live Meta and UI-mode integration REMAINS BLOCKED by a security tool.

## Requested functional contract

| Mode | Desired behavior | Actual limitation |
|---|---|---|
| **API Meta** | Request a fresh SAM 3.1 inference even when the same video was processed earlier | Never relabel a cached result as fresh. Every authorized new run requires its own analysis ID, receipt and paid attempt marker. **Not implemented:** edits to force new billable inference were rejected by security controls. |
| **API Meta + cache** | Check SQLite for an exact source-video SHA-256, model/protocol version and prompt SHA; replay valid stored provider evidence on a hit, otherwise offer SAM inference after safety/budget checks | Physical SQLite index prepared but **NOT wired** into provider responses or the master player. Previous file-based response/receipt archive remains separate. No genuine SAM cache for the 50 cm pendulum exists. |

The UI may gather payment/media consent, but must not replace backend validation of credits, source hashes/PTS, costs and permitted execution. Pasting a key in a browser must never disable server or organizational safety gates.

## Implemented safe component

`src/ProofLab.Api/SamVideoEvidenceIndex.cs` is an **offline** Microsoft.Data.Sqlite 10.0.14 index located at `.private/physics/sam-video-evidence.sqlite`, separate from the existing Lean mathematical memory `.private/research/scientific-experience.sqlite`. It stores **metadata only**: original video SHA, protocol/model identity, prompt hash, response and receipt hashes, confined private relative archive path, analysis ID, completion timestamp, frame count, and provenance status. No tokens, source videos, masks, coordinates, plaintext prompts, or provider outputs enter SQLite.

Strict validation requires lowercase SHA/IDs, `sam-3.1`, pinned protocol `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2`, a non-traversable archive path and `PROVIDER_RECEIPT_PREVIOUSLY_VERIFIED`. Identical inserts are idempotent; scientific metadata conflicts are rejected. Lookup requires exact video/cache/prompt identity and protocol. **An SQLite row is NOT by itself proof that Meta produced the data.** Future backend integration must independently validate actual files' response/receipt hashes and Meta provenance before making them scientific observations. Manually pasted JSON must never be treated as a genuine provider result.

This component contains **zero Meta requests or paid-retry logic**. It does not alter the existing Lean database.

## Verification

`tests/ProofLab.Tests/SamVideoEvidenceIndexTests.cs` covers cache miss, persistence across reopening, exact query match, idempotency/conflicting metadata, path traversal, malformed/false provenance and invalid SHA. These are **synthetic database tests**, NOT real pendulum segmentations. Entire .NET suite **165/165 passed**. Isolated API Debug build under `.private/sqlite-index-qa` succeeded with 0 errors/warnings. The default Release build encountered an assembly held by the owner's existing port 5393 service; that owner service was not interrupted.

No edits to `Program.cs`, `PendulumSamCache.cs`, `sam-video-replay.ts` or billing flags. The prior tool security block against live inference/SQLite integration was not bypassed. Existing uncommitted Muse files and prior `attempt-started.json`/private media were preserved.

## Remaining security-gated work

1. Obtain an explicitly permitted way to connect the SQLite catalog to verified provider receipts and replace the UI port selector with only **API Meta / API Meta + cache**.
2. Recheck actual on-disk response/receipt SHA and model/timing provenance before cache replay. Never manufacture a cache hit.
3. Fresh API Meta calls require distinct analysis IDs and preserved attempt history; do not weaken paid-request gates or run Meta while provider/tool authorization remains blocked.
4. Verify CI, then independently review SAM bob masks and uncertainty. Scientific Stages 2 and 3 remain incomplete.

**Español:** [SAM-TWO-MODES-SQLITE.es.md](SAM-TWO-MODES-SQLITE.es.md).
