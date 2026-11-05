# ProofLab AI — Convierte el mundo real en un laboratorio científico

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier · Desarrollado en Perú 🇵🇪**

Transforma un video de un experimento común en mediciones trazables del movimiento, una predicción comprobable y una comparación con una segunda grabación. **ProofLab AI es un prototipo de investigación funcional**, no una afirmación de haber descubierto una ley física ni de disponer de precisión certificada independientemente.

**[Abrir el laboratorio](https://prooflab-ai.com/)** · **[Ver la demostración en español (3 min)](https://youtu.be/D1O1ZFvc0q0)** · [English](README.md) · [Resultados públicos](docs/RESULTS.es.md) · [Protocolo de evaluación](docs/EVALUATION.es.md)

## Demostración de tres minutos en tu idioma 🌎

| Idioma | Video |
| --- | --- |
| Español | [Ver en YouTube](https://youtu.be/D1O1ZFvc0q0) |
| English | [Watch on YouTube](https://youtu.be/75i78EO8xZo) |
| Português | [Assistir no YouTube](https://youtu.be/EXuD3RJayIk) |
| 简体中文 | [观看演示](https://youtu.be/quHyAHKxzZ8) |

Estos enlaces son versiones de la **demostración**. La interfaz y los informes científicos de la aplicación están disponibles en **español e inglés**, no en los cuatro idiomas.

## Del video a un experimento falsable

1. **Preparar:** se carga el primer video del péndulo y se ingresan la longitud medida de la cuerda, el ángulo inicial y la masa. Se conserva la grabación original, su identidad y sus tiempos.
2. **Analizar:** **Meta Muse Spark 1.3** localiza el objeto experimental y revisa referencias visuales; **SAM 3.1** lo segmenta y sigue durante la grabación.
3. **Entender:** el **motor de medición determinista** de ProofLab utiliza la geometría seguida y los tiempos originales para estimar período observado, posición angular y velocidad angular. Muse Spark interpreta la evidencia y sus límites.
4. **Predecir:** el sistema valida y **congela** una predicción numérica y su margen práctico *antes de analizar el segundo video*. La persona confirma la propuesta experimental.
5. **Comparar:** el segundo video se procesa de forma independiente y su período observado se contrasta con la predicción congelada. El resultado puede estar **dentro del margen, fuera del margen o ser inconcluso**.

El estimador **no utiliza el período predicho para ajustar el observado**. Se diferencian las detecciones de SAM, las mediciones calculadas, las interpretaciones de Muse, las simulaciones y la revisión humana.

## Resultados experimentales — actualizados al 4 de noviembre de 2026

Una **consulta de solo lectura a registros guardados en la aplicación de producción** confirmó 25 flujos de contraste entre dos videos. Los 25 contenían las identidades de ambos videos, una predicción Muse congelada y una evaluación almacenada:

| Resultado registrado | Casos |
| --- | ---: |
| Dentro de la banda práctica predeclarada de ±5 % | **18** |
| Fuera de la banda | **6** |
| Inconcluso | **1** |
| **Total de contrastes almacenados** | **25** |

Estos contrastes reutilizaron **ocho videos ya grabados** con etiquetas nominales de 10 a 80 cm; **no son 25 experimentos físicos independientes ni grabados prospectivamente**, y 18/25 **no representa una tasa de exactitud científica**. En un caso fuera de banda, la longitud inicial declarada no coincidía con la etiqueta del video. El caso inconcluso tenía solo 3,07 % de cobertura de seguimiento del segundo video. Recuperar los datos no generó nuevas inferencias Meta.

Una campaña independiente en fecha, del **30 de octubre**, registró **10 comparaciones**: 7 dentro de sus bandas prácticas (incluido un control sintético), 2 inconclusas y 1 control negativo de longitud intencionalmente incorrecta fuera. Esta campaña anterior **no agrega filmaciones físicas independientes**. Las solicitudes al proveedor y las pruebas automatizadas se informan por separado, sin duplicar conteos.

**[Ver los 25 resultados por pareja, fallos y límites](docs/RESULTS.es.md)** · [Evidencias de la entrega](docs/SUBMISSION-EVIDENCE-2026-11-04.es.md) · [Evaluación y reproducibilidad](docs/EVALUATION.es.md)

**Paquete público reproducible:** [Catálogo anonimizado de 25 casos en CSV/JSON y generador SQLite sin conexión](demo-data/README.es.md). Permite al jurado consultar resultados numéricos almacenados sin códigos privados; no reproduce las inferencias originales de Meta.

**Para el jurado: [guía de instalación Windows y Linux](JUDGE-SETUP.es.md)** · [English setup guide](JUDGE-SETUP.md). Incluye instalación limpia, arranque de API/web, comprobaciones sin Meta y generación de la SQLite de 25 casos por separado.

## Cómo utilizamos los modelos de Meta

| Componente | Responsabilidad |
| --- | --- |
| **Meta Muse Spark 1.3** | Interpretación multimodal de escena y objeto, valoración de evidencia, planificación de hipótesis y seguimiento experimental e interpretación del contraste numérico |
| **Meta SAM 3.1** | Máscaras de segmentación y seguimiento del objeto sobre los fotogramas del video original |
| **Motor de medición ProofLab** | Trayectoria y período usando tiempos originales, controles de calidad, indicadores de incertidumbre e informes reproducibles; independiente de números inventados por un modelo |
| **ASP.NET Core (.NET 10), Angular 20, SQLite y FFmpeg** | Flujo experimental, predicción inmutable, consentimiento y control de costo, integridad de archivos, persistencia de evidencias e interfaz ES/EN |

El flujo documentado del **3 de noviembre** prepara un MP4 completo y 50 referencias de fotogramas originales, realiza una revisión del objeto con Spark y envía el video completo a SAM en una solicitud continua. El máximo estimado para **dos llamadas Spark es US$ 0,35** en ese plan; **no es una factura ni una nueva prueba de exactitud**. Los resultados históricos segmentados continúan disponibles.

Las solicitudes manuales a Meta requieren token de API, acción explícita, aceptación del costo mostrado y controles de autorización del backend. El token permanece únicamente en el campo de contraseña durante la sesión y se elimina al pulsar Borrar, cambiar de conexión o recargar; **no se borra después de cada solicitud correcta**. Los fallos de solicitudes pagadas no se reintentan automáticamente.

[Flujo Muse/SAM detallado](docs/MUSE-INVESTIGATOR.es.md) · [Estimador del período](docs/SAM-PERIOD.es.md) · [Operación en producción](docs/PRODUCTION-LINUX.md)

## Integridad científica y límites conocidos

- La **banda práctica de ±5 % no es un intervalo estadístico de confianza del 95 %** ni certifica exactitud de las mediciones.
- Longitud efectiva de la cuerda, pivote físico, perspectiva de cámara e incertidumbre sistemática necesitan **calibración independiente**. La repetibilidad estadística no cubre esos errores.
- La fecha de congelamiento de la predicción prueba el orden de acciones **dentro de la aplicación**, no que la segunda grabación física se haya filmado después.
- Las respuestas crudas del proveedor, videos privados, bases de datos y códigos de recuperación **no se publican**. Los métodos y pruebas públicas se pueden revisar, pero no es posible reproducir íntegramente todas las ejecuciones originales del proveedor solo con esos archivos.
- Los últimos cambios del procesamiento cuentan con verificaciones de software/navegador; esto **no sustituye** una nueva validación científica con el proveedor real.

Se diferencia una medición observada, una interpretación generada, un control sintético, un teorema conocido y una hipótesis. **No afirmamos descubrimiento científico autónomo ni precisión física universal.**

[Protocolo experimental](docs/EVALUATION.es.md) · [Resultados y fallos](docs/RESULTS.es.md) · [Procedencia del experimento](docs/PROVENANCE.es.md)

## Código, documentación y desarrollo local

**Código del proyecto bajo [licencia MIT](LICENSE).** Los componentes externos conservan sus propias condiciones, incluida la [licencia del parser SAM de Meta](ui/third-party/META-SAM-LICENSE.txt).

**Requisitos:** SDK .NET 10, Node.js y npm, además de FFmpeg/FFprobe para procesar videos. Para trabajar localmente, instala primero las dependencias Angular antes de compilar el worker de mediciones del backend:

```powershell
cd ui
npm ci
cd ..
dotnet test tests/ProofLab.Tests/ProofLab.Tests.csproj
```

El laboratorio utiliza API ASP.NET Core e interfaz Angular. Los puertos de desarrollo habituales son **5391** y **4300**; **no inicies otra instancia en puertos ocupados**. Usa puertos libres y la [guía de pruebas aisladas en Edge](docs/EDGE-TESTING.es.md) si convives con otras sesiones. Las pruebas de software sin conexión **no necesitan llamar a Meta**.

Más documentación: [Método científico](docs/SCIENTIFIC-METHOD.es.md) · [Experimentos físicos](docs/EXPERIMENTS.es.md) · [Política de idioma](docs/LOCALIZATION.es.md) · [Diseño histórico del reproductor](docs/PENDULUM-ZONE1-MASTER-PLAYER.es.md) · [Investigación matemática/Lean anterior](docs/MISSION-001-MATH.es.md). Son módulos históricos o paralelos **fechados**, no afirmaciones de que todas esas funciones estén activas en la demostración actual del péndulo.

**Procedencia de competencia:** el código público se creó después de la inauguración del **26 de octubre de 2026** y es independiente del prototipo privado previo. Consulta [procedencia](docs/PROVENANCE.es.md).

**Apoya el proyecto:** [demostración de ProofLab AI en X](https://x.com/vherediaE/status/2118103090998260005).
