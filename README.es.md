# ProofLab AI — Hackathon Meta 2026

**Edición de competencia — creada el 26 de octubre de 2026 después de la inauguración oficial.**

ProofLab AI es un **motor de evidencia científica** para investigadores adultos,
desarrollado para la temática *Push the Frontier* del Meta Global AI Developer Hackathon.
Combina mediciones físicas con modelos de Meta para formular hipótesis comprobables,
predecir resultados y explicar diferencias entre la teoría y las observaciones.

**Estado: alfa.** La prueba del propietario obtuvo **respuesta real `status: completed` de SAM 3.1** con caja y máscara codificada para una pelota sintética. El decodificador/overlay local de máscaras se verificó **con muestras sintéticas oficiales**, pero aún falta comprobar ese mismo resultado real en la nueva interfaz. La inferencia exitosa de Muse Spark sigue pendiente; no hay descubrimiento científico original.

## Procedencia del código competitivo

Este es un proyecto **nuevo**, independiente del prototipo privado anterior
`C:\PROOFLAB-AI`. No se copiaron archivos fuente anteriores a la inauguración.
El historial Git registra trabajos realizados después del kickoff del 26 de octubre,
de acuerdo con la respuesta oficial de Cerebral Valley en Discord que permite empezar
a desarrollar inmediatamente después de la inauguración.

La entrega final deberá ser pública, original y distinguir qué funcionalidades
se desarrollaron durante el evento. No presentaremos simulaciones como datos reales.

## Funcionalidades actuales

- **Respuestas bilingües ES/EN:** explicaciones de Lean, resultados, mensajes e informes generados respetan el idioma seleccionado, sin alterar el resultado formal ni generar nuevas llamadas de pago. Consulta [política de idioma](docs/LOCALIZATION.es.md).

- **Scientific Method Engine (Misión 1):** cinco etapas visibles, campos obligatorios por informe, bloqueos en el servidor, borradores locales persistentes, confirmación humana de completitud, huella SHA-256 y exportación Markdown de cada etapa y del informe global. **No** certifica validez científica. Ver [metodología](docs/SCIENTIFIC-METHOD.es.md).

- **Evidencia real de Lean vinculada (Misión 1):** un ciclo determinista y las tácticas controladas ejecutan Lean 4; las etapas 4–5 exigen IDs auténticos de intentos y las bitácoras se agregan a los informes. Es un control de teoremas conocidos, no un descubrimiento de IA. [Flujo de evidencias](docs/MISSION-001-EVIDENCE.es.md).
- **Misión 1 — Descubrimiento matemático (primera etapa):** tres teoremas conocidos, tácticas permitidas de Lean 4, verificación real, huellas SHA-256 y registro privado de intentos. **No hemos resuelto ninguna conjetura abierta.** Ver [Misión 1](docs/MISSION-001-MATH.es.md).
- **Conjetura abierta curada (Goldbach fuerte):** catálogo con fuentes y fecha de revisión, exploración finita hasta 2000 con algoritmo y huellas reproducibles, bitácora por expediente e informes ES/EN. **No equivale a demostración Lean ni hallazgo nuevo.** Ver [investigación abierta](docs/MISSION-001-OPEN-RESEARCH.es.md), [contrato del agente](docs/MISSION-001-AGENT.es.md) y [diseño del futuro sandbox Lean](docs/LEAN-SANDBOX.es.md).
- **Ensayo offline de Misión 1 (SIN Muse Spark):** flujo programado que enlaza cálculo Goldbach finito, táctica Lean `rfl` rechazada, reintento condicional `omega`, registro de decisiones y anexos ES/EN. Los informes siguen requiriendo aprobación documental humana declarada y no hay descubrimiento nuevo. [Protocolo del ensayo](docs/MISSION-001-OFFLINE-REHEARSAL.es.md).
- **Memoria científica SQLite:** almacena resultados Lean reales y recupera experiencias de otros expedientes para priorizar estrategias ya verificadas en el mismo teorema. Cada táctica recuperada vuelve a comprobarse con Lean y produce un nuevo ID. Sin IA, sin aprendizaje de pesos ni prueba de conjeturas abiertas. [Memoria científica](docs/SCIENTIFIC-MEMORY.es.md). El [esquema SQL versionado](src/ProofLab.Api/Database/001_scientific_experience.sql) se publica aquí; los datos reales `.sqlite` siguen privados.
- **Borradores guiados ES/EN (sin IA):** botón para proponer campos de las cinco etapas usando información Goldbach curada (1–3) o evidencia real del expediente (4–5); un segundo botón ejecuta Goldbach finito + control Lean conocido en etapa 4. No sobrescribe campos, no aprueba automáticamente ni demuestra Goldbach. [Guía de borradores](docs/MISSION-001-GUIDED-DRAFTS.es.md).

- **Interfaz principal · Péndulo (enfoque para el jurado, sin Meta):** pantalla de **ancho completo** con configuración de longitud en cm, ángulo en grados y masa en g, casilla de segunda prueba opcional y **cuatro perspectivas sincronizadas de un solo video** (principal combinada de vectores + rastro, video original, vectores y rastro). La principal ocupa el 70 % del ancho; cualquiera de las tres miniaturas laterales puede intercambiarse con ella sin reiniciar el video; zoom de imagen 1,6× opcional **con arrastre de mouse/táctil y botón Ajustar**. Tras cargar un video, **Marcar pivote** permite registrar el punto de suspensión mediante clic y recuperar ángulo, ω, α y vectores locales con unidad, calidad y separación de teoría ideal (NO SAM, NO m/s calibrados). [Guía de zoom/pivote ES](docs/PENDULUM-ZOOM-PAN-KINEMATICS.es.md). Encabezado gigante, barra lateral y gráficas matemáticas antiguas **ocultos solo en este recorrido, código y evidencias conservados**. Reproducción mediante proxy H.264 de 12 FPS; los MOV/MP4 originales se guardan intactos con SHA-256. El seguimiento NO es SAM 3.1, y la propuesta de segundo experimento sigue siendo local NO IA. **«Seleccionar video» aparece inmediatamente y después de «Nueva investigación»**; elegir archivo registra automáticamente la teoría antes del envío. [Interfaz enfocada ES](docs/PENDULUM-FOCUSED-UI.es.md) · [Cuatro perspectivas ES](docs/PENDULUM-STAGE2-FOCUSED-VIEWS.es.md) · [Reproductor ES](docs/PENDULUM-ZONE1-MASTER-PLAYER.es.md) · [Modelo matemático conservado](docs/PENDULUM-MATHEMATICAL-WORKBENCH.es.md).

- **Etapa 3, parser SAM para VIDEO (local / SIN COBROS):** decodifica JSON completo aportado por el usuario mediante el parser oficial, preserva frame/ID de objeto y calcula centroides desde máscaras, con visor opcional en Zona 1. El inspector manual usa muestras sintéticas y no autentica SHA/PTS; el flujo del servidor sí verifica esos vínculos. **Aún no hay un resultado SAM del péndulo ni validación de identidad de plomada**. [Guía Etapa 3](docs/SAM-VIDEO-STAGE3.es.md).

- **Etapa 3, fuente SAM auténtica y caché antes de SQLite:** nuevo backend controla SHA del MOV, remux MP4 sin recodificar, comparación exacta de fotogramas/PTS, permiso y presupuesto de una llamada SAM, recibo de respuesta e imposibilidad de reintento automático. Si existe resultado auténtico guardado, las cuatro vistas del reproductor recuperan máscaras/centroides SAM, vectores y rastro calculados en píxeles por ProofLab sin tokens adicionales. Las posiciones históricas FFmpeg se etiquetan NO SAM. Se probó localmente un MOV 1920×1080 HEVC de 1.828 frames: MP4 de 1.828 frames con diferencia PTS 0 s y costo teórico US$0,3656; **cobros Meta OFF, ninguna inferencia SAM nueva ni revisión humana completada**. [Etapa 3 caché ES](docs/SAM-VIDEO-CACHE-STAGE3.es.md).

- **Etapa 4, dos modos SAM y SQLite:** **API Meta** solicita un análisis nuevo sujeto a autorización; **API Meta + caché** consulta SQLite y reutiliza archivos verificados sin token ni llamada a Meta cuando hay HIT. Cada ejecución conserva su ID, respuesta y recibo; el reproductor recupera la evidencia mediante GET. Selector de dos modos y validación local con datos sintéticos; no se ejecutó SAM nuevo ni se validó la plomada. [Contrato y verificación ES](docs/SAM-TWO-MODES-SQLITE.es.md).

- API ASP.NET Core .NET 10 con predicción y evaluación científica independientes.
- Péndulo: predicción `T=2π√(L/g)` independiente de la medición; cálculo local mediante cruces ascendentes o estimador sinusoidal robusto. **No se utiliza el período previsto para ajustar las observaciones ni se certifica seguimiento físico sin revisión del video.**
- Esfera maciza rodando por una rampa: aceleración teórica
  `a=(5/7)g·sin(θ)` y aceleración medida mediante ajuste cuadrático de
  posiciones calibradas y tiempos.
- Etiquetas de procedencia para datos sintéticos, ingresados manualmente o
  procedentes de coordenadas de seguimiento.
- Interfaz Angular 20 en español e inglés, cálculos, demostraciones sintéticas
  y exportación de reportes JSON.
- Adaptador de Muse Spark 1.3 para planificación de hipótesis.
- Adaptador SAM 3.1 para segmentación de imagen y flujo de video con decodificador
  oficial y evidencias verificadas; probado con datos sintéticos. El seguimiento SAM
  del péndulo real aún **no** se ha validado.
- Las llamadas a Meta están **desactivadas por defecto** hasta comprobar créditos,
  facturación y condiciones de prueba.

## Flujo de desarrollo durante el hackathon

Los cambios se implementan y verifican en la PC autorizada. Se permite publicar commits en GitHub; los workflows de Actions se activan solo manualmente y únicamente cuando el propietario solicite CI. No ejecutar, reintentar ni esperar CI como requisito de cada cambio. Elegir comprobaciones locales según el alcance y registrar qué se verificó realmente.

## Requisitos

.NET SDK 10, Node.js 20 o superior y npm.

### Iniciar backend

```powershell
cd C:\PROOFLAB-AI-HACKATHON
dotnet test tests/ProofLab.Tests/ProofLab.Tests.csproj
dotnet run --project src/ProofLab.Api --urls http://127.0.0.1:5391
```

### Iniciar Angular (otra terminal)

```powershell
cd C:\PROOFLAB-AI-HACKATHON\ui
npm.cmd ci
npm.cmd start -- --host 127.0.0.1 --port 4300
```

Abrir `http://127.0.0.1:4300/` desde el mismo equipo.

### Rutas de la API

| Ruta | Finalidad |
| --- | --- |
| `GET /api/health` | Estado y bloqueo del uso de Meta |
| `GET /api/meta/readiness` | Estado de credencial y cuatro controles de pago, sin revelar token |
| `GET /api/research/stages` | Etapas del método científico y campos obligatorios |
| `POST /api/research` | Crear expediente de investigación matemática |
| `POST /api/research/{id}/stages/{n}/draft` | Guardar borrador de informe local |
| `POST /api/research/{id}/stages/{n}/muse-draft` | Propuesta de Muse Spark no verificada, bloqueada hasta verificar créditos y autorización |
| `POST /api/research/{id}/stages/{n}/approve` | Cerrar informe completo tras declarar revisión humana |
| `GET /api/research/{id}/reports/{n}` | Descargar informe de etapa (Markdown) |
| `GET /api/research/{id}/paper` | Descargar informe global al completar todo |
| `GET /api/math/problems` | Teoremas conocidos para calibrar el verificador |
| `GET /api/math/status` | Disponibilidad de Lean y etapa de investigación |
| `POST /api/math/verify` | Verificación Lean de un teorema curado y registro local |
| `POST /api/math/plan` | Muse Spark propone estrategias, bloqueado hasta verificar créditos |
| `GET /api/science/experiments` | Tipos de experimentos |
| `POST /api/science/predict` | Predicción física |
| `POST /api/science/evaluate` | Análisis de posiciones con marcas de tiempo |
| `GET /api/science/synthetic/{kind}` | Ejemplo sintético, nunca evidencia real |
| `POST /api/ai/plan` | Planificación de Muse Spark en vivo, desactivada inicialmente |
| `POST /api/ai/segment-frame` | Respuesta bruta de SAM 3.1, desactivada inicialmente |

Ejemplo:

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:5391/api/science/predict `
  -Method Post -ContentType 'application/json' `
  -Body '{"kind":"Pendulum","lengthMeters":0.5}'
```

La evaluación recibe `setup`, `observations` (lista de valores
`{"timeSeconds":0,"position":1.0}`) y `origin` (`Simulated`,
`ManuallyEntered` o `TrackingCoordinates`). Para el péndulo, la
posición es una coordenada horizontal consistente; para la esfera sobre
rampa, se mide en **metros sobre la pendiente**.

### Activación posterior de Meta API

El token se almacena únicamente como `MODEL_API_KEY` del **proceso backend**, nunca en el navegador o GitHub. El nuevo `GET /api/meta/readiness` muestra su presencia **sin revelar el secreto**. La conexión está desactivada por defecto y necesita simultáneamente clave, `Meta:CreditsConfirmedByOwner=true`, `Meta:OwnerApprovedPaidRequests=true` y `Meta:AllowPaidRequests=true`. Además hay un límite inicial de **2 solicitudes Meta reales por proceso**; esto no es un límite de dinero. El saldo no se comprueba automáticamente y la promoción no está confirmada. La interfaz puede proponer borradores con Muse Spark únicamente tras todos los controles, sin aprobarlos.

**Pasos para obtener y colocar el token de manera segura:** [Conexión Meta Model API ES](docs/META-API-CONNECTION.es.md).

**Pruebas manuales con dos modelos:** en la barra lateral elige **«Pruebas Meta API»**. Para SAM 3.1, la interfaz ahora interpreta la máscara base85 y dibuja su contorno y caja sobre la imagen; también permite pegar el JSON completo de una prueba anterior para decodificarlo sin enviar una nueva solicitud a Meta. Se usa `@meta-sam/parser@0.1.0` bajo la licencia SAM de Meta, copiada en `ui/third-party/META-SAM-LICENSE.txt`. El operador puede abrir una sesión temporal y confirmada en `5393` mediante `scripts/Start-MetaTest.cmd`, realizar **una solicitud por clic** a Muse Spark o SAM, y ver códigos HTTP sin exponer el token. La API principal en `5391` sigue con los pagos desactivados. [Guía ES](docs/META-MANUAL-TESTS.es.md). Las alertas por correo no son topes rígidos; no habilitar gastos hasta verificar Billing y tener aprobación explícita.

Durante las pruebas de implementación no hicimos llamadas a Meta.

## Próximos pasos

**Secuencia de misiones:** (1) Descubrimiento matemático → (2) Descubrimiento físico autónomo → (3) Descubrimiento histórico y de grandes datos → (4) Director autónomo de investigación. Compartirán un registro de evidencias verificables; por ahora solo se ha implementado la primera etapa de la Misión 1.


1. Para la demo principal, grabar **un video de 50 cm** y, opcionalmente, otro de longitud distinta después de registrar una predicción. Las tres repeticiones por longitud son **controles avanzados opcionales**. Cronometrar manualmente como contraste independiente.
2. Integrar subida de video a SAM 3.1 y decodificación de máscaras por fotograma.
3. Convertir esas máscaras en observaciones calibradas con marcas de tiempo.
4. Planificación e interpretación de resultados reales mediante Muse Spark.
5. Añadir grabaciones reales de la esfera en una rampa.
6. Publicar la demo, grabar un video en inglés de tres minutos y entregar antes
   del 4 de noviembre de 2026 a las 9 p. m., hora de Perú.

Documentación: [experimentos](docs/EXPERIMENTS.es.md) ·
[experiments](docs/EXPERIMENTS.md) ·
[procedencia](docs/PROVENANCE.es.md) ·
[provenance](docs/PROVENANCE.md).

**English version:** [README.md](README.md).
