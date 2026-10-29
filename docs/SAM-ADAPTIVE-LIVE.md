# SAM 3.1 — adaptive live processing, experimental branch

> **Current permissions (2026-10-29):** disabled-payment references below describe that instance's historical state or isolated rehearsal. They do not instruct agents to disable the owner's API or new branches. Follow the [current authorization and testing policy](META-API-CONNECTION.md).

**Branch:** `feature/sam-adaptive-live`, based on `main@ced3849`; not merged to main. The authorized execution host remains DESKTOP-ESGSLJH only.

## Scientifically conservative segmentation

Spark provides **one previously authenticated full-video localization**. The baseline dynamic algorithm proposes roughly 53 core frames per paid SAM request; the 50-frame figure is a **billing minimum**, not a guaranteed video-accuracy threshold.

The 667-frame test recording has only **three authentic Spark anchors at original frames 67, 333 and 599**. The nominal 13-way split would leave most fragments without a real seed. `SamAdaptiveGroundedPlan` therefore selects the largest partition count for which every fragment includes a genuine Spark seed, **four fragments** in this case. Prompt frame indices are translated to part-local indices without changing bounding-box coordinates; missing seeds cause fail-closed rejection **before spending**. No interpolation, hallucination or arbitrary fallback text prompt.

## Verified four real MP4 clips

Frame-exact H.265 HEVC fragments of **168, 171, 172 and 169 frames**, with 680 total submitted frames including overlaps; **nominal SAM cost USD0.1360**, excluding any new Spark spend (cached Spark reused). Preparation and verification of this real 667-frame video took **30.241 s**. All 680 decoded frame SHA-256 values matched the original; max measured timestamp drift **0.001 ms**. Resolution and orientation match in this source (1920×1080, 0°). Intermediate/segments and experimental evidence remain in ignored `.private` storage. Meta accepts MP4/MOV with H.264/H.265/MPEG-4: https://dev.meta.ai/docs/sam/overview.

## Billing, SSE and provenance

The experimental backend supports at most **three owner-approved batch trials**, **four simultaneous real SAM requests**, and an aggregate estimated **USD0.50 SAM ceiling per trial**. The original backend credit/token/consent budget gates remain required. Each part has durable private intended-attempt, reserve and potential dispatch markers, independent SSE progress, response SHA and receipt. Failures stop queuing new paid work; already dispatched parts may finish. There are **no automatic retries**.

A derived combined mask output is not misrepresented as a single native provider result. Overlap mask differences are marked **inconclusive** without synthesizing tracks; raster IoU and human review remain pending. No derived multi-part output is imported as existing single-call SQLite evidence.

**Security blocker:** a paid-capable POST attempt was rejected by the remote tool, even with payments off. No alternate route was used. The experimental server stays configured with `Meta:AllowPaidRequests=false`; **0/3 real trials consumed, 0 provider requests sent by this integration**.

## Local endpoints, checks, next steps

- Experimental web: `http://127.0.0.1:4327/`.
- Experimental API: `http://127.0.0.1:5410`.
- Read-only quote for the stored 667-frame demonstration: **4 Spark-grounded fragments**, 680 potentially billable frames, USD0.1360 estimated, trial allowance 3, paid switch OFF.
- Other chat services 5391 / 5393 / 4300 were preserved.
- **347/347 .NET tests, 64/64 targeted Angular tests**, Angular/API development builds passed. The broader Angular suite had one existing unrelated text-expectation failure. No CI was run or checked.

Next: resolve the remote tool authorization through its supported mechanism, obtain real provider response+receipt data, compare total A/B latency/cost/quality with one full-video run, and decide on a human-reviewed merge into main. No unverified speedup claims.
