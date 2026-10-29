# Testing ProofLab in Microsoft Edge
Updated: 2026-10-29. [Español](EDGE-TESTING.es.md).

## Toolchain
The existing workflow uses Remote Desktop Commander to verify the authorized device and run PowerShell/Node, Microsoft Edge's DevTools Protocol (CDP) to inspect/control a dedicated QA tab, and Node's built-in fetch/WebSocket. Observed versions: Node 24.21.0, Edge 155.0.4283.62, protocol 1.3. No Selenium, Playwright, msedgedriver or extension is needed. [Microsoft protocol reference](https://learn.microsoft.com/en-us/microsoft-edge/devtools/protocol/).

Operate only on the device/workspace authorized in shared memory. Current workspace: C:\PROOFLAB-AI-HACKATHON. UI 4300, API 5391, dedicated Edge CDP 9237. Preserve the separate owner API 5393 and personal browser sessions. The dedicated profile is .private\muse-browser-profile.

## Prepare/reuse the QA instance
Read shared AGENTS/context; inspect current branch/changes before edits. Check first:

~~~powershell
Set-Location 'C:\PROOFLAB-AI-HACKATHON'
Get-NetTCPConnection -LocalPort 9237 -State Listen -ErrorAction SilentlyContinue
Invoke-RestMethod 'http://127.0.0.1:9237/json/version'
Invoke-RestMethod 'http://127.0.0.1:9237/json/list' |
  Select-Object id,type,url
~~~

Verify the listener belongs to the dedicated Edge profile and is loopback. Reuse the authorized instance, not a personal browser. One automation client per QA tab. If no instance exists:

~~~powershell
$qaRoot='C:\PROOFLAB-AI-HACKATHON'
$qaProfile=Join-Path $qaRoot '.private\muse-browser-profile'
$qaEdge='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if(Get-NetTCPConnection -LocalPort 9237 -State Listen -ErrorAction SilentlyContinue) {
  throw '9237 is occupied: identify and reuse the authorized instance.'
}
if(!(Test-Path -LiteralPath $qaEdge)) { throw 'Locate the Edge installation.' }
Start-Process -FilePath $qaEdge -WindowStyle Hidden -ArgumentList @(
  '--remote-debugging-address=127.0.0.1',
  '--remote-debugging-port=9237',
  ('--user-data-dir="'+$qaProfile+'"'),
  '--no-first-run','--no-default-browser-check','--disable-extensions',
  'http://127.0.0.1:4300'
)
~~~

Recheck the listener and /json/version. Do not expose the port to the network, terminate all Edge processes, copy personal profiles, change execution policy, or restart APIs merely to inspect the UI. Inline PowerShell and node work without running a .ps1 file.

## Reproducible current-page inspection
Run the checked-in [inspection script](../scripts/edge-inspect.mjs):

~~~powershell
Set-Location 'C:\PROOFLAB-AI-HACKATHON'
node scripts/edge-inspect.mjs
~~~

It selects a page with the exact UI origin, reads DOM/video state, captures a PNG and stores a timestamped JSON under .private/edge-inspection/. It does not navigate, upload media, press Process or enter a key. It blocks mutations/external requests from that tab while attached, permits local GET/HEAD/OPTIONS, and returns exit 1 for unexpected blocked requests/errors or a changed Meta counter.

This is temporary tab interception, not a system firewall; it does not cover other tabs/processes and ends at disconnect. Avoid concurrent human use of the QA tab. Verified 2026-10-29 on Home at 1763×795: no overflow/errors, requests 37→37. Zero video views is expected on Home. This check does not validate playback, physics or a new inference.

## Existing functional campaigns
Private local files, absent from a fresh Git clone:
- .private/suspension-review-browser.mjs: latest three-study cached playback campaign.
- .private/suspension-review-browser.json and corresponding PNGs: evidence.
- .private/pendulum-values-browser.mjs: older campaign; historical expectations may no longer apply.

Read the script and verify .private/pendulum-new-videos-live.json plus referenced studies/media before running:

~~~powershell
Set-Location 'C:\PROOFLAB-AI-HACKATHON'
node .private/suspension-review-browser.mjs
~~~

Require three actual results, assertions, expected analysis identities, empty unexpected blocked/errors and unchanged request count. A successful exit with skipped fixtures is not a pass. A cache miss or newly paid review must stop a cache-only test. Prepare a separate private script with explicit expectations for other videos; never alter scientific data to satisfy assertions.

## Building another browser test
Read /json/list, attach to the selected webSocketDebuggerUrl and send {id,method,params}; correlate responses with timed promises and handle events/errors/connection close.

| Purpose | Method or condition |
|---|---|
| Inspect DOM/click existing controls | Runtime.evaluate; returnByValue/awaitPromise |
| Navigate to local UI | Page.navigate; wait for language/navigation buttons |
| Select a local study fixture | Fetch.fulfillRequest for /demo-video.json, without modifying the file |
| Desktop/mobile layout | Emulation.setDeviceMetricsOverride at 1440×1000 and 390×844 |
| Screenshot | Page.captureScreenshot to private PNG |
| JavaScript errors | Runtime.exceptionThrown and evaluate.exceptionDetails |
| Network guard | Fetch.enable/requestPaused; resolve using continueRequest, failRequest or fulfillRequest |
| Cleanup | Resolve pending requests, disable Fetch, restore changed viewport/focus and disconnect only the client |

Use DOM/media readiness conditions instead of fixed sleeps. For cached playback, require API Meta + cache, zero price and enabled Process. After load, seek to 3/4/5 s, wait for !video.seeking and the presented-frame update, then read θ/ω. Values should change when valid; ambiguous geometry must retain — and an explanation.

Development Angular exposes window.ng.getComponent(...); previous tests compared the player's frameTime() to video.currentTime within 0.04 s. This is a campaign-specific synchronization assertion, not a universal scientific tolerance or production API. Adapt timing checks to build/FPS; prefer video events and observable DOM in production. Verify four views, aligned overlays, play/pause/seek, ES/EN, screenshots and overflow. Mobile emulation is not a physical phone test. [Fetch protocol](https://chromedevtools.github.io/devtools-protocol/tot/Fetch/).

## Network policy and real Meta tests
Default UI/cache tests use no token. Enable interception before navigation/clicks. Allow necessary local reads; only if needed for that flow, allow POST /api/science/pendulum/mathematical-model (local deterministic calculation). Block other mutations/external destinations, including local endpoints that could call Meta.

For error fixtures, fulfill a 502 without forwarding and mark it simulated/forwarded:false. Such a test does not validate Meta. Real inference is a separate authorized workflow with budget, call limit and receipts; never remove the UI guard to make a failing test pass. Do not print tokens, Authorization headers or credential-bearing request bodies.

Read /api/meta/readiness usedRequests before/after. Counter equality supplements interception logs; simultaneous owner requests may change it. Never reset the counter or restart the API to obtain a pass. CDP guards only the attached flow, not the whole server.

## Troubleshooting and handoff
Missing 9237/tab: verify profile/URL. Stalled loader: inspect errors/requests, not repeated Process clicks. HEVC/MOV failure: inspect video.error/browser support, do not silently replace original media with a reduced proxy. A blocked Adblock extension is unrelated; preserve browser policy and use a separate extension-free QA profile. Stale selectors/fixtures require targeted updates, not routine full-suite runs.

A screenshot cannot certify physical accuracy or new provider inference. Handoff must include commit/build, authorized device, profile/port, script/command, private study/analysis IDs, assertions, screenshots/report paths, counters and limitations. Keep profiles/videos/evidence/IDs out of public Git; publish generic tools/docs only. No CI is required for local browser checks.

This guide is discoverable from both README files and shared AGENTS/PROJECT-CONTEXT. Consult it only for browser work. The existing Remote Desktop Commander access is sufficient; no additional browser plugin is required for this workflow.
