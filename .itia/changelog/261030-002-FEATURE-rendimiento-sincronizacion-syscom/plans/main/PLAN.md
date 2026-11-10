# main — Rendimiento de la sincronización SYSCOM

## Identificación y alcance

- Cambio: [261030-002-FEATURE-rendimiento-sincronizacion-syscom](../../README.md).
- Fuente: solicitud explícita del usuario del 2026-10-30.
- Objetivo: medir tiempo por etapa, configurar `limit` y solapar HTTP con staging.
- Exclusiones: cambios al recálculo global, concurrencia entre categorías, despliegue,
  configuración ALUSA_CONNECTION_STRING y demás pendientes del checkout original.
- Base: da1f198; rama feature/syscom-sync-performance, worktree aislado.

## Tareas y propiedad

Todos los agentes usan gpt-6-sol/high por selección explícita del usuario.

| ID | Responsable / handle | Escrituras exclusivas | Criterios |
| --- | --- | --- | --- |
| pipeline | pipeline / /root/pipeline | Worker, pipeline y pruebas correspondientes | Solapamiento probado; cola acotada; único escritor; falla/cancelación detiene ambas tareas sin promover |
| metricas | metrics / /root/metrics | DatabaseWorker, métricas y sus pruebas | Duraciones por etapa, incluso error/cancelación; persistencia aditiva y logs seguros; no cambiar cálculo de precios |
| paginacion | paging / /root/paging | Request/cliente, SyncOptions, consola/config y pruebas propias | limit de 10 a 1000; default500; configuración de cola1–16 default2; pruebas de URL y variables inválidas |
| integracion | lead / /root | Postman, documentación, índices, reportes y commits | Suite local completa y revisión independiente; pendientes originales preservados |

## Contratos compartidos

- SyscomSyncOptions: PageSize=500 (10..1000), BufferedPages=2 (1..16).
- SyscomProductsRequest: int? Limit al final para compatibilidad; cliente envía limit si se configura.
- Variables: SYSCOM_PAGE_SIZE, SYSCOM_BUFFERED_PAGES; sin importar ajustes locales ALUSA.
- SyscomSyncMetrics: Measure(SyscomSyncStage) devuelve IDisposable; GetElapsedMilliseconds(stage),
  TotalElapsedMilliseconds, ToJson(). Thread-safe, reloj monótono; etapas Preparation, Download,
  Staging, CatalogPromotion, PriceRecalculation y Promotion. Las duraciones pueden solaparse
  y Promotion contiene las dos subetapas; no se suman para obtener tiempo total.
- DatabaseWorker constructor tercer parámetro opcional SyscomSyncMetrics; método
  RecordMetricsAsync(Guid runId, CancellationToken ct) guarda snapshot JSON sin cambiar estado.
- Worker crea una instancia por ejecución y la comparte; mide Preparation y cada llamada HTTP
  en Download; DatabaseWorker mide staging y promoción/subetapas. Al finalizar, worker intenta
  persistir métricas con timeout independiente y loguea resumen sin ocultar error original.
- Pipeline productor HTTP secuencial + consumidor DB único, buffer acotado; ningún acceso
  concurrente al UoW. Finalización completa y ambas tareas observadas antes de promoción o cleanup.

## Validación

Tests deterministas mediante coordinación de tareas, sin umbrales de tiempo frágiles.
PostgreSQL17 desechable y Postman local reutilizados; proveedor real omitido por defecto.
Probar el solapamiento con compuertas deterministas y comparar tamaños de página
con el mismo catálogo simulado, sin prometer mejoras de producción. Conservar pruebas de precios globales, rollback y completitud.

## Estado y entrega

Implementación y validación local completas: 81 pruebas pasan y 4 reales omitidas.
Revisión independiente PASS sobre `600748b`; ver QA.md en la entrega de integración. Lead único responsable del índice Git y commits. Cada agente registra
PROGRESS.md y DELIVERY.md bajo development/<agente>/<tarea>/. Revisión independiente posterior.
