# Etapa 5 — investigador Muse sobre evidencia SAM

El Laboratorio ofrece una bitácora bajo el período observado. **Preparar evidencia** es local: el servidor verifica el archivo y la respuesta SAM guardada y ejecuta el mismo estimador TypeScript que usa la pantalla. Requiere Node, `ui/node_modules` instalado con `npm ci` y el worker generado automáticamente al compilar la API. El worker no recibe credenciales ni ejecuta código del modelo.

**Consultar Muse** envía únicamente un resumen de mediciones, cobertura, huecos, incertidumbre estadística, parámetros declarados y límites. No vuelve a subir el video. Usa `muse-spark-1.3`, JSON Schema estricto, una consulta de tokens y como máximo una inferencia. Ambas solicitudes HTTP cuentan contra el cupo de llamadas autorizado. Sin reintentos automáticos; el presupuesto estimado es US$0,05. Conserva los controles de crédito, token y cupo del servidor.

Muse puede proponer un experimento condicional, pedir repetir la grabación o declarar un resultado inconcluso. Una propuesta no certifica evidencia ni constituye un descubrimiento. La app verifica independientemente:

- Evidencia con período candidato y nuevo largo entre 0,10 y 2 m, al menos 15% diferente.
- T₂ = T₁ observado × √(L₂/L₁). La banda práctica de contraste se fija entre 1% y 10%; no es un intervalo de confianza.
- Respuesta bilingüe con justificación, límites, hipótesis falsable y protocolo.
- Identidad de video, resultado SAM y pivote vigentes antes y después de la inferencia.

## Persistencia y orden

La base privada `<ContentRoot>/.private/physics/muse-experiments.sqlite` guarda observaciones inmutables, intentos con respuesta cruda y predicciones inmutables. Se conservan ID/modelo/uso del proveedor, hash de la respuesta, hash del payload y fecha. Un intento fallido o ambiguo queda bloqueado hasta reconciliación; refrescar la pantalla no repite el cobro.

La escritura del resultado y la predicción es transaccional. Un bloqueo por investigación impide subir el video 2 durante la inferencia. La API de subida exige una predicción Muse persistida y rechaza el mismo SHA del video 1. Los registros antiguos siguen siendo legibles; una propuesta local antigua no desbloquea una subida nueva.

El hash comprueba integridad local. No es firma de terceros ni prueba de cuándo se filmó el segundo video. La longitud siguiente debe medirse desde la suspensión hasta el centro visible; este centro no es una medición independiente del centro de masa.

## API local

Bajo `/api/science/director/pendulum/sessions/{id}/muse`:
- GET: estado y resultado guardados.
- POST `/prepare`: reconstrucción gratuita desde evidencia auténtica.
- POST `/evaluate`: acción explícita con consentimiento y límite de costo.
- GET `/report`: acta reproducible JSON para descargar.

Escrituras solo desde loopback, origen local 4300 y cabecera `X-ProofLab-Manual-Action: muse-research`. El token se usa como credencial de la solicitud; nunca se guarda en SQLite ni en el informe.

## Validación y límite actual

Pruebas locales con transporte HTTP sintético comprueban rechazo de predicciones incompatibles, JSON estricto, costos, ausencia de reintentos, inmutabilidad, transacción y bloqueo concurrente. Estos fixtures no son respuestas reales de Muse.

Para aprobar científicamente esta etapa falta registrar una respuesta real. Si Muse pide repetir o declara inconcluso, no se inventa ni congela una predicción. La etapa 6 —contrastar SAM del segundo video, incertidumbre entre longitudes y conclusiones reproducibles— requiere trabajo adicional; esta entrega no sustituye ese análisis con la comparación local histórica.

Fuentes de integración: [modelos](https://dev.meta.ai/docs/models), [salida estructurada](https://dev.meta.ai/docs/structured-output), [precios](https://dev.meta.ai/docs/pricing-rate-limits). El costo por tokens es una estimación, no una factura.
