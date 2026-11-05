# ProofLab AI — Datos públicos de demostración del péndulo (4 de noviembre de 2026)

[English](README.md) · [Proyecto](../README.es.md) · [Guía de instalación](../JUDGE-SETUP.es.md) · [Alcance de evidencias](../docs/SUBMISSION-EVIDENCE-2026-11-04.es.md)

Este es un **catálogo independiente, reproducible y sin datos privados**, extraído mediante consultas GET de solo lectura de la aplicación de ProofLab AI en AWS Lightsail. El propietario seleccionó **25 investigaciones**. Sus códigos de recuperación originales **no se publican**: se usan alias `PEND-01` a `PEND-25`.

## Resultados que realmente constan en AWS

| Veredicto guardado | Registros |
| --- | ---: |
| Dentro de la tolerancia práctica declarada | 18 |
| Fuera de la tolerancia práctica declarada | 6 |
| Inconcluso (sin período válido del segundo video) | 1 |
| **Total** | **25** |

**24 registros cuentan con los tres períodos numéricos:** T₁ medido, predicción T₂ congelada y T₂ observado. En el caso restante se conserva el veredicto inconcluso y la ausencia de T₂, sin inventarlo.

Se cotejaron la identidad del estudio, los SHA-256 de videos originales, el hash de la predicción y que esta se congelara **antes de subir el segundo video a ProofLab**. Los hashes de las fuentes coinciden con el manifiesto de los ocho videos públicos. Esto **no demuestra** que la segunda grabación se haya filmado después de predecir.

Son **25 comparaciones que reutilizan ocho videos previamente grabados**, no 25 experimentos físicos independientes. Los resultados dentro del margen no son una validación externa de precisión física: faltan calibración independiente del pivote e incertidumbres sistemáticas completas.

## Contenido

- `pendulum-cases.json`: resúmenes numéricos, intervalos, estados de medición, hashes de medios autorizados, cronología, revisión y decisiones guardadas.
- `pendulum-cases.csv`: una fila por comparación, fácil de abrir con Excel.
- `build-demo-db.mjs`: crea una SQLite nueva de consulta, sin dependencias npm; requiere **Node.js 24+**.

Los ocho videos originales siguen en la galería pública del proyecto, enlazados y verificados mediante SHA-256; no se duplican en GitHub.

## Crear la base SQLite de demostración

Desde la raíz del repositorio, en Windows, macOS o Linux:

```sh
node demo-data/build-demo-db.mjs --verify
node demo-data/build-demo-db.mjs prooflab-demo.sqlite
```

El programa **no sobrescribe** una base existente. El archivo `prooflab-demo.sqlite` está excluido de Git. Se puede consultar con cualquier visor SQLite:

```sql
SELECT case_id, baseline_length_m, followup_length_m,
       measured_t1_s, predicted_t2_s, measured_t2_s, recorded_verdict
FROM pendulum_cases ORDER BY case_id;
```

Tablas: `pendulum_cases` (25), `public_recordings` (8) y `dataset_metadata`. No requiere Internet ni créditos Meta para reconstruir esta base.

## Límites y privacidad

La SQLite generada es **un catálogo separado**, no la SQLite operativa del producto. Iniciar la interfaz de ProofLab **no muestra automáticamente** las 25 sesiones: el juez podrá examinarlas mediante CSV/SQLite. Reproducir las sesiones originales dentro de la aplicación exigiría una migración separada y autorizada de evidencias privadas, que aquí no se realiza.

No se incluyen códigos de recuperación, tokens, recibos brutos del proveedor, videos no autorizados ni la base de producción. No hubo inferencias nuevas ni llamadas pagadas durante la exportación.

**Nivel de evidencia:** catálogo público de resultados guardados, no repetición independiente de los análisis científicos con Meta.
