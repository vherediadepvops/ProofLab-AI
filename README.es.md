# ProofLab AI — Hackathon Meta 2026

**Edición de competencia — creada el 26 de octubre de 2026 después de la inauguración oficial.**

ProofLab AI es un **motor de evidencia científica** para investigadores adultos,
desarrollado para la temática *Push the Frontier* del Meta Global AI Developer Hackathon.
Combina mediciones físicas con modelos de Meta para formular hipótesis comprobables,
predecir resultados y explicar diferencias entre la teoría y las observaciones.

**Estado: alfa.** La inferencia real con Meta y la interpretación completa de máscaras
de SAM **aún no han sido verificadas**. Los cálculos y ejemplos funcionan localmente.

## Procedencia del código competitivo

Este es un proyecto **nuevo**, independiente del prototipo privado anterior
`C:\PROOFLAB-AI`. No se copiaron archivos fuente anteriores a la inauguración.
El historial Git registra trabajos realizados después del kickoff del 26 de octubre,
de acuerdo con la respuesta oficial de Cerebral Valley en Discord que permite empezar
a desarrollar inmediatamente después de la inauguración.

La entrega final deberá ser pública, original y distinguir qué funcionalidades
se desarrollaron durante el evento. No presentaremos simulaciones como datos reales.

## Funcionalidades actuales

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
- Adaptador SAM 3.1 para segmentación de una imagen; por ahora devuelve
  **una respuesta sin decodificar**, no máscaras interpretadas ni seguimiento de video.
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

La clave se lee de `MODEL_API_KEY` exclusivamente en el backend y las
llamadas pagadas solo se permiten con `Meta:AllowPaidRequests=true`.
En PowerShell se pueden usar las variables de proceso `$env:MODEL_API_KEY`
y `$env:Meta__AllowPaidRequests='true'` **solo después** de verificar
el saldo promocional en Billing y comprender los posibles cobros posteriores.
No se guardarán secretos en Git ni en Angular. Las alertas de gasto por correo
no son límites rígidos.

Durante las pruebas de implementación no hicimos llamadas a Meta.

## Próximos pasos

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
