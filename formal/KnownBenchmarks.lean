import Lean
set_option autoImplicit false

-- Reference examples only: these are well-known theorems, not discoveries.
theorem prooflab_add_zero (n : Nat) : n + 0 = n := by
  rfl

theorem prooflab_add_comm (a b : Nat) : a + b = b + a := by
  omega

theorem prooflab_add_bound (a b : Nat) : a ≤ a + b := by
  omega
