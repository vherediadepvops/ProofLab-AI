# Etapa 5 — investigador Muse sobre evidencia SAM

El protocolo V3 añade una clasificación explícita emitida por Muse Spark: PENDULUM_COMPATIBLE, OTHER_MOTION o INCONCLUSIVE, con justificación ES/EN. ProofLab aporta métricas y hasta 48 centroides observados con PTS; no presenta el diagnóstico geométrico local como veredicto. SAM sin detecciones produce evidencia insuficiente. La misma consulta de Muse interpreta esos datos y propone o rechaza el siguiente experimento; no hay otra inferencia automática. Las respuestas V1/V2 conservan su formato y hash, sin inventarles una clasificación. Véase [distribución de responsabilidades](SAM-PENDULUM-GENERIC.es.md).

El Laboratorio ofrece una bitácora bajo el período observado. **Preparar evidencia** es local: el servidor verifica el archivo y la respuesta SAM guardada y ejecuta el mismo estimador TypeScript que usa la pantalla. Requiere Node, `ui/node_modules` instalado con `npm ci` y el worker generado automáticamente al compilar la API. El worker no recibe credenciales ni ejecuta código del modelo.

**Consultar Muse** envía únicamente un resumen de mediciones, cobertura, huecos, incertidumbre estadística, parámetros declarados y límites. No vuelve a subir el video. Usa `muse-spark-1.3`, JSON Schema estricto, una consulta de tokens y como máximo una inferencia. Ambas solicitudes HTTP se incluyen en el registro de ejecución. Sin reintentos automáticos; el presupuesto estimado es US$0,05. Conserva los controles de crédito, token y costo por solicitud.

El protocolo V2 envía opciones numéricas calculadas por ProofLab; Muse elige y justifica una, y el esquema restringe los valores a esas opciones. El servidor vuelve a comprobar que longitud y período correspondan entre sí. La versión del protocolo forma parte de la identidad de la observación; los intentos anteriores se conservan.

Muse puede proponer un experimento condicional, pedir repetir la grabación o declarar un resultado inconcluso. Una propuesta no certifica evidencia ni constituye un descubrimiento. La app verifica independientemente:

- Evidencia con período candidato y nuevo largo entre 0,10 y 2 m, al menos 15% diferente.
- T₂ = T₁ observado × √(L₂/L₁). La banda práctica de contraste se fija entre 1% y 10%; no es un intervalo de confianza.
- Respuesta bilingüe con justificación, límites, hipótesis falsable y protocolo.
- Identidad de video, resultado SAM y pivote vigentes antes y después de la inferencia.

## Persistencia y orden

La base privada `<ContentRoot>/.private/physics/muse-experiments.sqlite` guarda observaciones inmutables, intentos con respuesta cruda y predicciones inmutables. Se conservan ID/modelo/uso del proveedor, hash de la respuesta, hash del payload y fecha. Un intento fallido o ambiguo queda bloqueado hasta reconciliación; refrescar la pantalla no repite el cobro.

La escritura del resultado y la predicción es transaccional. Un bloqueo por investigación impide subir el video 2 durante la inferencia. La API de subida exige una predicción Muse persistida y rechaza el mismo SHA del video 1. Los registros antiguos siguen siendo legibles; una propuesta local antigua no desbloquea una subida nueva.

El hash comprueba integridad local. No es firma de terceros ni prueba de cuándo se filmó el segundo video. La longitud siguiente debe medirse desde la suspensión hasta el centro visible; este centro no es una medición independiente del centro de masa.

## Cupo del servidor

Por petición del usuario, `Meta:UnlimitedManualRequests=true` permite envíos manuales sin cupo por sesión. Cada clic requiere token válido y autoriza el costo mostrado; no inicia reintentos automáticos. El contador conserva las inferencias admitidas, incluso por encima del límite de procesos, y se restaura con `Meta:AlreadyUsedRequests`. Las rutas sin token manual conservan `Meta:MaximumRequestsPerProcess` (predeterminado 2). El contador no es una factura ni cuenta solicitudes HTTP individuales.

## API local

Bajo `/api/science/director/pendulum/sessions/{id}/muse`:
- GET: estado y resultado guardados.
- POST `/prepare`: reconstrucción gratuita desde evidencia auténtica.
- POST `/evaluate`: acción explícita con consentimiento y límite de costo.
- GET `/report`: acta reproducible JSON para descargar.

Escrituras solo desde loopback, origen local 4300 y cabecera `X-ProofLab-Manual-Action: muse-research`. El token se usa como credencial de la solicitud; nunca se guarda en SQLite ni en el informe.

## Validación histórica del 28/10/2026

Pruebas locales con transporte HTTP sintético comprueban rechazo de predicciones incompatibles, JSON estricto, costos, ausencia de reintentos, inmutabilidad, transacción y bloqueo concurrente. Estos fixtures no son respuestas reales de Muse.

La prueba real del 28/10/2026 registró una respuesta de Muse con hipótesis, justificación y protocolo ES/EN: variar de 0,50 a 0,80 m, período predicho 1,7909424812 s y banda práctica ±5%. Quedó congelada antes de subir el segundo video y se conservó tras reiniciar la API. La primera respuesta contenía un período incompatible y fue rechazada; se conserva íntegra. Se usaron dos inferencias y dos consultas de tokens, con costo total estimado US$0,0239455. Volver a consultar el resultado no consumió cupo. Esto valida la integración; aún no contrasta la hipótesis contra un segundo experimento. Si Muse pide repetir o declara inconcluso, no se inventa ni congela una predicción. La etapa 6 —contrastar SAM del segundo video, incertidumbre entre longitudes y conclusiones reproducibles— requiere trabajo adicional; esta entrega no sustituye ese análisis con la comparación local histórica.

Fuentes de integración: [modelos](https://dev.meta.ai/docs/models), [salida estructurada](https://dev.meta.ai/docs/structured-output), [precios](https://dev.meta.ai/docs/pricing-rate-limits). El costo por tokens es una estimación, no una factura.

## Contraste del segundo video — 30/10/2026

La bitácora existente también acepta evidencia de lotes SAM segmentados. La petición incluye adaptiveBatchId; el servidor comprueba el SHA original, cada recibo y los empalmes antes de medir. Una longitud siguiente elegida manualmente se respeta; Muse no se atribuye haberla elegido. El segundo video usa su propio seguimiento autenticado: no reutiliza la trayectoria local histórica.

Tras congelar la predicción y cargar el segundo video, Verificar contraste prepara localmente sus medidas, longitud declarada y banda congelada. Consultar Muse envía este resumen a Meta en una inferencia manual, máximo estimado US$0,05, sin contar tokens previamente ni repetir automáticamente. El resultado distingue compatibilidad dentro de la banda, discrepancia fuera de la banda o inconcluso. Muse puede declarar inconcluso, pero no invertir el contraste aritmético ni cambiar retrospectivamente la predicción. No se atribuyen diferencias a fricción sin evidencia.

Rutas adicionales: GET /contrast, POST /contrast/prepare y POST /contrast/evaluate, bajo la misma base y protección de origen local. Los archivos privados .private/physics/muse-contrasts/{id}/ conservan evidencia, intento, respuesta original y resultado en registros nuevos sin sobrescritura. La predicción sigue en SQLite. Una respuesta ya registrada se recupera sin otra inferencia; cambiar video, análisis o longitud de un contraste guardado se rechaza. Un nuevo experimento requiere un estudio nuevo.

Las grabaciones ya existentes permiten una comparación retrospectiva. Subirlas después de congelar el JSON acredita el orden dentro de la aplicación, no una adquisición prospectiva ciega. Los controles sintéticos con prefijo synthetic- se identifican como validación de software; este indicador no es un detector de videos sintéticos. La longitud sigue siendo declarada, y la incertidumbre estadística no incluye errores de cámara, pivote ni calibración. Una conclusión compatible no demuestra una ley ni un descubrimiento.

Esta actualización completa el contraste SAM→Muse en la bitácora; no añade nuevas secciones ni sustituye la calibración/validación científica pendiente. Las ejecuciones y límites de la campaña de diez casos quedan en el registro privado de la tanda y la memoria compartida.


## Validación real del flujo completo — 2026-10-30

Diez casos autorizados y terminados: ocho comparaciones de seis grabaciones preexistentes de 50/80 cm (tres pares principales y cinco cruces), un control negativo con una grabación de 50 cm declarada como 80 cm y un control sintético de período conocido. Los cruces reutilizaron SAM por SHA exacto; cada caso consultó nuevamente a Muse para la hipótesis y para el contraste. No son diez experimentos físicos independientes.

Resultados: siete dentro de la banda predeclarada (uno sintético), dos inconclusos por suspensión insuficientemente localizada en 80CM_2, y el control negativo fuera de banda (-20,97%). En los diez casos la predicción y su hash precedieron a la carga del segundo video. Se comprobaron el rechazo de cambios de fuente/longitud y la lectura idempotente sin otra inferencia.

Se registraron 102 inferencias en total, incluidas dos recuperaciones continuas tras empalmes SAM inconsistentes (80CM_1 y el primer sintético). La suma de presupuestos estimados fue US$ 7,1816; no es factura ni gasto confirmado. Los datos, videos y recibos completos permanecen privados.

El control sintético dio períodos de 1,419011 s y 1,793957 s frente a valores conocidos de 1,418746 s y 1,794587 s (errores +0,019% y -0,035%). Esto NO valida precisión angular: Spark desplazó el pivote y SAM perdió 13,4% de fotogramas del primer control. La prioridad pendiente es verificar el pivote contra la imagen y los huecos de seguimiento; no flexibilizar validaciones para mostrar valores.

Verificación: 401 pruebas .NET, 46 Angular y build de producción correctos (advertencia de bundle existente). En Edge aislado se comprobó reproducción H.264 original, θ/ω variables, huecos sin valores inventados, contraste ES/EN y tarjeta sin desbordamiento a 390 px. Los originales HEVC se decodificaron como audio sin imagen en esos perfiles; esta limitación no equivale a fallo en todos los navegadores. Sin CI. Permisos y contador del servicio propietario conservados.
