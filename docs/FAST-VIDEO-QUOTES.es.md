# Cotización rápida y procesamiento manual

[English](FAST-VIDEO-QUOTES.en.md)

## Flujo visible

Registrar el token en Configuración lo mantiene únicamente en el campo password de la sesión. No se guarda en localStorage, sessionStorage, SQLite, archivos ni logs. Borrar, cambiar de servidor o recargar elimina el token; cambiar el modo de análisis no lo elimina.

Una vez listo el video y la cotización, Procesar queda habilitado dentro de la autorización y el cupo vigentes. El clic autoriza únicamente el envío y presupuesto explicados junto al botón. No hay inferencias al cargar video, registrar el token o consultar costo.

- Verde / US$0: resultado SAM verificado en caché.
- Rojo / usará créditos: API Meta o MISS de caché.
- Si falta el montaje: precio desglosado (reconocimiento hasta US$0,05 + SAM). Procesar reconoce tres imágenes y se detiene para revisión humana; después se vuelve a pulsar Procesar para SAM.
- Un montaje ya reconocido abre su revisión sin otro cobro. Un intento pendiente de reconciliación no se repite.
- El flujo nuevo necesita dos envíos; se avisa si queda menos cupo. Configurar el token no aumenta esa autorización.

## Costos y trabajo paralelo

Status SAM, cotización, readiness y estado del montaje se consultan con GET en paralelo. La primera cotización usa nb_frames del contenedor cuando está disponible; no decodifica todo el video para mostrar el precio. Si falta ese dato, se cuentan los PTS originales.

VideoTimingCatalog conserva conteo y tiempos en el almacenamiento privado, vinculados a SHA, tamaño, fecha de modificación y versión. Comparte el trabajo de solicitudes simultáneas y limita a dos lectores de PTS. La consulta de metadatos tiene una cola independiente para no esperar a esos lectores. Un solicitante cancelado no cancela el trabajo que otros esperan.

Dentro del límite actual de video, los PTS se preparan en segundo plano después de cotizar. Antes del envío se comprueba el SHA completo, el número real de fotogramas y que la estimación no supere la aceptada en pantalla; si sube, se rechaza sin reservar créditos. El remux se sigue comprobando contra los tiempos originales. No hay reducción de FPS o resolución.

## Verificación local, 28/10/2026

En una grabación HEVC de 31,28 s y 1.875 fotogramas:
- Antes: cotizaciones repetidas de 10.390 y 10.579 ms.
- API de prueba: primera cotización 204 ms, repetición 5 ms, estimado SAM US$0,375.
- Tras finalizar la preparación, la cotización reutilizó los 1.875 PTS verificados.
- HIT del video de demostración conservado: 169 ms para consultar el estado.
- Pruebas locales cubren concurrencia, persistencia, cancelación, cambios del original, límites de autorización, respuestas obsoletas, revisión y doble clic. Transportes y claves sintéticos; sin llamadas pagadas ni CI.

Estas mediciones no garantizan latencia para todos los formatos ni validan 4K/120/240 FPS. Se mantienen los límites de carga y de solicitud vigentes. Conteo declarado es una estimación, no una factura; SHA y tiempos completos se verifican antes de enviar.
