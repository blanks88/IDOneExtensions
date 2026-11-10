# Entrega — rendimiento de sincronización SYSCOM

## Identificación y resultado

- Cambio: 261030-002-FEATURE-rendimiento-sincronizacion-syscom; plan main; tarea integracion.
- Responsable: /root; equipo gpt-6-sol/high: paging, pipeline y metrics.
- Base da1f198; rama feature/syscom-sync-performance en worktree aislado.
- Estado: implementación y verificación PASS; revisión independiente PASS sobre `600748b`.
- Revisión entregada: `600748b`.
- Alcance: opciones de página/buffer, pipeline y métricas; recálculo global conservado.
- Sin cambios pendientes ajenos importados, paquetes nuevos, publicación o proveedor real.

## Criterios y evidencia

| Criterio | Resultado |
| --- | --- |
| Página 10–1000 default500 y buffer1–16 default2 desde entorno hasta Worker/HTTP | PASS: opciones, configuración y URL probadas |
| HTTP secuencial solapado con único escritor y memoria acotada | PASS: compuertas de coordinación prueban solapamiento, orden y contrapresión |
| Cancelación/error observa ambas tareas antes de cleanup y conserva error | PASS: pruebas productor/consumidor/cancelación y PostgreSQL |
| Tiempos monótonos por etapa y snapshot aditivo sin modificar estado | PASS: reloj controlado, concurrencia y actualización de esquema legado |
| Recálculo global y promoción atómica | PASS: otras empresas/listas porcentuales, listas fijas y rollback |
| Otros pendientes intactos | PASS: seis hashes SHA256 iguales en checkout original |

## Validación ejecutada

2026-10-30, .NET10 Release, PostgreSQL17 desechable y Postman CLI local.
`dotnet test IDOneExtensions.sln -c Release --no-restore --logger "console;verbosity=detailed"`:
**81 PASS, 4 SKIP, 85 total**. Variables locales SYSCOM_TEST_DB_CONNECTION y
SYSCOM_SIMULATION_URL; sin secretos o destinos de producción. `git diff --check` PASS.

Primera compilación detectó un overload ambiguo de Assert.Throws, corregido. La siguiente
suite detectó dos suposiciones incorrectas de buffer y cuatro aserciones heredadas de
pages_loaded. Se corrigieron y la suite completa posterior pasó. En una falla concurrente
pueden confirmarse entre cero y las páginas válidas previas: las pruebas exigen ese rango,
Failed, métricas y ausencia de promoción; no exigen un orden secuencial inexistente.

| Página | Llamadas | Total observado ms | Descarga ms | Staging ms | Recálculo global ms |
| --- | --- | --- | --- | --- | --- |
| 60 | 20 | 325 | 67 | 96 | 176 |
| 500 | 3 | 355 | 32 | 58 | 247 |
| 1000 | 2 | 302 | 21 | 66 | 189 |

Catálogo estable de 1,200 productos. Cada ejecución conserva 1,201 productos contando
la otra empresa, 2,402 precios porcentuales y el precio fijo. Una sola muestra por tamaño;
no es benchmark estadístico ni predicción de producción. El solapamiento se verifica con
compuertas sin umbral de rendimiento, evitando una comparación temporal frágil.

## Archivos y reproducción

Código: Load/{SyscomWorker,SyscomPagePipeline,SyscomSyncOptions,SyscomSyncMetrics,
SyscomDatabaseWorker}, Models/Requests, SyscomClient, consola/configuración/environment.example.
Pruebas: configuración, métricas, pipeline, paginación, persistencia y simulación de rendimiento.
Documentación: .itia/docs/sincronizacion-syscom.md, índices y reportes de este cambio;
Postman README y mock con modo paged. Todos pertenecen exclusivamente a este cambio.

Seguir [simulación local](../../../../../../../../postman/README.md), después ejecutar suite
Release con ambas variables locales. Esperar 81 PASS y 4 SKIP; live tests permanecen opt-in.
Los fixtures eliminan sus esquemas; lead detiene su simulador/contenedor al terminar.
Los logs crudos son temporales registrados y no se retienen: el resumen anterior contiene
la evidencia necesaria, sin adjuntos externos.

## Límites y QA

No se ejecutó SYSCOM real ni base productiva. No se afirma reducción porcentual del tiempo
total. Las métricas de descarga/staging se solapan y promoción contiene subetapas.
El snapshot es best effort; una conexión perdida puede impedir persistir métricas.
La revisión independiente de `600748b` terminó PASS; el reviewer no implementó.
Detalle en [QA.md](QA.md).

Control de artefactos: el handoff automático con baseline no pudo validar porque la
asignación inicial solo enumeraba README/PLAN y luego se amplió a los archivos del
equipo. No se alteró ni recapturó el baseline histórico. La comparación manual de
rutas y los seis hashes originales se verificó; el checker staged se ejecuta con
la asignación completa antes del commit.
