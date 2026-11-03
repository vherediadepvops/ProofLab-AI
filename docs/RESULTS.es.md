# Resultados — investigación del péndulo en ProofLab AI

**Meta Global AI Developer Hackathon 2026 · Indie Developer · Push the Frontier**
**Resultados históricos recopilados el 3 de noviembre de 2026.** [English](RESULTS.md) · [Protocolo de evaluación](EVALUATION.es.md).

**Interpretación:** son observaciones documentadas durante el desarrollo y la evaluación, **no estadísticas de precisión auditadas independientemente** ni demostración de un descubrimiento. La campaña de diez casos utilizó videos reales anteriores, un control negativo deliberado y un control sintético.

## Resumen

| Métrica | Resultado registrado | Alcance |
|---|---|---|
| Casos científicos con Muse/SAM (30 oct) | **10** | 8 comparaciones de 6 videos físicos ya existentes + 1 control negativo + 1 sintético |
| Dentro de la banda práctica predeclarada | **7/10** | **Incluye** el control sintético; no representa 70 % de precisión |
| Inconclusos | **2/10** | Suspensión incierta del video real 80CM_2 |
| Fuera de banda | **1/10** | Video de 50 cm declarado intencionalmente como 80 cm (control negativo, desviación **−20,97 %**) |
| Inferencias de proveedor registradas | **102** | Incluye recuperaciones; no son 102 experimentos independientes |
| Suma de presupuestos estimados | **US$ 7,1816** | **Estimación, no factura ni gasto confirmado** |
| Verificación de versión `b651232` (3 nov) | **153 .NET / 101 Angular correctas** | Pruebas locales e interfaz en producción; **0 inferencias Meta nuevas**; no certifica commits posteriores |

**Desglose:** de ocho comparaciones con videos reales, seis quedaron dentro de sus bandas prácticas y dos fueron inconclusas; el control sintético coincidió y el control de longitud incorrecta quedó fuera. Se reutilizan videos entre pares, por lo que los casos no son estadísticamente independientes.

## Uso de Meta e inventario histórico de campañas (28 de octubre–3 de noviembre)

La captura del titular en **Meta Developer Usage**, correspondiente al **28 de octubre–3 de noviembre de 2026**, con filtros **All API keys** y **All models**, mostró **550 solicitudes**, **11,5 millones de tokens de entrada**, **621,1 mil de salida** y **9 % de aciertos de caché**. La pantalla mostraba **S/ 0,00 de gasto**, pero **no equivale** a una factura verificada, conciliación de cobros ni prueba de costo cero. Es una instantánea del uso del equipo/proyecto, **no** un export detallado de solicitudes. No podemos atribuir de forma única todas las 550 a experimentos de ProofLab ni demostrar que todas terminaron correctamente.

Esta tabla presenta **campañas históricas**, no sujetos experimentales independientes ni totales acumulables. Los valores indican **inferencias de modelo registradas por campaña**, salvo cuando se distingue el conteo HTTP. El 3 de noviembre se resumieron en solo lectura dos bitácoras JSON privadas del proyecto autorizado: `fast-eight` registró **19 Spark + 94 SAM = 113** llamadas y `full-science`, **16 Spark + 66 SAM + 20 Muse = 102**. Se comprobaron sus estados y resultados sin exponer identificadores, medios ni respuestas privadas.

| Período / campaña | Inferencias registradas | Evidencia y resultado | Precaución contra duplicación |
|---|---:|---|---|
| 28 oct. — primeras pruebas de Muse | **2 Muse** | Primera respuesta real rechazada por inconsistencia numérica; segunda aceptada y congelada. **4 solicitudes HTTP a Meta** contando 2 consultas de tokens | 4 solicitudes HTTP **no son** 4 inferencias; integración separada |
| 28 oct. — primer video real del péndulo con SAM | **1 SAM** | 1.781 máscaras de 1.828 cuadros; cobertura 97,43 %; verificación visual/humana parcial | Cobertura no equivale a exactitud física |
| 29 oct. — valores automáticos en videos nuevos | **12: 6 Spark + 6 SAM** | Seis flujos; algunos mostraron ángulo/velocidad y otros fueron inconclusos | No está resuelta la posible reutilización entre campañas |
| 29–30 oct. — videos segmentados y revisión del objeto | **113: 19 Spark + 94 SAM** | Bitácora final `fast-eight`: 18 pruebas con API y un registro sin llamadas; seguimientos satisfactorios e inconclusos | Las **83** anteriores (12 Spark + 71 SAM) **ya están incluidas en 113** |
| 30 oct. — diez contrastes científicos | **102: 16 Spark + 66 SAM + 20 Muse** | Bitácora `full-science`: **7 dentro de banda, 2 inconclusos y 1 control negativo deliberado fuera**; seis videos reales reutilizados | No representa diez experimentos físicos independientes |
| 30 oct. — refinamiento de suspensión | **5 Spark** | Mejoró control sintético de 80 cm; persistieron casos físicos inciertos | Se reutilizaron máscaras SAM, no hubo cinco SAM nuevos |
| 30 oct. — validación de recortes del cuerpo | **5: 3 Spark + 2 SAM** | Grabación real de 50 cm y control sintético; sin verdad independiente de precisión angular | Cinco llamadas, **no** cinco experimentos científicos completos |
| 3 nov. — investigaciones de videos y contacto móvil | **42: 28 Spark + 7 SAM + 7 Muse** | Siete flujos técnicos terminados; **una medición candidata**, seis decisiones inconclusas o de repetir; sin nuevo contraste verificado de dos videos | Las **30** llamadas iniciales de cinco flujos están **incluidas en 42** tras dos flujos adicionales (12) |

Referencias: [valores automáticos](AUTOMATIC-PENDULUM-VALUES.es.md), [videos segmentados](SAM-NATIVE-SEGMENTS.es.md), [contrastes científicos Muse](MUSE-INVESTIGATOR.es.md), [suspensión](SUSPENSION-REFINEMENT.es.md), [validación del cuerpo](BODY-CROP-VALIDATION.es.md) y [corrección del índice de fotogramas del 3 de noviembre](https://github.com/vherediadepvops/ProofLab-AI/commit/8a75fff0615f712ae496bf55919a916c34143b00). Los detalles por solicitud permanecen privados deliberadamente.

**No sumar las filas para afirmar un total único.** Algunas son subtotales acumulados y falta conciliar identificadores entre campañas. En particular, **550** es el conteo de solicitudes del panel de Meta para un rango temporal, **113** y **102** son totales comprobados de dos bitácoras locales y **10** son casos científicos. Miden cosas diferentes. Otras pruebas documentadas no aparecen desglosadas aquí; el registro **no es un inventario exhaustivo** de las 550 solicitudes.

**Las pruebas automatizadas son distintas:** el 3 de noviembre la versión `b651232` aprobó 153 .NET y 101 Angular; otro registro anterior del mismo día documentó 539/539 pruebas .NET. Las suites, versiones y datos de prueba se solapan y **no deben sumarse como tests únicos, experimentos físicos o solicitudes a Meta**.

## Controles y valores observados

| Evidencia | Resultado | Alcance y límite |
|---|---|---|
| Control sintético 50 cm (período conocido) | **1,419011 s** medido frente a **1,418746 s** conocido (**+0,019 %**) | Cercanía numérica, pero seguían los problemas de pivote y seguimiento |
| Control sintético 80 cm (período conocido) | **1,793957 s** medido frente a **1,794587 s** conocido (**−0,035 %**) | No certifica precisión angular física |
| Video real 50CM_1 después de cambios de localización del cuerpo (otra tanda de cinco llamadas) | Período **1,415824 s**; incertidumbre estándar tipo A **0,000424 s**, con 10 de 20 ciclos alternos | Seguimiento SAM real, sin coordenada verdadera independiente del pivote |
| SAM real 50CM_1 de esa tanda | **1.828/1.828** máscaras; llamada SAM continua histórica de **193,55 s** | Cobertura y latencia; no acredita que cada máscara siga el objeto correcto |
| Control sintético comparativo de seguimiento | Error medio del centro **30,39 → 1,04 px**; error angular medio **7,82° → 1,47°** | Cambiaron también el modo SAM segmentado/continuo y la región; **no atribuir toda la mejora al recorte** |

La validación adicional de cinco llamadas realizó tres Spark y dos SAM (estimación **US$ 0,75621375**, no factura). **No fueron cinco estudios científicos completos adicionales.**

## Fallos y resultados inconclusos

- **Suspensión dudosa:** dos comparaciones retrospectivas relacionadas con `80CM_2` quedaron inconclusas. No se modificaron los criterios para forzar una aprobación.
- **Control negativo deliberado:** la longitud incorrecta generó una desviación de **−20,97 %** fuera de banda. Es una prueba útil de rechazo, no un fallo de un péndulo real de 80 cm.
- **Geometría sintética:** aunque los períodos del control tuvieron menos de 0,04 % de diferencia, Spark desplazó el pivote y SAM perdió **13,4 %** de cuadros del primer control. Un período correcto no demuestra ángulo ni velocidad angular correctos.
- **Reproducción:** originales HEVC/MOV produjeron audio sin imagen en dos perfiles Edge aislados en octubre; las máscaras y los tiempos originales sí permitieron análisis. No se generaliza a otros navegadores.
- **Calibración física:** faltan referencias independientes de pivote, perspectiva, longitud y presupuesto completo de incertidumbre sistemática.

Detalles: [método del período SAM](SAM-PERIOD.es.md), [investigador Muse](MUSE-INVESTIGATOR.es.md) y [validación del cuerpo](BODY-CROP-VALIDATION.es.md).

## Los cambios recientes no son nuevos experimentos

El **3 de noviembre de 2026**, la versión [`b651232`](https://github.com/vherediadepvops/ProofLab-AI/commit/b6512323e7f72f0d718af30be648c8a59baaf648) cambió los análisis nuevos a **un video completo + 50 imágenes originales, una revisión del objeto y dos llamadas Spark**, sin rondas automáticas de zoom. El máximo cotizado para esas llamadas Spark pasó de **US$ 0,85 a US$ 0,35**. Se conservaron la preparación en segundo plano y los resultados históricos.

La versión superó **153 pruebas .NET y 101 Angular**, compilación/publicación local y verificación de interfaz ES/EN en escritorio/móvil, tanto local como en producción. **No hubo inferencias nuevas del proveedor ni validación independiente del pivote en esa entrega**. Los commits posteriores de `main` no quedan automáticamente cubiertos.

## Evidencia, privacidad y reproducibilidad

- **Público:** código/pruebas, [protocolo](EVALUATION.es.md), [investigador Muse](MUSE-INVESTIGATOR.es.md), [estimación SAM](SAM-PERIOD.es.md), [validación del cuerpo](BODY-CROP-VALIDATION.es.md) y commits fechados.
- **Privado:** videos originales, respuestas completas de Meta, recibos, bitácoras por caso y SQLite. No se incluyen credenciales, medios privados ni historiales sensibles.
- **Límite:** se pueden repetir pruebas y cálculos publicados, pero **los diez casos con Meta no pueden reproducirse íntegramente con los archivos públicos actuales**.

**Próxima evidencia necesaria:** calibración física independiente, una segunda grabación efectivamente posterior a la predicción congelada y pruebas reales con Meta para el último flujo. Este documento no ejecutó inferencias ni experimentos nuevos.
