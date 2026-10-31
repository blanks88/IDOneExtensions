# Plan de lanzamiento

## Contrato

Conservar IDs, modelos completos, listas fijas y recálculo global de precios positivos.
Usar tasas configuradas. Un commit por corrección; no incluir cambios ajenos.

| Tarea | Responsable | Aceptación | Estado |
|---|---|---|---|
| preflight | lead | Imagen amd64 operativa, configuración y esquema compatibles | Imagen lista; BD pendiente |
| deployment | lead | Release identificable en alusa-syscom-sync, web=0 | Pendiente |
| scheduled-validation | lead + verificador | Dos ejecuciones scheduled exitosas separadas por cinco minutos; verificación durable e integridad | Pendiente |
| daily-schedule | lead | Mismo trigger activo con 0 3 * * *, America/Mexico_City | Pendiente de dos éxitos |
| pull-request | lead | Documentación completa, commits revisados y PR aprobado antes de publicar | Pendiente |

Equipo solicitado: GPT-6 Sol, esfuerzo medio; revisores de despliegue y periodicidad
en lectura. El lead controla ejecución, escrituras e índice Git.

No contar corridas manuales como prueba de periodicidad. Capturar antes de la primera
corrida IDs, código→ID y filas fijas; correlacionar cada ejecución programada con run_id.
