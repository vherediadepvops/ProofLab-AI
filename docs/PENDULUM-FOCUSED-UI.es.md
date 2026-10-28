# ProofLab AI — Interfaz enfocada para jurado: configuración + tres vistas

**28/10/2026 (UTC) · Mission 02 / Zona 1.** Refinamiento visual solicitado por el propietario a partir de capturas de una tarjeta central demasiado estrecha.

## Decisión de interfaz

La pantalla principal de ProofLab para el jurado debe presentar **solo** el formulario de configuración física y las **tres vistas de un único video**: fuente, vectores aparentes y rastro fantasma. Se ocultan de esta ruta el encabezado grande, la barra lateral de misiones, los gráficos matemáticos previos, la repetibilidad 3+3 y los paneles científicos secundarios. **Nada se borra del código ni de los datos**. La física ideal no lineal, el almacenamiento de evidencias, el flujo de una o dos pruebas y los reportes existentes permanecen disponibles para futuras fases.

## Implementación concreta

- `ui/src/app/app.{html,ts}`: mantiene `workspace='pendulum'`, pero **oculta la barra lateral y el héroe** mientras esa ruta está activa; el encabezado se reduce a marca ProofLab AI, Péndulo y selector ES/EN. Renderiza `<app-pendulum-zone-one>` en lugar del antiguo panel grande y deja de mostrar el laboratorio 3+3 en esta pantalla.
- `ui/src/styles.scss`: la clase `.focus-shell .workspace.focus-workspace` elimina el `max-width:1200px`, utiliza **100 % del área horizontal disponible**, espaciado adaptable y diseño móvil. La ruta de las misiones antiguas mantiene sus estilos porque las reglas solo se activan para Péndulo.
- `ui/src/app/pendulum-zone-one.{ts,html,scss}`: nueva capa de **presentación** que hereda y reutiliza `PendulumDirector`; NO duplica lógica, cálculo matemático ni endpoints. Encabezado pequeño «Investiga un fenómeno con un video», configuración de **longitud en cm**, ángulo en grados sexagesimales, masa en g, casilla de segundo experimento opcional y longitud sugerida local NO IA o elegida manualmente. Antes de recibir video, tres paneles informativos muestran dónde estará cada vista.
- Una vez registrado el estudio, se carga **un video real** (MOV/MP4/otros). Si existe el proxy reproducible, `PendulumMasterPlayer` dibuja tres vistas **con un único HTMLVideoElement y dos canvas**. El formulario queda congelado hasta «Nueva investigación» para conservar la coherencia con el registro `modelSha256`. No se crea ni aprueba automáticamente ninguna evidencia.
- Si se eligió segunda prueba, la interfaz preserva la **confirmación humana**, la predicción local explícitamente **NO IA** y la carga del segundo video. Una pestaña «Prueba 1 / Prueba 2» permite cambiar la **única terna de vistas**; nunca se dibujan seis reproductores simultáneos.
- Los gráficos teóricos previos, CSV de modelo y tablero viejo de física **siguen en el repositorio**, pero no aparecen en la pantalla simplificada. Esta es una decisión de prioridad de la experiencia, no eliminación de las misiones.

## Límites científicos y de fidelidad

El video original iPhone HEVC/MOV permanece privado e intacto, pero el navegador reproduce por ahora **H.264 a 640 px de ancho y 12 FPS**. Los «vectores» y el «rastro» son **estimaciones locales en píxeles**, NO máscaras SAM 3.1 ni vectores de velocidad angular físicamente calibrados. No se enviaron solicitudes a Muse/SAM, no se activaron pagos de Meta ni se realizaron nuevas afirmaciones científicas.

## Criterios de aceptación

1. En desktop, **no hay sidebar ni héroe**; tarjeta de Péndulo **sin límite máximo de 1200 px**: aprovecha el ancho del navegador.
2. Solo aparecen **configuración + panel de video**; no hay `.model-chart`, `.technical-protocol` ni resultados matemáticos secundarios en pantalla.
3. Antes de cargar video hay tres espacios; tras cargar uno hay **un elemento video y dos canvas**, sincronizados por el mismo reloj (Etapa 1 ya verificada).
4. Un segundo ensayo sigue siendo opcional, usando pestañas de selección en lugar de seis paneles simultáneos. ES/EN funcionan. Los controles de pago Meta permanecen desactivados.
5. La lógica heredada se mantiene en `pendulum-director.*`, `pendulum-study.*`, matemáticas, APIs y memoria compartida.

## Corrección de navegación: selector inmediato al crear una investigación

**Hallazgo real del propietario en la pantalla:** después de pulsar «Nueva investigación» aparecían parámetros y las tres ventanas vacías, pero **no aparecía «Seleccionar video»** porque la versión D-030 exigía pulsar además «Iniciar investigación». La condición `@if(session())` ocultaba el selector hasta registrar la sesión. Se corrige sin cambiar el almacenamiento ni la lógica científica.

**Nuevo flujo principal:**
1. Abriendo Péndulo o pulsando «Nueva investigación», el bloque de video muestra inmediatamente **«Sube tu primera grabación» / «Seleccionar video»**, incluso si todavía no existe un estudio.
2. El investigador ajusta longitud, ángulo y masa, y elige un archivo. La UI valida extensión y 10 KB–240 MB **antes** de crear un expediente; errores de formato no crean sesiones.
3. Se recalcula el modelo matemático local desde los valores visibles, se registra el estudio con su huella teórica `modelSha256` y **solo después** se sube el archivo a `/video/baseline`. No existe doble pulsación ni fórmula entregada por el usuario.
4. Si falla la creación del estudio, se muestra el error y **el selector permanece disponible para reintentar**. Si el upload falla tras crearse la sesión, ésta se conserva y se permite seleccionar de nuevo el archivo sin alterar originales.
5. Las tres vistas funcionan igual que antes al terminar el upload. «Nueva investigación» abandona únicamente la sesión visible, no borra los expedientes privados existentes. La segunda grabación conserva el requisito previo de predicción congelada cuando corresponde.

**Pruebas de regresión:** selector presente antes de tener sesión, petición del modelo → petición de creación → petición de subida en orden, archivo inválido sin alta, error de registro y posibilidad de reintentar, «Nueva investigación» mostrando otra vez el control, y reproductor de 1 video + 2 canvas cuando se procesa un archivo. Todo el frontend trabaja localmente, **0 llamadas Meta**.

**Documentación del reproductor:** [PENDULUM-ZONE1-MASTER-PLAYER.es.md](PENDULUM-ZONE1-MASTER-PLAYER.es.md). **English:** [PENDULUM-FOCUSED-UI.md](PENDULUM-FOCUSED-UI.md).
