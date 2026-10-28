# ProofLab AI — Zoom con arrastre y mediciones angulares del péndulo

**Misión 2 · Etapa 2.** Implementación de interacción en la Zona 1 con el motor determinista `PendulumVideoKinematics` ya publicado. **No implica seguimiento SAM 3.1 ni validación metrológica.** Interfaz, controles y avisos en ES/EN.

## Ampliación y arrastre

El botón **Ampliar imagen 1,6×** permite desplazar la vista ampliada con mouse o dedo en las cuatro perspectivas. La posición se limita a ±30% del panel, evitando perder definitivamente el encuadre; **Ajustar imagen** devuelve zoom 1× y desplazamiento a (0,0). Los cuatro contenidos mantienen una misma compensación de desplazamiento para facilitar comparar un mismo detalle. El arrastre utiliza eventos pointer con captura, no modifica `currentTime`, `playbackRate` ni muestras almacenadas; un arrastre no activa accidentalmente el intercambio de miniaturas. Los cambios de vista siguen intercambiando exactamente dos posiciones, con un único `HTMLVideoElement` y tres canvas.

## Pivote humano y mediciones

Con una investigación que ya tenga video y muestras bidimensionales (no conjeturas de posición), aparece **Marcar pivote**. Al pulsarlo:
1. Se pausa el video maestro, se elimina temporalmente zoom/pan, y **Video original** pasa al panel grande.
2. Se pide **hacer clic en el punto de suspensión verdadero**. El clic se proyecta al espacio del rastreador FFmpeg **480×270**, restando las barras de `object-fit:contain`. Los clics sobre bandas negras se rechazan.
3. El cliente manda el SHA-256 del video y píxeles de pivote al endpoint local `POST /api/science/director/pendulum/sessions/{id}/kinematics/{baseline|followup}`; el backend conserva una revisión nueva sin sobrescribir el original. Solo una respuesta de sesión, rol, SHA, versión y fuente coincidentes se visualiza.
4. Al volver a cargar un estudio, `GET .../kinematics/{role}` restaura la última revisión. Cambiar a otro video invalida solicitudes antiguas para que no contaminen la nueva condición. **Corregir pivote** crea otra revisión; no aprueba ninguna evidencia científica por sí solo.

El reproductor dibuja un marcador amarillo del pivote y una línea al centro candidato de la plomada en canvas derivados, y, **solo si hay derivadas observadas válidas para el fotograma**, tres flechas orientadas y de longitud variable: velocidad tangencial cian (px/s), aceleración tangencial naranja (px/s²) y centrípeta violeta (px/s², hacia el pivote). El resumen enseña θ (°), ω (rad/s), α (rad/s²), rapidez tangencial aparente (px/s) con la etiqueta **OBSERVADO · CANDIDATO DE IMAGEN**; sin muestra válida muestra «—». Los valores del modelo ideal aparecen en **sección separada**, etiquetada **NO MEDIDO**: ω máxima teórica al pasar por el centro y ω teórica calculada para el ángulo de imagen actual. La ley ideal alcanza máximo al centro y cero en ángulos extremos según conservación de energía; **nunca se obliga a las estimaciones observadas a cumplirla**.

## Límites científicos y consentimiento

Los candidatos X/Y provienen del **rastreador LOCAL FFmpeg por sustracción de fondo, NO SAM 3.1**; la identidad verdadera de la plomada y del pivote debe confirmarse visualmente. La API sigue con `physicalUnitsEnabled=false`; **no se exhiben coordenadas físicas XY, m/s ni m/s² calibrados**. El preview del navegador sigue H.264 **640px/12FPS**, aunque el original se conserva en privado con su SHA-256. Los instantes del rastreador corresponden a la malla de 12FPS, no al timestamp PTS original. La cobertura de derivadas y calidad se indican sin ocultar el diagnóstico. No se activa gasto Meta. Las predicciones y las mediciones están claramente separadas.

## QA técnica y validación humana pendiente

Pruebas Angular ChromeHeadless: pan en zoom 1,6× con límites, reset Fit y ausencia de seek; clic con letterboxing, rechazo de geometría inválida, no interpolación en huecos; vector tangencial firmado e inward radial; ideal máximo y extremos; GET del pivote vinculado a SHA, POST con pivote marcado, cambio de ensayo invalida GET anterior, sincronización de observación/flechas. Ejecutar suite completa, build de producción y CI al publicar. La API de revisiones ya fue validada con MOV auténtico (316 coordenadas locales) anteriormente, pero **el propietario todavía debe marcar el pivote real y comprobar visualmente que las flechas siguen el centro de su plomada**. No declarar Etapa 2 científica terminada sin esta evaluación ni sin incertidumbre y SAM real autorizado.

**English:** [PENDULUM-ZOOM-PAN-KINEMATICS.md](PENDULUM-ZOOM-PAN-KINEMATICS.md).
