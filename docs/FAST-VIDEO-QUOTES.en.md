# Fast video quotes and manual processing

[Español](FAST-VIDEO-QUOTES.es.md)

## User flow

Registering a key in Settings retains it only in the session password field. It is never saved to localStorage, sessionStorage, SQLite, files or logs. Clear, changing servers or reloading removes it; changing analysis modes does not.

When the original video and quote are ready, Process is enabled within the existing permission and allowance. The click authorizes only the transfer and estimated budget disclosed beside it. Uploading a video, registering a key and checking prices never trigger inference.

- Green / US$0: verified SAM cache hit.
- Red / uses credits: API Meta mode or cache miss.
- Missing assembly: itemized recognition budget (up to US$0.05) plus SAM estimate. Process recognizes three images, then pauses for human review. A second Process click starts SAM.
- Existing recognition opens its review without another charge. Unreconciled attempts cannot be repeated.
- A new workflow needs two requests; the UI warns when fewer remain. Registering a key does not expand that allowance.

## Pricing and concurrent preparation

SAM status, quote, readiness and assembly status are independent parallel GETs. The first quote uses container nb_frames when available instead of decoding the entire video. Missing counts fall back to original frame timestamps.

VideoTimingCatalog persists counts and timestamps in private storage, tied to source SHA, file size, modification time and version. Concurrent callers share the same work, with at most two native timestamp readers. Metadata probes have an independent lane so they do not queue behind those readers. Cancelling one waiter does not cancel work awaited by others.

For videos within the current request limit, timestamps are prepared in the background after quoting. Submission verifies the complete source SHA and actual frame count, and rejects a cost increase above the displayed approval before reserving credits. Remux timestamps still have to match the original. Resolution and frame rate are preserved.

## Local verification, 2026-10-28

For a 31.28-second HEVC recording with 1,875 frames:
- Previous repeated quotes: 10,390 and 10,579 ms.
- Test API: first quote 204 ms, repeated quote 5 ms; SAM estimate US$0.375.
- After preparation, quotes reused all 1,875 verified native timestamps.
- Existing demo SAM cache hit preserved: 169 ms for status.
- Local tests cover concurrency, persistence, cancellation, source changes, authorization limits, stale responses, human review and double clicks. Synthetic keys/transports only; no paid calls or CI.

Measurements do not guarantee latency for every format or validate 4K/120/240 FPS. Existing upload and per-request limits remain. Declared frame count is an estimate, not an invoice; full SHA and timestamps are verified before submission.
