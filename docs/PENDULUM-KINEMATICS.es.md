# ProofLab AI — Etapa 2: motor geométrico del péndulo (avance parcial)

**Estado:** núcleo .NET compilado y probado; NO integrado en la interfaz, SIN SAM 3.1 ni Muse Spark. No confundir una simulación con observaciones físicas certificadas.

## Medición propuesta

`PendulumVideoKinematics.Analyze` toma exclusivamente candidatos `(tiempo, Xpx, Ypx)` almacenados por el seguidor local y un **pivote elegido por el usuario**. Estima ángulo de imagen mediante `atan2(Xbob-Xpivote, Ybob-Ypivote)`, velocidad angular `rad/s` y aceleración angular `rad/s²` mediante ajustes polinómicos locales de cinco muestras, además de velocidad tangencial aparente `px/s`. Las predicciones ideales no se utilizan para ajustar observaciones. Las salidas sin evidencia suficiente permanecen nulas/no concluyentes.

Se aplica suavizado mediano de cinco muestras **solo en el interior** para no distorsionar el ángulo del primer/último fotograma. Se rechazan huecos temporales grandes, cambios abruptos de ángulo y trayectorias con radio incoherente. Se informa calidad y desviación del radio pivote–candidato; una trayectoria periódica **no garantiza identidad real de la plomada**.

El motor calcula por separado una **velocidad angular ideal máxima al centro**, usando longitud y ángulo declarados; esta curva es **PREDICCIÓN TEÓRICA** y nunca reemplaza los valores observados. El comportamiento ideal de máxima rapidez al centro y mínima en extremos **no se fuerza** sobre las mediciones.

## Unidades y precauciones

Sin calibración metrológica independiente, **no mostrar plano XY físico ni afirmar velocidades en m/s**. El motor admite un caso condicional explícito de escala aproximada bajo supuesto humano de cámara frontal y radio consistente; **el endpoint de integración propuesto lo deja deshabilitado** y este caso no es todavía una medición física validada. Las observaciones provienen de FFmpeg/sustracción de fondo local a **480×270 y 12 FPS**, NO de SAM; timestamps son rejilla de muestreo, NO PTS nativos.

## Verificación y bloqueo actual

- SDK comprobado: **.NET 10.0.401** en el equipo autorizado `DESKTOP-ESGSLJH`.
- Se sustituyó `Math.Hypot(dx,dy)` por **`double.Hypot(dx,dy)`** mediante la edición permitida.
- Se corrigió suavizado unilateral de extremos y pasaron **10/10 pruebas específicas**, seguidas de **136/136 pruebas .NET Release**.
- Se preparó un almacenamiento local de revisiones del pivote asociado al SHA-256 original y nuevos endpoints en `Program.cs`, y la API compiló **Debug, 0 advertencias/0 errores**, pero **estos endpoints todavía NO tienen prueba HTTP**.
- Los controles de seguridad bloquearon una escritura de componente Angular y luego un intento de reiniciar la API; **no se alteraron mecanismos de seguridad ni se intentó otra vía**. La web 4300 y la instancia de pruebas 5393 seguían activas; API principal 5391 quedó detenida. No se realizaron llamadas pagadas.
- Se preservaron archivos concurrentes Muse sin publicar y videos privados en `.private`.

**Pendiente:** restaurar 5391 mediante procedimiento expresamente permitido, probar API del pivote con un video real, permitir marcar pivote con mouse y renderizar flechas físicas en la misma línea de tiempo, comprobar resultados frente a cronometraje manual y procedencia SAM, actualizar CI y memoria de misión. **Etapa 2 no terminada**.

**English:** [PENDULUM-KINEMATICS.md](PENDULUM-KINEMATICS.md).
