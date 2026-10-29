# SAM 3.1 — análisis dinámico real, integración experimental

> **Permisos vigentes (29/10/2026):** las referencias a pagos apagados en este documento describen el ensayo o estado histórico de esa instancia. No son instrucciones para desactivar la API del propietario ni las ramas nuevas. Aplicar la [política actual de autorización y pruebas](META-API-CONNECTION.es.md).

**Rama:** `feature/sam-adaptive-live` (derivada del `main` público `ced3849`). No se ha fusionado a `main`; los servicios del propietario siguen intactos. Solo ejecución y pruebas en `DESKTOP-ESGSLJH`, `C:\PROOFLAB-AI-HACKATHON`.

## Objetivo

Spark localiza el objeto **una sola vez en el video completo** y genera una escena verificable. SAM procesa **N fragmentos de ese mismo video**, en streams independientes; el backend preserva recibos, máscaras y PTS reales de cada fragmento, detecta huecos/contradicciones y no registra la composición como una respuesta nativa de un solo proveedor.

### Limitación descubierta: referencias Spark insuficientes

La propuesta dinámica básica busca ~53 cuadros centrales por petición. Sin embargo, 50 cuadros es un **mínimo de facturación**, no una prueba de que un objeto pueda rastrearse con precisión sin referencia. Nuestra grabación candidata de 667 cuadros tiene solamente **tres semillas auténticas Spark, en los cuadros 67, 333 y 599**. El plan nominal de **13** fragmentos dejaría varios sin ninguna localización verificada.

`SamAdaptiveGroundedPlan` calcula el plan de mayor número de partes cuya ventana contenga por lo menos **una caja auténtica Spark en un cuadro del propio fragmento**. Para esta grabación son **4** partes, con un máximo de 4 solicitudes simultáneas. No se interpolan o extrapolan cajas para inventar semillas. Los índices de prompt se convierten del índice original al local mediante `FrameOriginal - InicioDelFragmento`. Ante un fragmento sin semilla, **se bloquea todo el envío** antes de consumir créditos.

## Preparación real y verificación local

Para el video privado de 667 cuadros: **4 MP4 HEVC H.265**, creados a partir de un intermedio con fotograma clave cada dos cuadros. Cuadros por parte: **168, 171, 172, 169**; con solapamiento se enviarían **680 cuadros**, por un costo estimado de SAM de **US$0,1360**, sin gasto real, Spark reutilizado.

La preparación y las verificaciones duraron **30,241 segundos**. Los **680 cuadros decodificados coincidieron bit a bit** con sus originales, con error máximo de PTS observado de **0,001 ms**; se comprobó además que el origen y las partes tienen la misma resolución y orientación en la muestra usada (1920×1080, rotación 0). La producción verifica codificación, PTS, hashes de píxeles, SHA del video, número de cuadros y metadatos de orientación antes de permitir subir cualquier parte. El MP4 intermedio y los cuatro archivos permanecen en `.private`, sin publicarse.

La documentación de Meta permite MP4 y MOV con H.264, H.265 o MPEG-4: https://dev.meta.ai/docs/sam/overview.

## Seguridad y facturación

- El backend admite **máximo 3 lotes de prueba** explícitamente autorizados por el propietario y no crea pruebas automáticas. Cada lote puede contener varias solicitudes SAM, a lo sumo **4 en paralelo**. Presupuesto agregado máximo **US$0,50 estimados por lote**, con solapamientos; no garantiza factura exacta ni velocidad.
- El frontend tiene opción **API Meta · segmentos** en el selector ya existente. El token se mantiene solo en el campo password de la sesión. El POST necesita origen local, cabecera explícita y todas las puertas del backend (créditos, consentimiento, clave, presupuesto).
- Cada parte tiene marcadores privados inmutables de preparación, reserva y posible despacho; recibo y respuesta SHA independientes. Cuando una parte falla, no se admiten nuevas; las iniciadas se dejan terminar y se conservan. **Sin reintentos automáticos.**
- La composición de máscaras exige que las mismas máscaras del solapamiento coincidan. Si difieren, el resultado se declara **inconcluso**, sin rellenar máscaras ni publicarlo como prueba científica. La validación con máscaras realmente distintas y la comparación raster IoU requieren investigación posterior. Los datos derivados no se introducen como una respuesta nativa en la caché SQLite anterior.
- **Bloqueo activo:** una operación POST potencialmente facturable fue rechazada por la seguridad de la herramienta remota, aun con pagos desactivados. No se repitió por otra vía. La API de pruebas conserva `Meta:AllowPaidRequests=false`. **Se han consumido 0 de 3 lotes reales y 0 llamadas Meta por esta integración.** Resolver el rechazo por el procedimiento autorizado antes de probar el proveedor.

## Aplicación en puertos independientes

- Web experimental: `http://127.0.0.1:4327/`.
- API experimental: `http://127.0.0.1:5410`.
- Datos de prueba privados: video de 667 cuadros con escena Spark archivada y verificada. `demo-video.json` es un archivo local ignorado por Git.
- Estado comprobado por GET: **4 fragmentos**, 3 semillas Spark, 680 cuadros, US$0,1360, tres pruebas restantes, **envío pagado deshabilitado**.
- Los puertos ajenos **5391, 5393, 4300** no se han modificado.

## Verificación local y pendiente

**347/347 pruebas .NET**, **64/64 Angular focalizadas** y builds de API y Angular correctos. La suite Angular completa había tenido 1 fallo preexistente de expectativa textual, ajeno a la inferencia; no se corrigió fuera de alcance. No se ejecutó CI.

Pendiente: permiso/procedimiento de la herramienta para el POST, ejecutar y medir hasta 3 lotes reales, cotejar duración total y calidad frente a SAM monolítico y revisar máscaras independientes. **No fusionar esta rama a main hasta obtener esa evidencia y autorización.**
