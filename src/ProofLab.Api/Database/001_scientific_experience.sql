-- ProofLab AI | Scientific Experience Memory | SQLite schema v1
-- ES: Estructura versionada, SIN datos reales ni credenciales.
-- EN: Versioned schema only; NO real experiences or credentials.
-- Original Lean JSONL evidence remains the authoritative source.
-- Applying this script repeatedly must not delete previously stored attempts.
CREATE TABLE IF NOT EXISTS lean_experience (
    attempt_id TEXT PRIMARY KEY,
    research_id TEXT NOT NULL,
    problem_id TEXT NOT NULL,
    goal_sha256 TEXT NOT NULL,
    lean_version TEXT NOT NULL,
    tactic TEXT NOT NULL CHECK(tactic IN ('rfl','omega','simp')),
    outcome TEXT NOT NULL,
    source_sha256 TEXT NOT NULL,
    checked_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_lean_memory_goal
    ON lean_experience(problem_id,goal_sha256,lean_version);
