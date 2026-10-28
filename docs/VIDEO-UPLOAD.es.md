# ProofLab AI — Carga directa de videos móviles (Misión 2)

**27/10/2026 · primer incremento de interoperabilidad multimedia.** El objetivo es que un participante o juez pueda cargar videos originales del péndulo sin convertirlos antes en CSV. **No implica que todo video pueda medirse correctamente ni que SAM 3.1 procese videos todavía.**

## Compatibilidad de archivos

Se aceptan contenedores **MOV, MP4, M4V, WebM, MKV, AVI, 3GP y 3G2** (extensión en mayúsculas o minúsculas). Se comprueba el flujo comprimido con **FFprobe**, independientemente del nombre o el MIME indicado por el navegador, y se admiten por política códecs **H.264, HEVC/H.265, AV1, VP8, VP9, MPEG-4 Parte 2, MJPEG, ProRes y H.263**. El soporte real depende de los decodificadores compilados en el FFmpeg instalado: nunca se promete que funcionen todos los teléfonos o combinaciones de contenedor/códec. La página funciona incluso cuando Chrome no reproduce directamente HEVC.

**Límites actuales:** 240 MB por archivo, 5 a 80 segundos, mínimo 160×120, hasta 8192×8192 con máximo 35,4 megapíxeles; de 5 a 240 FPS nominales. Se permiten hasta tres grabaciones de 50 cm y tres de 80 cm por predicción registrada. Los casos habituales 1080p60 o 4K se decodifican a **480×270, 12 FPS uniformizados** para el algoritmo local inicial; NO se usan todas las imágenes originales para realizar llamadas a Meta.

## Arquitectura del primer incremento

1. Usuario registra una predicción local 50 cm → 80 cm con ID, UTC y SHA-256 (esto ya existía).
2. Nueva interfaz **Demo estrella · Péndulo** permite seleccionar y subir directamente los videos, una grabación a la vez y sin depender de la reproducción nativa del navegador. La elección de la condición 50/80 cm la realiza el humano; no se deduce del nombre del archivo.
3. La API `POST /api/science/pendulum/preregistrations/{id}/video/{condition}/{take}` recibe los bytes del video original mediante POST de cuerpo binario, con `?filename=... `. Comprueba tamaño/nombre, limita cantidad y usa archivo temporal privado; la API no interpreta el códec por la extensión.
4. `VideoMediaDecoder` obtiene metadatos con FFprobe y decodifica con FFmpeg como proceso hijo de argumentos fijos, **sin shell, sin protocolos de red, sin enviar contenido a Meta**. Ejecuta solo binarios ubicados en PATH, links locales de WinGet o `Media:FfmpegDirectory` configurado. Limita duración y número de fotogramas, tiempo de proceso y concurrencia. Genera miniatura JPEG local del segundo 2 para que el navegador pueda mostrar algo aunque no reproduzca MOV/HEVC.
5. El rastreador experimental `PendulumMotionTracker` usa sustracción de fondo temporal local y trata de obtener el centro horizontal de un objeto móvil. **Esto NO es SAM 3.1**, no confirma que el objeto sea la plomada y puede fallar con manos, sombras, movimiento de cámara o cambios de luz. La UI muestra una curva preliminar y los fotogramas detectados para revisión visual.
6. El video, miniatura, trayectoria y SHA-256 de origen permanecen en `src/ProofLab.Api/.private/physics/pendulum/video-evidence`. La carpeta está ignorada por Git. No hay envío a servicios externos, aprobación automática, medidas inventadas ni llamadas a Meta. Un video puede aparecer como **RECIBIDO, NECESITA REVISIÓN** sin valor de período.
7. Si hay trayectorias candidatas revisables en ambas longitudes, `POST /api/science/pendulum/preregistrations/{id}/compare-videos` produce una **comparación descriptiva preliminar**, con promedio de hasta tres repeticiones por condición y dispersión si hay más de una. No es descubrimiento, seguimiento SAM ni verificación con línea de base humana.

**Otros endpoints:** `GET /api/science/pendulum/video-formats`, `GET /api/science/pendulum/preregistrations/{id}/videos`, `GET /api/science/pendulum/preregistrations/{id}/videos/{videoId}/poster`.

## Validaciones ejecutadas

- **iPhone 14 Pro Max real:** archivo MOV HEVC 1920×1080, ~60 FPS, duración 30,5 s, tamaño ~45 MB. La API temporal de prueba lo recibió, FFprobe reconoció HEVC, FFmpeg decodificó y el análisis local vio 316/366 fotogramas. **No obtuvo un período confiable**, por lo que informó `UPLOADED_NEEDS_MANUAL_TRACKING_REVIEW`. La subida/decode funciona; la medición todavía NO está validada.
- **Fixture local sintética Android-like:** MP4 H.264 de 640×360 y 8 segundos, codificado localmente para probar transporte y decodificación; subida satisfactoria, miniatura JPEG y consulta HTTP 200, libro de videos disponible. **No es video real de un Samsung** ni evidencia de oscilación.
- **Fixture WebM/VP9 sintética:** video de prueba 320×240 de 6 s, cargado y decodificado correctamente; miniatura JPEG generada. El sistema lo dejó en revisión manual por no contener un péndulo. Es una prueba técnica de interoperabilidad, no evidencia física.
- Los demás contenedores/códecs admitidos por código requieren una prueba equivalente. La inferencia de Muse Spark sigue pendiente y la integración SAM→trayectorias no ha sido realizada.

## Instalación, privacidad y criterios del jurado

FFmpeg es una dependencia de **servidor**, no del celular ni del navegador. Instalado solo en el host autorizado `DESKTOP-ESGSLJH` mediante WinGet (paquete `BtbN.FFmpeg.LGPL.8.1`, versión informada 8.1.3). El servidor de evaluación público necesitará FFmpeg y medidas adicionales: autenticación, autorización de carga, cuotas, gestión de archivos y privacidad; **esta API local no está preparada para exponer cargas anónimas en Internet**. Evitar grabaciones con personas o material privado sin consentimiento explícito.

La meta posterior es usar **SAM 3.1** en fotogramas reales muestreados y rastreo local entre inferencias, validando plomada, intervalos de confianza, tiempos y registro causal. No activar llamadas Meta sin verificar créditos y autorización del propietario. Antes de presentar una cifra en el concurso, revisar manualmente video y resultado.

## Hito: recuperación de períodos en las seis grabaciones reales (27/10/2026)

El estimador inicial de cruces ascendentes fallaba para las tres tomas de 50 cm porque había saltos y errores transitorios en el seguimiento local. Se añadió el estimador determinista `PendulumSpectralEstimator` (`LOCAL_ROBUST_SINUSOID_V1_NOT_SAM`), **sin utilizar longitud ni período teórico como entrada**: excluye de forma predefinida los dos primeros segundos, aplica mediana móvil de cinco muestras, limita los valores extremos a los percentiles 8 y 92, explora períodos posibles mediante ajuste sinusoidal, y comprueba calidad R², amplitud, detecciones y coherencia entre las dos mitades del video. Una señal periódica de alta calidad **puede pertenecer a otro objeto, sombra o fondo**: siempre exige revisión de la plomada en el video.

Para los originales ya almacenados, `POST /api/science/pendulum/preregistrations/{id}/videos/{videoId}/reanalyze` procesa **solo coordenadas previas** (sin volver a subir o decodificar el video, y sin Meta). Persiste una revisión inmutable `period-reviews/{videoId}-spectral-v1.json` por cada toma con huella de video y metodología, **sin sobrescribir el registro original**. `GET .../videos` combina ambos para visualizar las estimaciones, junto con R² y períodos estimados por mitades. Las nuevas cargas aplican el mismo método y guardan su análisis local con los datos recibidos.

**Ensayo real reproducible en API local:** seis tomas subidas por el usuario como `50CM_1/2/3.MOV` y `80CM_1/2/3.MOV`, HEVC/1080p; análisis espectral: **50 cm** = 1,4156 / 1,4172 / 1,4180 segundos; **80 cm** = 1,7976 / 1,7984 / 1,7996 segundos. El promedio resultó 1,41693 s y 1,79853 s respectivamente, cambio estimado **26,9314 %**, frente al cambio teórico **26,4911 %** previamente registrado (diferencia +0,4403 puntos porcentuales). Las seis estimaciones cumplieron filtros espectrales, pero **NO hay verificación manual independiente del objeto rastreado, error temporal calibrado ni medida de incertidumbre física completa**. La variación entre tomas no captura errores sistemáticos.

La UI ES/EN ofrece «Reanalizar videos guardados (sin Meta)» para estudios antiguos, conserva el análisis previo y desbloquea «Comparar videos procesados» cuando hay candidatos en ambas condiciones. Ningún expediente ni video se certifica automáticamente. Es solo una comparación descriptiva a partir de datos aportados y procesamiento local.

**English:** [VIDEO-UPLOAD.md](VIDEO-UPLOAD.md).
