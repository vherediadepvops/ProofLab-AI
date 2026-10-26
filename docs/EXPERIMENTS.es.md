# ProofLab AI — Protocolos científicos

## 001 · Péndulo

**Pregunta:** ¿La longitud modifica el período conforme a `T = 2π√(L/g)`?

Mide desde el **pivote hasta el centro de masa**. Graba videos de 20 segundos
con longitud de 50 cm y 80 cm, **tres repeticiones por cada longitud**,
una apertura inicial cercana a 15 grados y cámara fija. A 60 fps, un video
de 20 segundos puede contener hasta 1,200 fotogramas. Registrar origen real,
tasa de cuadros y parámetros de cámara.

Predicciones para gravedad estándar:
- 50 cm: aproximadamente 1.419 s por oscilación completa.
- 80 cm: aproximadamente 1.794 s por oscilación completa.

Para comprobar la predicción sin sesgos, detectar cruces ascendentes del centro
utilizando exclusivamente las posiciones observadas, sin emplear el período
esperado para localizar eventos. Comparar período observado, cronometraje
manual independiente y teoría. Documentar incertidumbre y movimiento en profundidad.

## 002 · Esfera rodando sobre una rampa

**Pregunta:** ¿Aumenta la aceleración de una esfera maciza al incrementar
la inclinación?

Grabar tres videos a 10° y tres a 20°, con la misma tabla rígida y esfera.
Medir el ángulo y calibrar las posiciones del video usando una referencia
de longitud visible. Comparar con el modelo sin deslizamiento
`a=(5/7)g sin(θ)`.

Calcular aceleración a partir de **metros recorridos sobre la rampa** y las
marcas de tiempo. Los píxeles por sí solos no proporcionan unidades de
aceleración del SI. Evitar deslizamiento, rebotes y movimiento de cámara.

## Resultados y evidencias

Mantener videos experimentales fuera de Git, conservar derechos sobre
el material, registrar procedencia, comparar método manual y automático,
y nunca presentar coordenadas sintéticas como datos experimentales reales.

Toda afirmación de **5 veces más rapidez** deberá sustentarse con pruebas
cronometradas frente a un proceso manual documentado usando los mismos videos.

**English:** [EXPERIMENTS.md](EXPERIMENTS.md).
