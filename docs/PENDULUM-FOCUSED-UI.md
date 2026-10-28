# ProofLab AI — Jury-focused pendulum screen: setup and three video views

**October 28, 2026 UTC · Mission 02 / Zone 1.** UI refinement based on the owner's screenshot of the overly narrow, vertically padded central card.

## Approved UX priority

The main jury screen shows **only the pendulum's physical setup and three synchronized views of one video**: reference footage, apparent movement vectors and ghost trail. The old large header, mission sidebar, theoretical chart gallery, 3+3 repeatability form and other scientific panels are **hidden from this primary path**. No code or private evidence was deleted. The nonlinear physics engine, original stored experimental data and one/two-trial workflow remain available.

## Implementation

- `ui/src/app/app.{html,ts}`: starts at `workspace='pendulum'`; while active, hides legacy mission navigation and tall hero, shows a slim ProofLab/Pendulum header with ES/EN language selection, and renders the new `<app-pendulum-zone-one>` component. The six-video advanced section is no longer rendered in this primary view but its source and backend remain intact.
- `ui/src/styles.scss`: only the focused Pendulum route removes the prior `max-width:1200px` constraint. The layout uses **all available browser width**, responsive spacing and mobile stacking. Hidden legacy pages retain their previous styles.
- `ui/src/app/pendulum-zone-one.{ts,html,scss}`: presentation-only component inheriting the already verified `PendulumDirector` logic and endpoints — **no duplicate research or mathematical backend**. A compact header, three inputs (string length **cm**, initial angle in sexagesimal degrees, bob mass **g**), optional second trial with local **NOT AI** suggested/manual length, then three visually meaningful placeholder panels until the first clip is uploaded.
- Creating a study freezes parameters until the user starts a new one. A valid MOV/MP4 upload mounts the existing `PendulumMasterPlayer`, which renders **one HTMLVideoElement plus two synchronized canvases**. The existing user-declared bob review is not automatically approved.
- In two-trial mode, the original validated acknowledgement and **local, explicitly non-Muse** follow-up prediction remain available, with second clip upload. **Trial 1 / Trial 2 tabs** show only one group of three views at a time, not six concurrent media players.
- The old theoretical charts, CSV and advanced workflows remain present in source (the original `pendulum-director.*`, `pendulum-study.*`, other missions and APIs). This is a jury UX focus decision, not removal.

## Scientific and playback caveats

Source iPhone HEVC/MOV bytes are preserved privately, but browser playback currently uses a **640px-wide 12FPS H.264 proxy**. Motion arrows/trails are **local pixel-space candidates, NOT SAM 3.1 bob masks or calibrated physical angular velocities**. No Muse/SAM model calls or new spending authorization occurred.

## Acceptance criteria

1. On desktop, the old sidebar and hero are hidden, and Pendulum's card spans the **full available viewport width**, no 1200px cap.
2. Main UI shows the physical setup and three video views only — no old `.model-chart`, `.technical-protocol` or technical graph gallery.
3. Before upload: three placeholders. After a valid video: **one video + two canvases**, preserving the earlier single-clock synchronization guarantee.
4. Optional second trial uses tabs to avoid six simultaneous views. ES/EN remain available and Meta billing stays blocked.
5. All prior scientific code/evidence is preserved.

## Upload UX regression fix: file picker available immediately

**Owner-reported bug:** after clicking **New study**, the three placeholder panels and physical setup were visible, but **Select video was missing**. Previously, the UI gated the file input behind an additional **Start study** action because `@if(session())` rendered upload only after creating the server session. This required two separate clicks and made the new-study state appear stuck.

**Corrected behavior:**
1. On initial entry or **New study**, **Upload your first recording / Select video** is immediately available even without an existing session.
2. User sets string length, initial angle and bob mass and chooses a real file. Client validates extension and 10 KB–240 MB size **before** creating any study.
3. Theory is recalculated from current user inputs, a new research record is persisted with a local `modelSha256` **before** video bytes are sent. Automatic sequence: mathematical-model request → `POST .../sessions` → `POST .../video/baseline`.
4. Failure to create a study leaves the file picker visible with a retryable error; video upload failure retains the already recorded study so the user can retry without overwriting original evidence.
5. Once uploaded, the same one-clock three-view player renders the video. **New study** removes only the currently selected UI session; it does not delete historical scientific records. The optional second video still follows the preregistered local follow-up prediction.

**Regression tests:** initial picker rendered, actual file handled in the required model/session/upload order, invalid extension rejected before registration, failed registration preserving retry control, reset restoring picker, one video and two canvases after upload. Zero Meta inference calls.

**Player documentation:** [PENDULUM-ZONE1-MASTER-PLAYER.md](PENDULUM-ZONE1-MASTER-PLAYER.md). **Español:** [PENDULUM-FOCUSED-UI.es.md](PENDULUM-FOCUSED-UI.es.md).
