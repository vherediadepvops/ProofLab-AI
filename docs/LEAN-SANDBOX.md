# Mission 1 — Isolation policy for untrusted Lean 4

**2026-10-27 · DESIGN ONLY, NOT ENABLED.** Existing Lean verification executes three known theorems with fixed `rfl`, `omega`, `simp` tactics; it never executes arbitrary AI-generated Lean on Windows. The Docker daemon was **unavailable** when `docker info` was checked on the approved host.

## Threat model

Lean supports metaprogramming and IO, so generated source must be treated as untrusted, just like retrieved papers, external repositories and prompts. Threats include reading host secrets, network exfiltration, runaway CPU/RAM/disk use, persistence, manipulating proof targets and false successes with `sorry`, `admit` or added `axiom`.

## Proposed sandbox execution contract (not validated yet)

1. Use only a prebuilt, audited Linux Lean/mathlib image **pinned to its sha256 digest** (never `latest`), with verified Lean 4.35.0 compatibility and license. Fetch/build dependencies **before** processing untrusted code.
2. Each proof gets a disposable container with a **read-only** input mount. Never mount project directories, the Windows home directory, `.git`, credentials or Docker's socket. Do not pass `MODEL_API_KEY` or any other secret.
3. Enforce `--network none`, `--read-only`, `--cap-drop ALL`, `--security-opt no-new-privileges`, default seccomp, `--pids-limit 64`, `--memory 512m`, `--memory-swap 512m`, `--cpus 1`, unprivileged user, `--tmpfs /tmp:rw,nosuid,noexec,size=64m` and no devices/privileged mode. Check actual Linux engine support for each restriction.
4. Limit total **container runtime**, stdout/stderr size, input size, attempt count and spend. Always stop/remove timed-out containers, preserving sanitized evidence externally; never accept shell arguments from the model.
5. Recheck that the verified statement exactly matches a **human-reviewed formal target**, that Lean accepted a real proof free of holes or new axioms, and that imports/toolchain/flags are pinned. Archive image/Lean versions, goal, hashes and diagnostic. Successful compilation with `sorry` is not a theorem proof.
6. **Fail closed:** until the image, daemon, isolation and target review are verified, free-form Lean source remains DISABLED; never fall back to native Windows execution. Stronger adversarial cases may warrant disposable VMs after authorization.

## Acceptance tests not yet run

- Confirm running Docker Linux daemon, seccomp and pinned image.
- Test a valid and an invalid Lean proof with no network.
- Safely probe denial of host-secret reads, network requests, writes outside `/tmp`, process/memory exhaustion and infinite loops.
- Verify timeouts, bounded logs, reproducibility and no orphaned containers.
- Confirm an unsuccessful tactic is never reported as a refuted conjecture.

**Current state:** none of these sandbox acceptance tests has completed. No AI-generated free-form Lean execution is permitted on Windows. References: [Docker resource constraints](https://docs.docker.com/engine/containers/resource_constraints/), [container run security options](https://docs.docker.com/reference/cli/docker/container/run), [seccomp](https://docs.docker.com/engine/security/seccomp).

**Español:** [LEAN-SANDBOX.es.md](LEAN-SANDBOX.es.md).
