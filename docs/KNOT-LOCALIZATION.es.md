# Localización del nudo con Meta

ProofLab propone el punto donde la cuerda libre sale del soporte usando Muse Spark 1.3. Es una inferencia visual candidata, distinta de las máscaras SAM de la plomada. Requiere revisión humana antes de modificar el pivote, θ o ω.

## Flujo
1. Tras procesar el video, abrir **Localizar nudo con Meta**.
2. Una comprobación local busca evidencia por SHA-256 del original, modelo y protocolo. No transmite imágenes ni consume créditos.
3. Sin caché: configurar el token de un uso, aceptar el envío de tres imágenes y el presupuesto máximo **estimado de US$0,05**, y pulsar **Detectar nudo**.
4. El servidor extrae los fotogramas originales al 10 %, 50 % y 90 % del índice de fotogramas. FFmpeg conserva resolución y aplica la orientación del original; JPEG de análisis, sin reducir FPS ni cambiar el video de reproducción.
5. Una única consulta de Muse devuelve tres propuestas normalizadas 0–1000. No hay herramientas, bucles ni reintentos automáticos.
6. Si son claras y coherentes, revisar tres ampliaciones. Ajustar mediante clic o flechas (un píxel de origen), confirmar la revisión y **Usar este punto**. La revisión del pivote conserva la referencia a la evidencia Meta. La posición anterior permanece en el historial.

Un punto oculto, confianza declarada menor a 0,8, coordenadas inválidas o discrepancia mayor al máximo de 3 px y 0,5 % del lado corto impide aplicar la propuesta. La confianza del modelo no es una probabilidad calibrada. Se mantiene la selección manual; no se certifican unidades físicas, cámara frontal ni exactitud científica. Un pivote móvil requiere otro método.

## Gasto y procedencia
- Tarifa de referencia estándar Muse Spark: US$1,25/M entrada y US$4,25/M salida, consultada el 28/10/2026.
- El tope presentado es un **presupuesto estimado**, no una cotización exacta ni un límite de facturación garantizado por Meta.
- Antes de generar, el transporte consulta `responses/input_tokens` con las mismas imágenes. Es un control conservador de tamaño, no uso facturado. Rechaza más de 32768 tokens de contexto; generación limitada a 2048 tokens, sin herramientas/historial. El importe real depende del campo `usage` de la respuesta y del proveedor.
- Transferencia, crédito, autorización y cupo se comprueban en backend. Comparte el cupo de dos solicitudes de la sesión con SAM; el token no se almacena. El conteo fallido consume conservadoramente una reserva, y cualquier intento incompleto queda bloqueado para revisión manual.
- Imágenes, respuesta y recibo permanecen en `.private/physics/knot/`. SHA originales, imágenes y respuesta se verifican al leer caché. No se reutiliza el índice SQLite de videos SAM porque representa otra evidencia.
- `Meta:AlreadyUsedRequests` permite conservar el cupo usado al reemplazar el proceso; no habilita pagos. El operador debe llevar el contador actual (0–2), conservar los gates y no usar reinicios para renovar autorizaciones.
- El punto se transforma a la cuadrícula persistida 480×270; el reproductor calcula la geometría con las proporciones originales. Una confirmación humana es revisión visual, no validación científica independiente.

## Validación
Pruebas locales de coordenadas/rotación, ambigüedad, límites, autorización, caché e integridad, bloqueo de reintentos, concurrencia, transporte HTTP simulado y flujo Angular. Ninguna prueba automatizada usa Meta real. La calidad del detector sobre videos reales se evalúa con una ejecución manual y revisión de las tres cruces.

Fuentes: [Imágenes y coordenadas](https://dev.meta.ai/docs/image-understanding), [Salida estructurada](https://dev.meta.ai/docs/structured-output), [Conteo de tokens](https://dev.meta.ai/docs/token-counting), [Precios](https://dev.meta.ai/docs/pricing-rate-limits).

[English](KNOT-LOCALIZATION.en.md).
