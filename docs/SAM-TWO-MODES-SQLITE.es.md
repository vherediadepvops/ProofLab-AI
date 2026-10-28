# ProofLab AI — Etapa 4: dos modos SAM y SQLite

**Fecha:** 28/10/2026 · **Misión:** Autonomous Physics Discovery / péndulo · **Estado:** integración técnica implementada; validación local con datos sintéticos. No se ejecutó una inferencia Meta nueva ni se validó científicamente la plomada.

## Modos y contrato

| Selección | Comportamiento |
|---|---|
| **API Meta** (`api-meta`) | Solicita una inferencia SAM nueva, aunque exista un análisis previo. No presenta la caché como ejecución nueva. Requiere autorización explícita, créditos y admisión del servidor; cada ejecución conserva su propio ID e historial. |
| **API Meta + caché** (`api-meta-cache`, predeterminado) | Consulta SQLite por identidad exacta de video/modelo/protocolo/prompt y verifica los archivos. Un HIT recupera evidencia sin token, permiso de cobro ni llamada a Meta. Un MISS solo ofrece una llamada manual cuando el servidor la permite. |

El selector principal tiene exactamente estas dos opciones. El token se introduce en **Configuración**; la selección de puerto local queda allí en **Conexión avanzada**, cerrada por defecto; 5391 sigue siendo el puerto principal. Elegir un puerto o pegar una clave no habilita pagos.

- `GET /api/science/director/pendulum/sessions/{id}/sam/{role}/status?mode=api-meta-cache` consulta el estado. También acepta `mode=api-meta`.
- `POST .../sam/{role}/manual-one-shot` y `POST .../sam/{role}/analyze` reciben `mode`; omitirlo conserva el modo caché. La UI utiliza el primero con la acción manual existente.
- El estado añade `analysisId`, `mode`, `cacheHit` y `storage`. Un HIT conserva `CACHED_META_...`. El modo nuevo usa `READY_FOR_FRESH_META_ANALYSIS` cuando corresponde; el permiso final depende de los controles del servidor.
- `GET .../sam/{role}/evidence` recupera la última evidencia verificada. La UI recupera la evidencia mediante GET después de pulsar Procesar o completar una inferencia autorizada; verificar el costo o reproducir nunca inicia un POST a Meta.
- Una ejecución nueva completada devuelve su ID y `cacheHit=false`. La presencia de metadatos de un análisis anterior en el estado no demuestra que se haya ejecutado uno nuevo.

## Costo y botón Procesar

Al cargar/restaurar un video o cambiar modo/puerto, la pantalla consulta automáticamente estado y preflight mediante GET locales. Siempre muestra el costo estimado de enviar el video original a Meta; con una caché válida también indica **US$0 por esta operación**. La estimación no es una factura. Coincidir significa identidad exacta del archivo original, modelo y protocolo, no parecido visual entre imágenes.

**Procesar** permanece visible. En modo caché, un HIT selecciona el análisis existente sin token ni llamada de pago; un MISS exige token, consentimientos y autorización del servidor para enviar una única solicitud. API Meta exige esos mismos controles y solicita un análisis nuevo. Los intentos previos pendientes de reconciliación siguen bloqueados.

En Laboratorio, antes de pulsar Procesar solo hay una vista estática: reproducción, avance de fotogramas, barra temporal y confirmación de revisión están bloqueados. El resultado debe corresponder al SHA y al ID de análisis seleccionado antes de habilitar reproducción. Los gráficos proceden exclusivamente de máscaras SAM verificadas; no se sustituye un error por rastreo local. Un resultado auténtico sin detecciones permite reproducir, indicando que no hay máscaras y sin inventar gráficos. Cambiar video, prueba, modo o puerto invalida el resultado visible.

**Validación de este flujo (28/10/2026):** 77 pruebas Angular focalizadas correctas y build de producción correcto; sin nuevas inferencias Meta ni CI. Las pruebas del POST son simuladas; la caché existente se comprueba localmente. Persiste la advertencia de presupuesto inicial (569,90 kB frente a 500 kB).

## Selección explícita y estados de carga

El Laboratorio inicia vacío aunque exista una sesión recordada. **Video de prueba** carga la grabación local configurada solo al pulsarlo. **Nueva prueba** sustituye los dos botones de la tarjeta superior por **Subir video** y **Cancelar**. Cancelar conserva el video y el resultado actualmente seleccionados; elegir un archivo válido crea otro estudio y conserva los anteriores en el servidor. No hay un segundo control de carga principal debajo. Cargar un video no ejecuta SAM.

Para configurar la demostración en una instalación local, copiar `ui/public/demo-video.example.json` a `ui/public/demo-video.json` y escribir el ID de un estudio existente con el video original. Reiniciar el servidor Angular después de crear este archivo. Este archivo está ignorado por Git; no incluir videos, tokens ni identificadores privados en el ejemplo público.

La vista previa y la consulta del costo avanzan independientemente al estar disponibles los datos del video. Sus tarjetas muestran barras de actividad separadas, sin porcentajes ficticios. Procesar requiere vista previa lista y estimación válida terminada, incluso con HIT. Al procesar desde caché o Meta se indica actividad hasta validar el resultado; se bloquean dobles clics y cambios de fuente/modo/puerto. Un error detiene el indicador y deja la reproducción bloqueada.

Validación local de este ajuste: 58 pruebas Angular focalizadas y build correcto. Las solicitudes de pago se prueban con HTTP simulado. No requiere CI.

## Reproducción del original y sincronización (28/10/2026)

El reproductor usa `GET/HEAD .../original/{role}`: bytes exactos del archivo conservado, SHA-256 verificado, Range y ETag. No cambia códec, resolución ni FPS. Las cargas nuevas ya no generan el proxy de revisión de 12 FPS; los archivos y endpoints históricos se conservan para compatibilidad. El muestreo FFmpeg del análisis local es independiente de la reproducción.

Las tres vistas derivadas dibujan el mismo video a su resolución decodificada nativa. Las máscaras se seleccionan con `requestVideoFrameCallback.mediaTime`, también al pausar y buscar; el avance manual usa los PTS originales verificados cuando existen y la frecuencia nominal en caso contrario. Se busca dentro del fotograma para evitar que el redondeo de un PTS seleccione el anterior. Los vectores y el rastro siguen siendo cálculos locales desde las máscaras SAM, no resultados gráficos enviados por Meta.

Si falla la reproducción o el navegador no admite el códec, se muestra un error y un enlace para descargar el original; no se sustituye silenciosamente por una copia reducida. Se informa cuando el navegador reporta fotogramas descartados. Conservar un archivo de 4K a 120/240 FPS no garantiza reproducir cada fotograma en cualquier hardware/pantalla; los límites existentes de carga y presupuesto siguen vigentes.

Verificación local: original HEVC de 59,94 FPS, bytes/SHA idénticos, imagen decodificada 1080×1920 con rotación y las tres vistas a esa resolución. 13 comprobaciones HTTP, 12 de navegador y 77 pruebas Angular focalizadas correctas; tras ajustar el redondeo de PTS, 33 pruebas afectadas repetidas correctamente. API y UI compiladas. Tamaño inicial de UI: 587,11 kB (advertencia existente frente a 500 kB). Pruebas unitarias cubren dimensiones 4K y cadencias 120/240; no se hizo una prueba de rendimiento con grabación real 4K/240. Sin CI ni nuevas llamadas a Meta.

### Geometría de las mediciones SAM

Las posiciones y el pivote guardado mantienen su cuadrícula compatible de 480×270 solo para almacenamiento/dibujo. Antes de calcular ángulo, derivadas angulares, radio y vectores, se recuperan las proporciones de `videoWidth/videoHeight` del original decodificado, incluida su orientación. Así, un video vertical o cuadrado no se trata como si fuera 16:9. La rapidez en px/s se expresa en píxeles del original; el tamaño de la ventana y el zoom no alteran las magnitudes. Las máscaras y los pivotes históricos no se reescriben.

Este ajuste corresponde al flujo SAM del Laboratorio. Los registros históricos del seguimiento local conservan su sistema y procedencia. El ángulo todavía se refiere a la vertical de la imagen: quedan pendientes la confirmación de vertical física, perspectiva y calibración métrica. No se habilitan m/s ni se ajustan observaciones al modelo ideal.

Validación local: 55 pruebas focalizadas correctas, incluidas 15 combinaciones de ángulos −30°/0°/30° y formatos vertical/horizontal/cuadrado/4K; estas 15 regresiones fallaban antes de la corrección. Integración comprueba orientación decodificada, vectores tangentes perpendiculares al radio y pivote/zoom estables. Build correcto con advertencia existente de tamaño inicial (587,59 kB / 500 kB).

### Ángulo de la cuerda con la vertical

El Laboratorio destaca θ en grados y el lado de inclinación: negativo a la izquierda, positivo a la derecha y cero sobre la vertical. La vista combinada añade una vertical discontinua desde el punto de suspensión, el eje suspensión–centro de la plomada y el arco del ángulo; el original sigue sin anotaciones. Las otras magnitudes existentes quedan en «Más mediciones · experimentales».

Es una estimación del eje de la cuerda mediante el centroide SAM y la suspensión marcada por el usuario, no una detección independiente de la cuerda. La referencia es la vertical de la imagen; requiere cámara nivelada y perpendicular al plano del movimiento para interpretarla físicamente. Se calcula atan2(Δx, Δy) con las proporciones del original decodificado, sin metros ni longitud declarada ni ajuste al ángulo inicial ideal.

La lectura usa el fotograma decodificado y una máscara próxima dentro de medio intervalo nominal (máximo 55 ms). Sin detección, sin suspensión o con eje degenerado se muestra «—». No se conserva el último valor en un hueco ni se sustituye por rastreo local. Un único centro válido permite medir el ángulo aunque no haya derivadas; zoom y tamaño de ventana no cambian la lectura.

Validación local: 66 pruebas Angular focalizadas correctas y build de producción correcto. Se cubren −30°/0°/+30° en vertical, horizontal 4K y cuadrado, extremos sin derivadas, huecos, bloqueo antes de Procesar, signos ES/EN y conservación del original. Advertencia existente: tamaño inicial 595,62 kB / 500 kB. Sin llamadas nuevas a Meta ni CI.

### Velocidad angular junto al ángulo

La tarjeta muestra ω en rad/s y su equivalente en °/s junto a θ, actualizados por el mismo fotograma decodificado. ω positiva significa cambio de θ hacia la derecha; negativa, hacia la izquierda. La posición de la plomada y la dirección del movimiento son conceptos distintos. Cambiar la velocidad del reproductor no multiplica la magnitud medida.

Reutiliza la derivada angular de tres posiciones SAM con tiempos originales y diferencias temporales no uniformes, conservando el desenvolvimiento de ángulos y los controles de calidad existentes. Solo se muestra si hay un ángulo actual y una derivada válida para ese mismo centro. En extremos sin vecinos, huecos, datos inválidos o falta de detección, muestra «—»; no usa el modelo ideal ni seguimiento local como reemplazo. Cero medido sigue siendo cero y no se trata como dato ausente. Es una estimación de imagen, con la misma referencia de cámara que θ.

Verificación local: 70 pruebas Angular focalizadas y build correctos; incluye dirección independiente del signo de θ, conversión de unidades, cero, ausencia de datos, cadencia no uniforme y cruce de ±π. Advertencia existente del paquete inicial: 599,14 kB / 500 kB. Sin nuevas llamadas Meta del asistente ni CI.

### Lecturas pintadas sobre el video

Las vistas combinada y de vectores dibujan θ (°), ω (rad/s) y el tiempo del fotograma dentro de la imagen. Usan los mismos valores y el mismo fotograma decodificado que las tarjetas: se actualizan al reproducir, buscar o avanzar un cuadro, y permanecen estables al pausar. Sin dato válido muestran «—» y la causa breve. El tamaño de lectura se adapta a la vista; cambiar idioma con el video pausado también repinta el texto. Original y rastro conservan su presentación anterior.

Pruebas del reproductor: 27/27 correctas y build correcto; advertencia de tamaño inicial existente: 600,85 kB / 500 kB. Sin llamadas nuevas a Meta ni CI.

## Token manual y habilitación de Procesar (28/10/2026)

El token registrado en Configuración satisface el requisito de credencial de la solicitud manual; no exige duplicarlo en MODEL_API_KEY del servidor. El servidor sigue validando su formato, créditos confirmados, autorización, interruptor de pagos y presupuesto compartido antes de reservar la solicitud. La admisión de caché y la reserva final reconocen el mismo token de un solo uso; no se guarda ni habilita globalmente otras llamadas.

Readiness expone manualTokenCallsPermitted como capacidad de admisión con token manual, no como validación de una clave ni prueba de saldo. Registrar el token no activa pagos: Procesar además requiere video/costo válidos, transferencia y créditos confirmados, y controles del servidor abiertos. El bloqueo indica el requisito pendiente. La configuración del repositorio sigue cerrada por defecto; una sesión habilitada requiere autorización del propietario. Se mantienen el límite de dos solicitudes por proceso, US$0,50 estimados por video y la reconciliación de intentos previos.

Verificación local: 40 pruebas .NET y 49 Angular focalizadas correctas, API y UI compiladas. La UI conserva la advertencia de tamaño inicial (589,65 kB / 500 kB). Transporte pagado simulado en pruebas; sin llamadas reales del asistente ni CI.

## Persistencia y procedencia

`SamVideoEvidenceIndex` usa Microsoft.Data.Sqlite bajo `.private/physics/sam-video-evidence.sqlite`, separado de la memoria matemática de Lean. SQLite guarda **solo metadatos**: SHA del video, modelo/protocolo, hash del prompt, hashes de respuesta y recibo, ruta privada relativa, ID de análisis, fecha, cantidad de fotogramas y procedencia. No guarda tokens, videos, máscaras, coordenadas, prompts en texto claro ni respuestas completas.

`PendulumSamCache` conecta ese índice con los archivos privados de respuesta y recibo. Cada ejecución se guarda en `.private/physics/sam-cache/<key>/runs/<analysisId>/`; una nueva inferencia no sobrescribe el archivo histórico anterior. El backend vuelve a comprobar identidad, hashes, recibo y tiempos del video antes de reutilizarlo. Puede recuperar un resultado completo conservado en disco tras una interrupción sin volver a llamar al proveedor. Una fila SQLite aislada o un JSON importado manualmente no certifican origen Meta.

La identidad incluye video original SHA-256, `sam-3.1`, protocolo `PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2`, clave de caché y hash del prompt. Las rutas se restringen al archivo privado permitido; los conflictos de identidad o procedencia se rechazan.

## Autorización y concurrencia

Se conservan las comprobaciones de transferencia del video privado, créditos, autorización de cobro, límite estimado, SHA/fotogramas/PTS e intentos previos. El servidor mantiene guardias entre procesos para evitar solicitudes simultáneas incompatibles y no hace reintentos automáticos. La reconciliación manual de un intento posiblemente facturado sigue siendo explícita. Un fallo de análisis nuevo devuelve `NEW_ANALYSIS_REQUIRES_MANUAL_RECONCILIATION` y no habilita el reintento excepcional del intento histórico.

La clave se configura en el campo de contraseña de **Configuración**, que permanece montado al navegar. Solo su disponibilidad se comparte con Laboratorio; el valor se lee y borra al ejecutar manualmente y nunca se persiste. Las comprobaciones GET no consumen el token. Cambiar modo o puerto limpia consentimientos y descarta comprobaciones anteriores. Durante un envío se bloquea ese cambio. El inspector JSON tiene un contador independiente y no puede dejar la consulta o el envío bloqueados.

## Verificación local

- Angular: **27/27 pruebas de integración inicial y 14/14 del control manual final correctas** (28 casos únicos), con HTTP simulado: dos opciones, HIT solo GET, MISS con controles cerrados, consentimiento antes de un único POST, ausencia de reintentos, cambios de modo/puerto, respuestas obsoletas, inspector independiente y recarga GET del reproductor.
- `npm run build`: correcto. Advertencia de tamaño inicial: 528,07 kB frente al umbral de 500 kB; no impide compilar.
- Backend: **181/181 pruebas .NET correctas**; incluye 15 casos de integración Stage 4 y 11 del índice (26 en total). Compilación API: **0 errores y 0 advertencias**. Las pruebas usan archivos y transporte sintéticos; cubren persistencia, identidad exacta, conservación de históricos, recuperación tras interrupción, conflictos, rutas y controles de ejecución.
- La validación se ejecuta en la PC física autorizada. **CI es manual y solo se ejecuta cuando el usuario lo solicita**; subir cambios a GitHub no exige ejecutarlo.

Estas pruebas no son segmentaciones reales del péndulo. La inferencia SAM del video, identificación humana de la plomada, calibración e incertidumbre siguen pendientes. No se declara gasto Meta nuevo ni descubrimiento científico. La validación técnica de la etapa 4 no completa esas etapas científicas.

**English:** [SAM-TWO-MODES-SQLITE.md](SAM-TWO-MODES-SQLITE.md).