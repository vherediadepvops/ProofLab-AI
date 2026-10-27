# ProofLab AI — Secure Meta Model API preparation (Muse Spark 1.3 and SAM 3.1)

**Status:** code path prepared and validated WITHOUT live API calls; **Meta stays off** until owner-verified credit balance and explicit paid-call authorization. Date: 2026-10-27.

## 1. Obtain a Meta Model API key

1. Sign in personally at **https://dev.meta.ai/** with the Meta Model API account used for the hackathon.
2. Open **API keys** in the dashboard. Reuse an existing valid key, or select **Create API key** and name it `ProofLab-AI-Hackathon`.
3. **Copy the secret once**, storing it safely. Never share it in ChatGPT, screenshots, Angular, code, GitHub, logs or project memory.
4. One key authenticates **both** Muse Spark and SAM 3.1. The backend expects `MODEL_API_KEY`, passed in an `Authorization: Bearer` header.

Official references: https://dev.meta.ai/docs/authentication · https://dev.meta.ai/docs/quickstart · https://dev.meta.ai/help/api-keys/create-api-key .

## 2. Where to put the key

**Only authorized PC:** `DESKTOP-ESGSLJH` · project `C:\PROOFLAB-AI-HACKATHON` · API `http://127.0.0.1:5391`.

**Preferred temporary option (in your own PowerShell console):** use a hidden prompt to set `MODEL_API_KEY` only in the current shell and child processes. Do not put the secret in typed commands or PowerShell history.

```powershell
$secureKey = Read-Host 'Paste MODEL_API_KEY' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
try {
    $env:MODEL_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    Remove-Variable secureKey, pointer -ErrorAction SilentlyContinue
}
# Presence only; never print the secret:
[bool]$env:MODEL_API_KEY
```

The existing ProofLab API already listens on 5391. Its process must be **safely restarted from this same terminal** after verifying PID and executable to inherit the environment key. Setting a variable in another terminal does **not** update an already-running backend. Do not launch a competing instance.

**Persistent alternative, less private:** Windows → “Edit environment variables for your account” → “User variables” → “New”: name `MODEL_API_KEY`, value the secret. Restart the backend using a new session. Windows user environment variables are persisted in user profile settings and are not an encrypted secrets vault. Never store the key in system-wide variables or messages.

## 3. Verify credits and consent BEFORE billable calls

Open **https://dev.meta.ai/billing**, complete personal identity confirmation if requested, and check the **actual promotional balance** assigned to the correct team, expiration and terms for charges after credits run out. The rumored $150 hackathon credit is **not confirmed**. Inspect Usage/spend controls as available; emailed spending alerts are **not hard limits**.

Four required backend gates must all be true:
- `MODEL_API_KEY` present in the backend process.
- `Meta:CreditsConfirmedByOwner=true` only after the owner genuinely checks Billing.
- `Meta:OwnerApprovedPaidRequests=true` only after explicit owner authorization.
- `Meta:AllowPaidRequests=true` as the final paid-request switch.

All three flags default **false**. Later authorized activation can use `Meta__CreditsConfirmedByOwner`, `Meta__OwnerApprovedPaidRequests` and `Meta__AllowPaidRequests` environment variables; **do not set them until credit verification and paid-call approval**. The app cannot independently verify the balance: these are owner attestations, not a Billing API integration.

The process also imposes **two maximum live calls per startup**, shared by Muse and SAM. This is a request count limit, **not a guaranteed monetary cap**. It resets on restart; check Meta Usage for actual billed costs.

## 4. Check readiness without spending

```powershell
Invoke-RestMethod http://127.0.0.1:5391/api/meta/readiness
```

This endpoint reveals only booleans and remaining request allowance, never the key. The Goldbach UI at `http://127.0.0.1:4300/` also shows it. After all gates are satisfied, “Draft with Muse Spark (may spend Meta credits)” is enabled; each click requires confirmation.

## 5. Model responsibilities

- **Muse Spark 1.3:** `POST https://api.meta.ai/v1/chat/completions`. ProofLab asks for JSON-schema-constrained proposals for the five reports; it **does not autoapprove**. Stages 4–5 retain authentic server evidence IDs/hashes. Muse output is **UNVERIFIED** until humans review it.
- **SAM 3.1:** `POST https://api.meta.ai/v1/responses` for images/video. The current adapter returns raw segmentation output only; **validated mask decoding and tracking for Mission 2 are not yet implemented**. Do not send personal/sensitive media without permission.
- Both endpoints use the backend-only `MODEL_API_KEY`, never Angular.

Current sources: https://dev.meta.ai/docs/protocols/chat-completions · https://dev.meta.ai/docs/sam/overview · https://dev.meta.ai/docs/pricing-rate-limits .

## 6. What is and isn't verified

- **Offline validated:** compilation, four-switch gate unit tests, concurrency-safe two-call budget, read-only readiness and HTTP 409 when disabled. No `MODEL_API_KEY` in CI.
- **Not yet validated:** real token authentication, team credit balance, live Muse or SAM inference, actual charges or autonomous scientific discovery. Do not market the integration as working until checked in an explicitly authorized live experiment.
- **Scientific safeguards:** Muse prose never equals a Lean proof; Lean runs curated known benchmarks only until a sandbox is fully validated.

**Español:** [META-API-CONNECTION.es.md](META-API-CONNECTION.es.md).
