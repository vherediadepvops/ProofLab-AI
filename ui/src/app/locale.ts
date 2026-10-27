/**
 * Presentation-only localization of validated scientific data.
 * Canonical tool outcomes, attempt IDs, hashes and raw diagnostics are never modified.
 * The model/Lean evidence remains language-independent and traceable.
 */
export type Language = 'es' | 'en';

export interface LeanFeedback {
  outcome: string;
  strategy: string;
  diagnostic: string;
  isNewDiscovery: boolean;
}

const OUTCOMES: Record<string, { es: string; en: string }> = {
  VERIFIED: { es: 'VERIFICADO', en: 'VERIFIED' },
  REJECTED: { es: 'RECHAZADO', en: 'REJECTED' },
  TIMED_OUT: { es: 'TIEMPO AGOTADO', en: 'TIMED OUT' },
  UNAVAILABLE: { es: 'NO DISPONIBLE', en: 'UNAVAILABLE' },
};

export function leanOutcome(outcome: string, language: Language): string {
  return OUTCOMES[outcome]?.[language] ??
    (language === 'es' ? 'SIN CLASIFICAR' : 'UNCLASSIFIED');
}

export function leanExplanation(
  verification: Pick<LeanFeedback, 'outcome' | 'strategy' | 'isNewDiscovery'>,
  language: Language
): string {
  const { outcome, strategy, isNewDiscovery } = verification;
  if (outcome === 'VERIFIED') {
    if (isNewDiscovery) {
      return language === 'es'
        ? 'Lean aceptó la demostración formal. La originalidad y la equivalencia del enunciado aún requieren una revisión independiente.'
        : 'Lean accepted the formal proof. Novelty and statement equivalence still require independent review.';
    }
    return language === 'es'
      ? 'Lean 4 verificó correctamente la demostración de un teorema ya conocido. Esta prueba no constituye un descubrimiento científico nuevo.'
      : 'Lean 4 successfully verified a proof of a previously known theorem. This is not a new scientific discovery.';
  }
  if (outcome === 'REJECTED') {
    if (strategy === 'rfl') {
      return language === 'es'
        ? 'Lean rechazó la táctica «rfl»: los dos lados de la igualdad no son idénticos por definición. La táctica no demuestra este teorema; eso no significa que sea falso.'
        : 'Lean rejected the «rfl» tactic: the two sides are not equal by definition. This tactic does not prove the theorem; that does not make the theorem false.';
    }
    if (strategy === 'simp') {
      return language === 'es'
        ? 'Lean rechazó la táctica «simp»: las reglas de simplificación disponibles no lograron demostrar el objetivo. Esto no refuta el teorema.'
        : 'Lean rejected the «simp» tactic: available simplification rules did not prove the goal. This does not refute the theorem.';
    }
    return language === 'es'
      ? 'Lean rechazó el intento con «' + strategy + '». La demostración no fue validada, pero la afirmación matemática no queda refutada.'
      : 'Lean rejected the «' + strategy + '» attempt. The proof was not validated, but the mathematical statement is not disproved.';
  }
  if (outcome === 'TIMED_OUT') {
    return language === 'es'
      ? 'Lean superó el límite de tiempo. No hay una demostración verificada ni una refutación.'
      : 'Lean exceeded the time limit. No proof or disproof was established.';
  }
  if (outcome === 'UNAVAILABLE') {
    return language === 'es'
      ? 'El verificador Lean no está disponible. Este intento no ha sido comprobado.'
      : 'The Lean verifier is unavailable. This attempt has not been checked.';
  }
  return language === 'es'
    ? 'El intento no tiene un resultado verificable.'
    : 'This attempt has no verifiable result.';
}

export function scientificStatus(status: string, language: Language): string {
  if (status === 'KNOWN_THEOREM') return language === 'es' ? 'TEOREMA CONOCIDO' : 'KNOWN THEOREM';
  return status;
}

export function executionMode(mode: string, language: Language): string {
  if (mode === 'MANUAL_CONTROL') return language === 'es' ? 'CONTROL MANUAL' : 'MANUAL CONTROL';
  if (mode === 'DETERMINISTIC_BASELINE' || mode === 'DETERMINISTIC_BASELINE_NOT_AI')
    return language === 'es' ? 'CICLO DETERMINISTA (SIN IA)' : 'DETERMINISTIC BASELINE (NO AI)';
  return mode;
}

export function strategyDescription(strategy: string, language: Language): string {
  if (strategy === 'rfl') return language === 'es'
    ? 'rfl · igualdad por definición' : 'rfl · definitional equality';
  if (strategy === 'omega') return language === 'es'
    ? 'omega · razonamiento aritmético' : 'omega · arithmetic reasoning';
  if (strategy === 'simp') return language === 'es'
    ? 'simp · simplificación' : 'simp · simplification';
  return strategy;
}

export function mathematicalAssumptions(kind: string, language: Language): string {
  if (kind === 'Pendulum') return language === 'es'
    ? 'Aproximación de ángulo pequeño, punto de suspensión fijo y longitud medida hasta el centro de masa.'
    : 'Small-angle approximation, fixed pivot, measured pivot-to-centre distance.';
  return language === 'es'
    ? 'Esfera sólida homogénea que rueda sin deslizarse sobre una rampa fija.'
    : 'Uniform solid sphere rolling without slipping on a fixed incline.';
}

/** Translate known backend validation errors without pretending they are Lean results. */
export function friendlyApiError(error: unknown, language: Language, fallbackEs: string,
  fallbackEn: string): string {
  const message = String((error as {error?: {error?: string}})?.error?.error ?? '');
  const is = (...parts: string[]) => parts.some(p => message.includes(p));
  if (is('requires an actual server-generated Lean attempt')) {
    return language === 'es'
      ? 'Esta etapa requiere al menos una prueba real ejecutada por Lean 4.'
      : 'This stage requires at least one actual Lean 4 verification attempt.';
  }
  if (is('requires a real recorded attempt ID')) {
    return language === 'es'
      ? 'Adjunta al informe el identificador de una prueba Lean registrada en esta investigación.'
      : 'Attach a Lean attempt ID recorded for this research project.';
  }
  if (is('Incomplete stage:')) {
    return language === 'es'
      ? 'El informe está incompleto. Revisa los campos obligatorios antes de continuar.'
      : 'This report is incomplete. Complete all required fields before continuing.';
  }
  if (is('This stage is locked', 'Approve the first three research reports')) {
    return language === 'es'
      ? 'Esta etapa sigue bloqueada. Completa y revisa primero los informes anteriores.'
      : 'This stage is locked. Complete and review earlier reports first.';
  }
  if (is('Human completeness review')) {
    return language === 'es'
      ? 'Debes confirmar que revisaste la documentación antes de avanzar.'
      : 'You must acknowledge reviewing the documentation before advancing.';
  }
  if (is('Unknown mathematical problem', 'Unknown research identifier')) {
    return language === 'es'
      ? 'El problema matemático o expediente indicado no existe.'
      : 'The selected mathematical problem or research record does not exist.';
  }
  if (is('Research project not found')) {
    return language === 'es'
      ? 'No se encontró esta investigación en el almacenamiento local.'
      : 'This research project was not found in local storage.';
  }
  return language === 'es' ? fallbackEs : fallbackEn;
}
