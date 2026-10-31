# Parámetros físicos opcionales / Optional physical inputs

## Comportamiento / Behavior — 31/10/2026

Las investigaciones nuevas empiezan con longitud, ángulo inicial y masa en null, sin valores de ejemplo. Los campos vacíos son grises y tienen un pulso suave; un dato válido es verde y un dato fuera de rango es rojo. Movimiento reducido desactiva el pulso. Cero grados es un ángulo válido; cero longitud o masa no lo son. Los campos vacíos se guardan y recuperan como null.

Se permite cargar y analizar sin parámetros físicos. Antes del análisis, el formulario y la tarjeta de procesamiento explican qué medidas requieren esos datos. Con seguimiento y geometría válidos se pueden observar ángulo, velocidad angular y período. Sin longitud no se calculan velocidades/aceleraciones métricas ni una predicción de cambio de longitud; sin masa no se obtiene energía. Sin ángulo inicial no se genera una amplitud teórica predeterminada. Ningún campo vacío se sustituye por 50 cm, 15° o 300 g.

Verde significa entrada válida, no exactitud verificada. Un intervalo del 95 % solo se calcula después con suficientes ciclos y describe repetibilidad del período; no certifica toda la investigación ni la probabilidad de que una hipótesis sea verdadera. Muse recibe procedencia NOT_PROVIDED para longitud ausente, sin opciones de experimento que dependan de ella. No puede congelar una predicción de longitud usando un valor inventado o inferido del período como si fuera una calibración independiente.

New studies start with null length, initial angle and mass. Empty inputs pulse grey, valid inputs turn green, and invalid supplied values turn red. Reduced motion disables animation. Zero degrees is valid; zero length/mass is invalid. Saving/recovery preserve nulls. Tracking and geometry can support observed angle, angular velocity and period without supplied length. Metric speed/acceleration and length-change predictions require length; energy requires mass. No sample defaults are substituted. Green is input validity, not accuracy or 95% confidence. The later period confidence interval describes repeatability only. Muse cannot freeze length-based predictions without a supplied length.

## Verificación / Verification

- Build Angular de producción y API .NET correctos; advertencias de presupuesto CSS/bundle existentes.
- 44 pruebas .NET focalizadas (cinemática/Muse) y 13 casos Angular pertinentes en Edge correctos.
- API aislada: crear, guardar 0°, borrar datos, recuperar nulls y rechazar longitud/masa cero.
- Edge aislado: campos vacíos, colores/estados, movimiento reducido y ancho móvil 390 px; sin errores JavaScript. No se usó Chrome del propietario.
- Cero llamadas Meta y cero ejecuciones CI. No se certifica aquí la precisión científica de nuevas inferencias.

La ejecución exploratoria de 45 pruebas UI encontró además expectativas ajenas a este cambio sobre títulos, modal de token y rutas antiguas de estado/caché. No se declara la suite completa aprobada ni se modifican esas expectativas en este cambio.

English: targeted checks passed (44 .NET, 13 Angular/Edge, isolated HTTP persistence and desktop/mobile visual QA). The exploratory wider UI run exposed unrelated expectations for headings, token-modal input counts and old status/cache URLs; a full-suite pass is not claimed. No provider calls or CI runs were made.
