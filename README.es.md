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

- **Demo estrella · Péndulo (primer incremento sin Meta):** registra la predicción 50→80 cm con fecha/huella local y permite comparar dos series CSV sin fingir video real. La integración automática SAM→video, Muse y el director siguen pendientes. [Plan y evidencia ES](docs/FLAGSHIP-DEMO.es.md).

- API ASP.NET Core .NET 10 con predicción y evaluación científica independientes.
- Péndulo: fórmula `T=2π√(L/g)` y medición del periodo mediante cruces
  ascendentes del centro observados, **sin forzar el resultado teórico**.
- Esfera maciza rodando por una rampa: aceleración teórica
  `a=(5/7)g·sin(θ)` y aceleración medida mediante ajuste cuadrático de
  posiciones calibradas y tiempos.
- Etiquetas de procedencia para datos sintéticos, ingresados manualmente o
  procedentes de coordenadas de seguimiento.
- Interfaz Angular 20 en español e inglés, cálculos, demostraciones sintéticas
  y exportación de reportes JSON.
- Adaptador de Muse Spark 1.3 para planificación de hipótesis.
- Adaptador SAM 3.1 para segmentación de imagen y decodificador local de máscaras
  mediante el parser oficial; probado con datos sintéticos. Aún **no** hay seguimiento de video.
- Las llamadas a Meta están **desactivadas por defecto** hasta comprobar créditos,
  facturación y condiciones de prueba.

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


1. Grabar 3 videos con péndulo de 50 cm y 3 de 80 cm; cronometrar manualmente.
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
