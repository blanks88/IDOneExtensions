# Plan de lanzamiento

## Contrato

Conservar IDs, modelos completos, listas fijas y recálculo global de precios positivos.
Usar tasas configuradas. Un commit por corrección; no incluir cambios ajenos.

| Tarea | Responsable | Aceptación | Estado |
|---|---|---|---|
| preflight | lead | Imagen amd64 operativa, configuración y esquema compatibles | Completo: code=text, imagen amd64 |
| deployment | lead | Release identificable en alusa-syscom-sync, web=0 | Completo |
| scheduled-validation | lead + verificador | Dos ejecuciones scheduled exitosas separadas por cinco minutos; verificación durable e integridad | Completo: 18:15 y 18:20 del 30/10/2026 |
| daily-schedule | lead | Mismo trigger activo con 0 3 * * *, America/Mexico_City | Completo: cron y zona persistidos |
| pull-request | lead | Documentación completa, commits revisados y PR aprobado antes de publicar | Pendiente de VoBo de publicación |

Equipo solicitado: GPT-6 Sol, esfuerzo medio; revisores de despliegue y periodicidad
en lectura. El lead controla ejecución, escrituras e índice Git.

No contar corridas manuales como prueba de periodicidad. Capturar antes de la primera
corrida IDs, código→ID y filas fijas; correlacionar cada ejecución programada con run_id.
