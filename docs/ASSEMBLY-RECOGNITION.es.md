# Reconocimiento del montaje y seguimiento por elemento

> Estado actual: la sección **Reconocer montaje** se retiró del laboratorio a petición del usuario. **Procesar** ejecuta SAM directamente, con el token de sesión y el costo del video, sin reconocimiento previo ni US$0,05 adicionales. La ruta ampliada descrita abajo se conserva en el código; las definiciones ya revisadas y su caché siguen siendo válidas. Nuevos componentes de interfaz requieren consulta previa al usuario.

El laboratorio admite la identificación visual de un objeto suspendido sin fijar su color,
material o forma (por ejemplo, una moneda), la cuerda, el nudo superior, una polea real y
el soporte. La precisión de este flujo ampliado aún requiere una prueba real con Meta.

## Flujo ampliado conservado (no visible en el laboratorio)

1. Cargar el original y abrir **Reconocer montaje**. No hay llamadas pagadas automáticas.
2. Muse Spark 1.3 propone cajas por elemento y la salida exacta de la cuerda en tres
   fotogramas originales (10 %, 50 % y 90 % de sus índices). Presupuesto máximo
   estimado US$0,05; preflight de hasta 24.000 tokens de entrada y salida limitada
   a 4.096. El conteo no es una factura. Requiere token y consentimiento manual.
3. Revisar las tres imágenes, excluir elementos mal identificados y confirmar.
   Si el objeto es incorrecto, no confirmar. La definición revisada es inmutable.
   Elementos ausentes o poco claros no se inventan. El centro de un transportador
   no es una polea ni el punto de suspensión.
4. Volver a configurar el token de un solo uso, revisar el costo del video y pulsar
   **Procesar**. SAM 3.1 recibe cajas en fotogramas originales con IDs separados:
   objeto 1, cuerda 2, nudo 3, polea 4, soporte 5. Solo se envían roles revisados
   con al menos una observación visible y confianza declarada >= 0,8.
5. El reproductor muestra las máscaras por elemento y el centro geométrico de la
   máscara del objeto. Ausencia de máscara en un fotograma significa dato ausente,
   no repetición de la última posición ni sustitución por otro ID.

Muse reconoce los papeles; SAM segmenta y sigue las regiones. El centro visible
no certifica el centro físico de masa. Nudo, polea y salida libre de la cuerda son
puntos distintos. Solo una salida estable revisada de un péndulo simple permite
el pivote propuesto. Para poleas o montaje incierto se conservan máscaras/posiciones
y se desactivan θ/ω del péndulo simple; no se presume esa dinámica.

## Evidencia y límites

- Original, resolución, orientación y PTS conservados; sin reducción de FPS.
- Caché SAM en SQLite separada por video y definición revisada. Recepción y
  reproducción verifican protocolo, SHA, roles, dimensiones, IDs y tiempos.
- La caché histórica de una plomada sigue disponible sin otra llamada.
  Las inferencias nuevas pueden procesar directamente una plomada sin revisión previa del montaje.
- Muse: archivos privados en `.private/physics/assembly`; respuesta, imágenes,
  hashes, intento y revisión. SAM conserva su archivo/journal/SQLite existente.
- Dos acciones potencialmente facturables, presupuestadas por separado; cada una
  consume un cupo de la autorización compartida. Nunca renueva el cupo ni reintenta
  una solicitud fallida automáticamente. Reconocer no inicia SAM.
- La confianza del modelo no es una probabilidad calibrada. Una cuerda muy fina,
  desenfoque, reflejos, oclusión o varios objetos similares pueden impedir el
  reconocimiento. No se garantiza detectar todo objeto o todos los fotogramas.
- Los ángulos disponibles siguen siendo estimaciones del eje suspensión–centro
  visible. Una cuerda doblada o una unión desplazada no equivale a ese eje.
- No se han ejecutado llamadas reales para validar este nuevo flujo de múltiples
  objetos. Las pruebas usan respuestas sintéticas identificadas como tales.

Verificado localmente: 68 pruebas .NET enfocadas; 83 Angular; build Angular y
publicación .NET desde exportación limpia. No CI. Permanece el aviso de tamaño
del bundle inicial (aprox. 626 kB frente al umbral de 500 kB).

Referencias: [SAM: prompts, cajas y múltiples objetos](https://dev.meta.ai/docs/sam/segmenting),
[Muse: imágenes y coordenadas](https://dev.meta.ai/docs/image-understanding),
[conteo previo](https://dev.meta.ai/docs/token-counting),
[precios](https://dev.meta.ai/docs/pricing-rate-limits).