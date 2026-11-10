# 261030-001-FEATURE-sincronizacion-syscom

## Identificación

- Cambio: `261030-001-FEATURE-sincronizacion-syscom`.
- Registro: 2026-10-30, America/Mexico_City.
- Tipo: FEATURE.
- Origen: solicitud del usuario de preparar mejoras y organizar plan y documentación en itiaOS.
- Objetivo: robustecer la sincronización SYSCOM → IDOne; preservar el recálculo global aprobado.
- Cambios relacionados: ninguno registrado.
- Contexto: [documentación del flujo actual](../../docs/sincronizacion-syscom.md).

## Planes

| Plan | Objetivo | Archivo |
| --- | --- | --- |
| main | Aislamiento, paginación, recuperación y promoción atómica | [PLAN.md](plans/main/PLAN.md) |

## Integración y cierre

- Criterios: los definidos en el plan; validación conjunta en PostgreSQL aislado.
- Tareas: asignadas en main al equipo sol6/high.
- Estado: implementación local terminada; revisión independiente PASS sobre 6a82a98; activación remota no ejecutada.
- Decisión: recálculo global de precios confirmado por el usuario; no restringirlo a SYSCOM.
