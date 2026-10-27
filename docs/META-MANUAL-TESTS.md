# ProofLab AI — User-operated Muse Spark 1.3 and SAM 3.1 tests

**State: UI added; no additional real model tests from the UI. Date: 2026-10-27.**

## Verified background

The owner visually confirmed Meta Billing displayed **US$150 free credits in use**, which is **not an exact remaining balance or spending cap**. `MODEL_API_KEY` was set in Windows. An initial remote operation was blocked by platform safety. An authorized second attempt reached a temporary local API, where `POST /api/math/plan` returned **HTTP 502**, with **one request reserved**. Without upstream diagnostics, it is unknown whether the cause was a Meta HTTP 400/401/403/429 response, transport, or response parsing. The temporary process exited; the main API remains payment-disabled.

## Starting a temporary test session yourself

1. Work exclusively on `DESKTOP-ESGSLJH` in `C:\PROOFLAB-AI-HACKATHON`.
2. Review [Meta Billing](https://dev.meta.ai/billing) for current credit availability, potential charges to the linked payment method and possible use of free-credit content for product improvement. Credits shown do not impose a guaranteed hard cap.
3. Open `C:\PROOFLAB-AI-HACKATHON\scripts\Start-MetaTest.cmd` from Windows Explorer. The script contains **no secret** and makes **no provider calls on startup**. It requires `MODEL_API_KEY` already inherited by that new Windows terminal; sign out and back into Windows if necessary. Never type the key into chat or public scripts.
4. Read the possible-charge warning and personally type **`ACTIVAR`**. Only a **temporary** server is started at `http://127.0.0.1:5393` with a maximum of **two Meta model calls per process**, shared across Muse and SAM. This is **not a monetary hard spending limit**. The main API at `5391` stays closed to payment calls. Do not restart repeatedly to avoid the call budget.
5. Open `http://127.0.0.1:4300/`, refresh (`Ctrl+F5`), select **Meta API Tests** in the sidebar and click **Refresh test connection** to see the four authorization gates and remaining calls. Do not enter any API key in this page.

## Run only user-approved individual tests

**Muse Spark 1.3:** enter a short public question (e.g. "What is a prime number? Explain in one sentence"), click **Send ONE Muse request**, then confirm potential credit consumption. A successful call shows **UNVERIFIED** model prose. A failure shows our API HTTP status and, if returned, the upstream Meta HTTP status and a safe category (never upstream raw error body or bearer key). No automatic retries.

**SAM 3.1:** click **Create synthetic test ball image** to generate a local PNG containing a red ball against a simple background. Use the prompt `red ball`, click **Send ONE image to SAM**, and confirm the potential charge. Alternatively choose a non-personal PNG/JPEG/WebP smaller than 1.5 MB. Successful responses show a **short RAW JSON response preview**; mask decoding, visual overlays and video tracking are **not implemented**. Synthetic imagery is a connectivity test, not scientific measurement.

When finished, close the temporary console or press **Ctrl+C**; check Meta Usage/Billing for charges, recognizing that reporting may be delayed. HTTP 502 **does not** guarantee zero charges.

## Security constraints

- These tests do **not** alter real research dossiers, SQLite memory or formal Lean proofs; they do not solve Goldbach.
- `MODEL_API_KEY` stays in the backend environment, never Angular or Git.
- The console sets three process-scoped flags only after direct owner acceptance: `Meta__CreditsConfirmedByOwner=true`, `Meta__OwnerApprovedPaidRequests=true`, `Meta__AllowPaidRequests=true`. No edits to `appsettings.json`, system variables, Git, or the main API.
- Each model request needs a separate explicit UI confirmation, shares the server-side two-call limit, and never retries itself.
- Do not bypass any platform security refusal. Troubleshoot credentials, provider permissions and upstream status through permitted methods rather than repeatedly spending credits.

**Español:** [META-MANUAL-TESTS.es.md](META-MANUAL-TESTS.es.md).
