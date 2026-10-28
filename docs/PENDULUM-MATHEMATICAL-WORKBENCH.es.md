# ProofLab AI — Laboratorio matemático interactivo del péndulo (27/10/2026)

**Misión:** Physics Discovery (M2). **Estado:** modelo ideal determinista funcionando y vinculado a la demo guiada; seguimiento SAM 3.1 y decisiones Muse Spark **NO** implementadas aún en video real. Sin llamadas pagadas, no descubrimiento científico original.

## Objetivo aprobado

El usuario configura longitud desde pivote hasta centro de masa (0,10–5 m), **ángulo inicial** (1–85°), **masa de plomada** (10–5000 g) y elige **una o dos pruebas** mediante una casilla. La segunda longitud puede resultar de una **regla de control local explícitamente NO IA** (50→80 cm para ejemplo), o fijarla libremente el usuario (ej. 90 cm). Una opción de Muse solo se habilitará cuando exista inferencia real autorizada y validada; por ahora no se atribuye ninguna propuesta a Muse. A continuación el motor genera datos, valores y **tres gráficos teóricos interactivos ANTES de subir un video**.

## Física empleada

El modelo es un péndulo ideal planar, plomada puntual, hilo sin masa e inextensible, pivote fijo, sin rozamiento, liberado desde reposo. La ecuación resuelta:

```text
theta¨ + (g/L) sin(theta) = 0
g = 9.80665 m/s²
theta(0) = theta0; theta'(0) = 0
x(t) = L sin(theta(t))
h(t) = L (1-cos(theta(t)))    [elevación sobre punto más bajo]
Ek(t) = 1/2 m (L theta'(t))²
Ep(t) = m g h(t); Etotal = Ek + Ep
```

**Período de ángulo pequeño:** `Tsmall = 2*pi*sqrt(L/g)`.
**Período exacto del ideal no lineal:** `T = Tsmall / AGM(1, cos(theta0/2))`, donde AGM es la media aritmético-geométrica, con ángulos en radianes.

**Trayectorias** resueltas numéricamente con Runge–Kutta de 4.º orden, paso interno `0,01 s`, muestras publicadas cada `0,04 s`, ventana de `20 s`, **501 muestras por prueba**. Cada respuesta incluye máxima deriva relativa de energía del integrador como control numérico. Para graficar se genera SVG desde los datos reales del solucionador, sin gráficos ilustrativos pregrabados. La masa modifica las energías y no el período ideal. Cambiar ángulo modifica amplitud y período no lineal. En comparación de longitudes se supone el **mismo ángulo inicial**.

## Tres gráficos teóricos reproducibles

1. **Movimiento horizontal `x(t)` vs tiempo**, cm y s: línea azul primera longitud; naranja segunda longitud si se marca la casilla.
2. **Plano XY**, cm: arco espacial `x=L sin(theta)`, `h=L(1-cos(theta))`. La segunda longitud aparece superpuesta, con ejes compartidos.
3. **Energía cinética, potencial y total** de la primera condición, en julios vs tiempo: total constante en el ideal. Cambiar masa provoca aumento proporcional de energías, **no** del período.

La interfaz permite exportar `prooflab-modelo-teorico.csv` con ambas condiciones, muestras, tiempos y energía. La API local determinista es `POST /api/science/pendulum/mathematical-model`, body `{"lengthMeters":0.5,"angleDegrees":15,"massGrams":300,"followUpLengthMeters":0.8}`. `followUpLengthMeters` es opcional.

**Ejemplo local verificado:** 50 cm, 15°, 300 g: `Tno-lineal ≈ 1,424847 s` frente a la aproximación `Tsmall ≈ 1,4185 s`; segunda de 80 cm, 15°: `Tno-lineal ≈ 1,802305 s`, cambio teórico ~26,4911 %. Son **predicciones**, no resultados extraídos de un video. El cálculo de la fórmula del período finito no debe confundirse con una conjetura matemática nueva.

## Flujo de observaciones opcional

- El usuario inicia un expediente local. Se guardan **parámetros teóricos ANTES del video** con UTC, código fuente del algoritmo y `modelSha256` de 64 caracteres, verificado al consultar el expediente. Es una **huella local** y no un sello de tiempo externo.
- **Una prueba:** carga un MOV/MP4 de 50 cm, el motor de seguimiento local experimental obtiene coordenadas, la persona puede reproducir el proxy H.264 y declara si observó realmente la plomada. Un botón compara período no lineal teórico con período candidato del video y calcula `100*abs(Tvid-Tteoría)/Tteoría`. **No requiere segunda grabación.**
- **Dos pruebas:** tras la primera observación y revisión humana declarada, se fija la predicción del segundo ensayo con el **mismo modelo no lineal** (ángulo y masa de la configuración). La longitud recomendada es **local, NO Muse Spark**, o elegida manualmente. Solo entonces se admite un segundo video y se compara el cambio de períodos previsto/observado. Para demo prospectiva, filmar ese segundo video después de la predicción; archivos antiguos dan solo un ensayo retrospectivo.
- Tras subir un video se muestran **otros tres gráficos derivados de la señal real de coordenadas locales**: posición horizontal `x(t)` en **píxeles**, velocidad horizontal aproximada en **px/s**, y espacio de fase **posición pixel vs velocidad pixel/s**. No se puede afirmar que sean el **plano XY espacial real**: el rastreador actual solo mide posición horizontal y no aporta calibración espacial. Los gráficos recortan visualmente percentiles 2–98 por robustez de escala, sin modificar la evidencia guardada; saltos y sombras pueden contaminar resultados. Es **FFmpeg + sustracción de fondo local**, **NO SAM 3.1**, **NO Muse**.

## Calidad, privacidad y límites

- La predicción **no utiliza coordenadas del video**, y las gráficas experimentales **no son generadas por fórmulas teóricas**. La IA no puede forzar coincidencia entre ambas señales.
- La diferencia relativa de períodos y la diferencia en puntos porcentuales entre cambios son **discrepancias descriptivas**, no un intervalo de confianza ni un margen completo de incertidumbre.
- No hay reconocimiento seguro de identidad de la plomada, calibración física XY, estimación de rozamiento, tiempo original PTS contrastado, error sistemático o revisión independiente. Una masa real, péndulo físico y soporte no corresponden perfectamente a este modelo.
- Registros originales, archivos privados y revisiones previas 3+3 **no** se sobrescriben. El «Modo técnico avanzado» anterior permanece accesible.
- Servidor solo en localhost; **no publicar carga de video anónima** sin autenticación, cuotas/retención y privacidad. Meta pagado permanece bloqueado.
- **Pendiente:** SAM real sobre fotogramas para XY calibrado y máscara por tiempo; Muse Spark justificando autónomamente la nueva prueba; intervalos de confianza/cronometraje; enlace al Scientific Method Engine de cinco informes ES/EN.

## Comparación teoría–video adicional (sin IA)

El nuevo panel añade una **superposición visual** de la señal del primer video (y segundo, si se eligió) con el modelo no lineal, ambos **normalizados por separado a amplitud relativa**. Para hacer las curvas legibles se recortan temporalmente los primeros 2 segundos y los extremos 8–92 %, y se ajustan **solo fase de presentación y sentido de eje de cámara**. La frecuencia/período de la teoría no se modifica, y el estimador espectral de período medido no utiliza datos de la teoría. Por lo tanto una buena coincidencia visual no implica ajuste estadístico validado; **no debe considerarse una medida independiente de precisión**. No existe conversión certificada píxeles→centímetros.

El análisis de **dos videos** devuelve también una tabla de errores relativos de período **individuales**: `abs(Tvideo-Tteoría)/Tteoría * 100` para primera y segunda longitud, además de la diferencia en puntos porcentuales entre el incremento previsto y observado. Estos números describen discrepancia matemática, **NO intervalos de confianza ni incertidumbre instrumental/sistemática**. Campos nuevos en `DirectorComparison` son opcionales para mantener compatibilidad con sesiones históricas.

**Control de seguridad:** el panel se etiqueta «SOLO VISUAL / NO SAM». Las trayectorias originales no se editan ni se sustituyen por curvas teóricas. Ninguna llamada Meta; pruebas automatizadas de normalización y distinción de datos incluidos en Angular.

**English:** [PENDULUM-MATHEMATICAL-WORKBENCH.md](PENDULUM-MATHEMATICAL-WORKBENCH.md).
