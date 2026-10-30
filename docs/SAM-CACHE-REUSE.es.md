# Reutilización de mediciones SAM

Actualizado: 30/10/2026. [English](SAM-CACHE-REUSE.en.md).

Procesar, en el modo automático y en **API Meta · segmentos**, consulta primero la evidencia guardada del mismo archivo, identificada por SHA-256. Si encuentra un análisis utilizable, muestra US$ 0,00 y carga ese análisis exacto, sin token ni llamadas a Meta. Si no lo encuentra, cotiza el flujo existente de Spark y SAM por hasta tres segmentos, incluido su único reintento continuo cuando sea elegible. La opción explícita API Meta conserva el análisis nuevo.

## Qué se puede reutilizar

- Se verifican los hashes, los recibos y, para resultados segmentados, los empalmes y tiempos originales.
- Se usa el mismo cálculo local del reproductor y del informe: suspensión automática disponible y período con estado CANDIDATE. Se exige período positivo y al menos tres ciclos utilizados; no se relajan las comprobaciones geométricas.
- Se buscan resultados segmentados y continuos anteriores. Un intento reciente defectuoso no oculta automáticamente uno anterior utilizable. El reproductor recibe el identificador exacto seleccionado.
- SQLite guarda el veredicto por huella de política, análisis, recibo, suspensión, revisión y derivación. Una corrección cambia la huella y exige reevaluación. La primera inspección puede tardar varios segundos; las siguientes reutilizan el veredicto, manteniendo la verificación de procedencia.

Una máscara válida por sí sola no basta para recuperar mediciones. Una suspensión manual guardada en otra sesión no se asume automáticamente para otro estudio. “Utilizable” significa que hay mediciones calculables; no certifica identidad física, precisión angular ni conclusión científica. No entrena los modelos de Meta ni convierte una evaluación Muse antigua en evaluación del análisis seleccionado.

## Datos rechazados

Los resultados incompletos, alterados o con mediciones insuficientes se excluyen de la selección automática. Los archivos originales, respuestas, recibos, historial e intentos de pago se conservan. No hay un borrado masivo de SQLite ni eliminación de evidencia. Si falla la consulta de calidad, la interfaz muestra el error; no sustituye esa consulta por una llamada pagada silenciosa.

Implementación: SamCacheQuality.cs, PendulumSamCache.cs, SamVideoArchive.cs, SamAdaptiveLiveService.cs, scripts/sam-period-worker.ts y sam-video-replay.ts. Endpoint de lectura: GET /api/science/director/pendulum/sessions/{id}/sam/{role}/reusable. Tabla derivada: sam_cache_quality. Los recibos y respuestas siguen en el archivo privado existente, indexado por SQLite.

## Verificación local

64 pruebas .NET focalizadas y 71 Angular correctas; build de producción con advertencias previas de tamaño. Edge con perfil propio recuperó resultados reales guardados de 50 y 80 cm: θ/ω, vectores y rastro disponibles, T≈1,416 s y T≈1,798 s. Se reprodujeron los originales HEVC 1080×1920; el perfil headless no decodificó HEVC y no se usó para afirmar éxito visual.

Contador Meta 149→149 en ambos recorridos. Cero inferencias nuevas; autorización de cinco pruebas reales no consumida. No se verificó una nueva detección del proveedor ni una nueva evaluación Muse. Sin CI ni cambios en permisos del propietario.
