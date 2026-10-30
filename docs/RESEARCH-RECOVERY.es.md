# Mediciones por fotograma y recuperación de investigación

Implementación local del 30/10/2026. No requiere nuevas inferencias para revisar evidencia guardada.

## Uso

1. Abre **Laboratorio**. Una visita nueva o una recarga empieza sin sesión activa; no se crea un ID automáticamente.
2. Pulsa **Generar código de recuperación** para crear una sesión vacía. Copia el código y guárdalo. Esto habilita la configuración y la carga de video.
3. Los cambios de longitud, ángulo, masa y segunda prueba se guardan con el mismo código antes de cargar el primer video. La interfaz indica «Guardando parámetros» o permite reintentar si falla la conexión. Al registrar el video, estos parámetros quedan protegidos.
4. **Video de prueba** y la primera carga propia conservan el código generado. Una Nueva prueba posterior crea otra investigación; la anterior continúa recuperable mediante su propio código.
5. Para recuperar, pega el código en **¿Ya tienes un código?**, al lado del botón de generación, y pulsa **Buscar**. También se conserva el acceso anterior desde Inicio. Se aceptan mayúsculas y espacios al principio/final.
6. La barra «Recuperando sesión» permanece hasta cargar la configuración, el video y los resultados guardados. Un ID desconocido muestra «No se encontró ninguna sesión»; un fallo de conexión muestra un mensaje diferente para reintentar.
7. Se recuperan ambos videos, resultados verificables y predicción congelada. Si una operación sigue en curso, se consulta cada 2,5 s sin reenviarla. Una etapa interrumpida requiere revisar su estado antes de continuar manualmente.

El código identifica una investigación y sus dos pruebas, no el token temporal. Recuperar requiere la misma API y sus datos persistentes; no es sincronización en la nube. Una carga de archivo incompleta requiere seleccionar nuevamente el archivo. Los tokens nunca se guardan en recuperación; para nuevas llamadas tras recargar hay que introducirlos otra vez.

El endpoint local PUT /api/science/director/pendulum/sessions/{id}/draft valida los parámetros y actualiza el modelo mediante reemplazo atómico, conservando el ID. Devuelve 404 si no existe y 409 si ya hay video, propuesta o predicción. No reescribe evidencia congelada.

## Geometría

Una referencia aislada de suspensión ligeramente desplazada no invalida las otras dos si concuerdan dentro del umbral existente y el desplazamiento total está acotado a tres veces ese umbral. Las tres referencias siguen requiriendo visibilidad, confianza suficiente y procedencia auténtica. Cámara móvil, desacuerdo grande, identidad incierta o una trayectoria mayoritariamente incoherente siguen siendo inconclusos. Se informa qué ancla se descartó; esto no certifica la precisión del pivote.

Cada posición SAM se comprueba individualmente. Un fotograma fuera de la geometría esperada no muestra valores angulares; un fotograma posterior válido vuelve a mostrar θ. Para ω se necesitan vecinos válidos consecutivos. No se deriva a través de huecos ni se inventa un dato para rellenarlos. El muestreo del reproductor usa como máximo medio intervalo nativo.

El informe V2 conserva todos los centroides originales, registra índices excluidos, umbral y residuo bruto. Los ciclos que cruzan huecos no entran en el período. Se mantienen condiciones de cobertura y estabilidad; el intervalo del 95 % cubre solo repetibilidad, no incertidumbre total. El worker de Muse y el reproductor comparten estas funciones. Las observaciones y predicciones ya congeladas no se reescriben.

## Persistencia y límites

GET /api/science/director/pendulum/sessions/{id}/recovery consulta sesión, resultados y un diario privado de etapas. Los recibos .started.json/.finished.json guardan IDs, etapa, estado, hora y proceso; no contienen credenciales ni cuerpo de solicitudes.

Una etapa enviada continúa ante desconexión del navegador durante un máximo de 70 minutos y hasta el cierre de la API. Recuperar no inicia etapas siguientes ni reenvía solicitudes posiblemente facturadas. Un reinicio del servidor marca la operación no terminada como interrumpida. El encabezado opcional X-ProofLab-Operation-Id impide ejecutar dos veces un mismo ID. No hay garantía de reanudar un trabajo interno del proveedor que no haya dejado evidencia recuperable.

La prueba inicial con predicción congelada se vincula a su análisis original. Si falta esa evidencia o no pasa integridad, se informa; nunca se sustituye por un análisis posterior del mismo video. Los archivos y SQLite existentes permanecen intactos.

## Verificación de la entrada de recuperación (30/10/2026)

- 46 pruebas Angular de interfaz, recuperación y navegación correctas en Edge.
- 7 comprobaciones locales de API con almacenamiento aislado: mismo código, parámetros recuperados, entrada inválida, ID desconocido, bloqueo tras registrar video, integridad y cero consumo Meta. Script: tests/recovery-draft.integration.mjs; pasar la ruta absoluta del DLL compilado y opcionalmente un puerto libre.
- Recorrido real en Edge: crear código, guardar 80 cm, recargar sin sesión activa, buscar código con mayúsculas, ver la barra, recuperar 80 cm, buscar ID inexistente y restaurar un H.264 histórico con 182 máscaras verificadas. Revisado a 1440 y 540 px; sin excepciones JavaScript.
- Compilaciones Angular/.NET correctas. Advertencias Angular: bundle inicial ~748 kB y estilos de ~11,5/10,2 kB sobre presupuestos de 500/10 kB.
- API 5391 y localhost:4300 actualizados. Cero llamadas Meta; contador 148→148 y permisos manuales conservados. Sin CI.

Las verificaciones históricas de geometría anteriores se conservan debajo; no se repitieron como parte de este ajuste de interfaz.

## Verificación

- 110 pruebas Angular de cálculo, reproducción, confianza y recuperación; 27 de recuperación/interfaz repetidas tras el último ajuste: correctas.
- 7 pruebas .NET: persistencia, aislamiento, interrupción, desconexión, duplicados y vinculación de predicción: correctas.
- Compilaciones Angular/.NET correctas; advertencias de tamaño Angular: bundle inicial ~739 kB, dos estilos ligeramente sobre 10 kB.
- Evidencia real guardada de 80 cm: 1.875 máscaras; una de tres anclas excluida, ninguna máscara excluida, período candidato 1,798157 s, 16 ciclos completos/8 usados. No se midió un nuevo experimento físico.
- Edge: ID, dos videos, recarga y predicción recuperados sin POST a Meta. H.264 original reproducible. El MOV/HEVC no decodifica imagen en el perfil Edge aislado; sus cálculos guardados sí se verificaron offline. El H.264 histórico usado carece de suspensión registrada y conserva θ/ω inconclusos.
- Cero llamadas Meta y cero CI en este cambio. Permisos manuales y contador del propietario conservados. No es entrenamiento de SAM/Spark.

English: [RESEARCH-RECOVERY.en.md](RESEARCH-RECOVERY.en.md).
