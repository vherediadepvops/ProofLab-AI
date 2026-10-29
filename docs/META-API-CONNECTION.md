# ProofLab AI — Meta permissions and authorized tests

**Effective: 2026-10-29.** The owner authorized keeping Meta enabled for this project. This applies to `main`, feature branches and worktrees, and supersedes older instructions to keep the owner's main service disabled.

## Project configuration

`src/ProofLab.Api/appsettings.json` retains:

```json
{
  "Meta": {
    "UnlimitedManualRequests": true,
    "AllowPaidRequests": true,
    "CreditsConfirmedByOwner": true,
    "OwnerApprovedPaidRequests": true
  }
}
```

These are owner-declared permissions, not an automatic Meta balance check. They contain no credentials and initiate no calls on their own. Valid credentials and explicit admission of each operation remain required.

## Rule for every agent and branch

- Creating a feature, building, running offline tests, publishing or completing work does not revoke these permissions. Do not routinely set the three flags to `false` or add environment overrides disabling the owner's service.
- Before agent-initiated live tests, ask the owner **how many tests are authorized**, explain how many calls each test may include (Spark, SAM and batches), and give estimated cost when available. Do not ask again for each call within an existing authorization's scope.
- Track tests and calls used. When authorization is exhausted, stop the agent's tests and request an extension. Do not switch off the owner's API or reduce their manual allowance.
- The owner's manual clicks follow the UI cost/confirmation flow with `UnlimitedManualRequests=true`. The shared counter must not limit those manual submissions.
- Use test doubles or simulated transports for offline tests. If a test requires disabled permissions, confine that setting to isolated test configuration; do not overwrite `appsettings.json`, global environment variables or shared services.
- Project enablement does not change OpenAI policies or remove actual provider limits. Report the exact action and reason for a tool rejection; do not claim without evidence that it requires disabling Meta, and do not bypass it.

## Token and readiness

Register the token in **Settings**; it remains in that browser session's memory. Manual submissions can use it without a server environment key. Backend operations may alternatively use a process-scoped `MODEL_API_KEY`. Never store or publish secrets in Git, SQLite, logs or documentation.

Local inspection without inference:

```powershell
Invoke-RestMethod http://127.0.0.1:5391/api/meta/readiness
```

Inspect `creditsConfirmedByOwner`, `ownerApprovedPaidRequests`, `paidRequestsSwitchEnabled`, `manualTokenCallsPermitted` and `manualRequestsUnlimited`. `liveCallsPermitted=false` can mean a missing server key or exhausted non-manual allowance; alone it does not mean manual submissions using a token are disabled.

The environment variables `Meta__AllowPaidRequests`, `Meta__CreditsConfirmedByOwner` and `Meta__OwnerApprovedPaidRequests` can override JSON settings. Check precedence when a runtime differs from the repository. Do not restart other services or reset their counters to fix a test instance.

## Verification scope

Local readiness does not prove Meta authentication, available balance or scientific validity. This permissions update is verified without provider calls. Earlier inference records retain their scope and provenance.

[Agent rules](../AGENTS.md) · [Español](META-API-CONNECTION.es.md)
