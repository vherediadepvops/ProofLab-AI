# Mediciones por fotograma y recuperación de investigación

Implementación local del 30/10/2026. No requiere nuevas inferencias para revisar evidencia guardada.

## Uso

1. Al crear una investigación, copia su ID desde la cabecera del Laboratorio.
2. Tras recargar o perder conexión, abre Inicio, pega el ID en «Retomar investigación por ID» y pulsa Retomar. El navegador recuerda el último ID.
3. Se recuperan configuración, videos, resultados verificables y predicción congelada. Puede alternarse entre Prueba 1 y Prueba 2.
4. Si hay una solicitud en curso, se consulta su estado cada 2,5 s; no se envía otra. Una etapa interrumpida exige revisar su estado antes de continuar manualmente.

El ID identifica una investigación y sus dos pruebas, no la sesión temporal del token. Recuperar funciona contra la misma API y sus datos privados persistentes; no es sincronización en la nube. Una carga de archivo incompleta requiere seleccionar de nuevo el archivo. Tokens nunca se guardan en el registro de recuperación. Si hacen falta llamadas nuevas tras recargar, el propietario debe volver a introducir el token.

## Geometría

Una referencia aislada de suspensión ligeramente desplazada no invalida las otras dos si concuerdan dentro del umbral existente y el desplazamiento total está acotado a tres veces ese umbral. Las tres referencias siguen requiriendo visibilidad, confianza suficiente y procedencia auténtica. Cámara móvil, desacuerdo grande, identidad incierta o una trayectoria mayoritariamente incoherente siguen siendo inconclusos. Se informa qué ancla se descartó; esto no certifica la precisión del pivote.

Cada posición SAM se comprueba individualmente. Un fotograma fuera de la geometría esperada no muestra valores angulares; un fotograma posterior válido vuelve a mostrar θ. Para ω se necesitan vecinos válidos consecutivos. No se deriva a través de huecos ni se inventa un dato para rellenarlos. El muestreo del reproductor usa como máximo medio intervalo nativo.

El informe V2 conserva todos los centroides originales, registra índices excluidos, umbral y residuo bruto. Los ciclos que cruzan huecos no entran en el período. Se mantienen condiciones de cobertura y estabilidad; el intervalo del 95 % cubre solo repetibilidad, no incertidumbre total. El worker de Muse y el reproductor comparten estas funciones. Las observaciones y predicciones ya congeladas no se reescriben.

## Persistencia y límites

GET /api/science/director/pendulum/sessions/{id}/recovery consulta sesión, resultados y un diario privado de etapas. Los recibos .started.json/.finished.json guardan IDs, etapa, estado, hora y proceso; no contienen credenciales ni cuerpo de solicitudes.

Una etapa enviada continúa ante desconexión del navegador durante un máximo de 70 minutos y hasta el cierre de la API. Recuperar no inicia etapas siguientes ni reenvía solicitudes posiblemente facturadas. Un reinicio del servidor marca la operación no terminada como interrumpida. El encabezado opcional X-ProofLab-Operation-Id impide ejecutar dos veces un mismo ID. No hay garantía de reanudar un trabajo interno del proveedor que no haya dejado evidencia recuperable.

La prueba inicial con predicción congelada se vincula a su análisis original. Si falta esa evidencia o no pasa integridad, se informa; nunca se sustituye por un análisis posterior del mismo video. Los archivos y SQLite existentes permanecen intactos.

## Verificación

- 110 pruebas Angular de cálculo, reproducción, confianza y recuperación; 27 de recuperación/interfaz repetidas tras el último ajuste: correctas.
- 7 pruebas .NET: persistencia, aislamiento, interrupción, desconexión, duplicados y vinculación de predicción: correctas.
- Compilaciones Angular/.NET correctas; advertencias de tamaño Angular: bundle inicial ~739 kB, dos estilos ligeramente sobre 10 kB.
- Evidencia real guardada de 80 cm: 1.875 máscaras; una de tres anclas excluida, ninguna máscara excluida, período candidato 1,798157 s, 16 ciclos completos/8 usados. No se midió un nuevo experimento físico.
- Edge: ID, dos videos, recarga y predicción recuperados sin POST a Meta. H.264 original reproducible. El MOV/HEVC no decodifica imagen en el perfil Edge aislado; sus cálculos guardados sí se verificaron offline. El H.264 histórico usado carece de suspensión registrada y conserva θ/ω inconclusos.
- Cero llamadas Meta y cero CI en este cambio. Permisos manuales y contador del propietario conservados. No es entrenamiento de SAM/Spark.

English: [RESEARCH-RECOVERY.en.md](RESEARCH-RECOVERY.en.md).
