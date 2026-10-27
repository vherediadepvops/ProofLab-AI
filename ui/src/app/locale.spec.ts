import { executionMode, friendlyApiError, leanExplanation, leanOutcome,
  mathematicalAssumptions, scientificStatus, strategyDescription } from './locale';

describe('Scientific evidence localization', () => {
  it('translates validated and rejected Lean outcomes without changing their codes', () => {
    expect(leanOutcome('VERIFIED','es')).toBe('VERIFICADO');
    expect(leanOutcome('VERIFIED','en')).toBe('VERIFIED');
    expect(leanOutcome('REJECTED','es')).toBe('RECHAZADO');
    expect(leanOutcome('REJECTED','en')).toBe('REJECTED');
    expect(leanOutcome('TIMED_OUT','es')).toBe('TIEMPO AGOTADO');
    expect(leanOutcome('UNAVAILABLE','en')).toBe('UNAVAILABLE');
  });

  it('explains the rfl diagnostic in the selected language, without claiming disproof', () => {
    const rejected = {
      outcome:'REJECTED',strategy:'rfl',
      isNewDiscovery:false
    };
    const spanish=leanExplanation(rejected,'es');
    const english=leanExplanation(rejected,'en');
    expect(spanish).toContain('Lean rechazó la táctica');
    expect(spanish).toContain('no significa que sea falso');
    expect(english).toContain('Lean rejected');
    expect(english).toContain('not make the theorem false');
  });

  it('explains a verified known theorem without claiming an original discovery', () => {
    const result = {
      outcome:'VERIFIED',strategy:'omega',isNewDiscovery:false
    };
    expect(leanExplanation(result,'es')).toContain('teorema ya conocido');
    expect(leanExplanation(result,'en')).toContain('previously known theorem');
  });

  it('explains simp failures consistently in both languages', () => {
    const result={outcome:'REJECTED',strategy:'simp',isNewDiscovery:false};
    expect(leanExplanation(result,'es')).toContain('simplificación');
    expect(leanExplanation(result,'en')).toContain('simplification');
  });

  it('localizes modes, strategies and theory assumptions', () => {
    expect(executionMode('DETERMINISTIC_BASELINE','es')).toContain('SIN IA');
    expect(scientificStatus('KNOWN_THEOREM','es')).toBe('TEOREMA CONOCIDO');
    expect(strategyDescription('omega','es')).toContain('razonamiento aritmético');
    expect(mathematicalAssumptions('Pendulum','es')).toContain('punto de suspensión');
  });

  it('does not display backend English gate errors verbatim in Spanish', () => {
    const error={error:{error:'Stage 4 requires a real recorded attempt ID in manifest'}};
    expect(friendlyApiError(error,'es','Falló','Failed')).toContain('Adjunta al informe');
    expect(friendlyApiError(error,'en','Falló','Failed')).toContain('Attach a Lean attempt ID');
  });
});
