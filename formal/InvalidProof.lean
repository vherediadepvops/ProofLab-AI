import Lean
set_option autoImplicit false

-- Negative test: this tactic must NOT prove the theorem.
theorem prooflab_add_comm_wrong (a b : Nat) : a + b = b + a := by
  rfl
