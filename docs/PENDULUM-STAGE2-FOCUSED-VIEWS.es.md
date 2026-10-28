# ProofLab AI — Cuatro perspectivas con vista principal intercambiable

**Misión 2 / Etapa 2 · Mejora de visualización aprobada por el propietario.** Este cambio es de interfaz y sincronización: NO acredita un descubrimiento científico, calibración física ni seguimiento por SAM 3.1.

## Experiencia visible

- Distribución **70 % principal / 30 % lateral** en escritorio. Vista inicial **Vectores + rastro fantasma** en grande; a la derecha, miniaturas **Video original**, **Vectores** y **Rastro fantasma**.
- Al pulsar «Ampliar» o hacer clic sobre una miniatura, **esa vista y la principal intercambian exactamente sus posiciones**. Las otras dos miniaturas conservan sus lugares. Los cuatro elementos visuales permanecen montados en el DOM: **no se reinicia, pausa ni busca automáticamente el video al seleccionar una vista**.
- El reproductor tiene **un solo elemento HTMLVideoElement** y **tres canvas** (vector, trayectoria reciente y composición). Los tres derivados dibujan el **mismo fotograma decodificado** en el mismo instante. Pausar, scrubbing, 0,25×–2× y avance de fotograma siguen controlados por el único reloj maestro.
- «Ampliar imagen 1,6×» usa una transformación visual compartida a todas las vistas; «Ajustar imagen» vuelve al encuadre completo. La ampliación puede recortar los bordes: **no cambia píxeles guardados ni coordenadas de medición**.
- Diseño adaptable: lateral a la derecha en escritorio y, en pantallas más estrechas, miniaturas debajo de la principal.
- Estados y controles bilingües ES/EN. Antes de cargar el primer archivo se conserva el formulario y los placeholders iniciales; después de subirlo aparecen las cuatro perspectivas.

## Honestidad científica y límites

El vector cian representa únicamente **velocidad aparente en píxeles por segundo** calculada a partir de candidatos X/Y del seguidor local de fondo; los puntos violetas muestran posiciones candidatas de los **1,5 segundos anteriores**. Combinarlos **no valida** que sigan la plomada correcta. NO se inventan flechas ni trayectorias ante muestras faltantes. **No SAM 3.1, no ω/α calibrados en esta vista, no coordenadas XY físicas**. El video compatible continúa H.264 **640px / 12FPS** (el MOV original y su SHA-256 siguen intactos en privado).

## Calidad y aceptación

Pruebas ChromeHeadless comprueban: renderizado de **1 video + 3 canvas**, tres miniaturas y vista combinada predeterminada; intercambios exactos sin sustituir el nodo de video ni alterar `currentTime`/`playbackRate`; zoom sin seek; los tres canvas leen la misma fuente y la composición recibe formas de vector y rastro; ausencia de rastros falsos si no hay muestras. Se ejecutan .NET Release, build Angular y `git diff --check`, más CI por SHA.

**Aún pendiente de aceptación humana:** revisar en la pantalla real la nitidez del encuadre y que las flechas y rastros sigan físicamente la plomada. Selección de pivote y cinemática angular calibrada de Etapa 2 no se declaran completadas.

**English:** [PENDULUM-STAGE2-FOCUSED-VIEWS.md](PENDULUM-STAGE2-FOCUSED-VIEWS.md).
