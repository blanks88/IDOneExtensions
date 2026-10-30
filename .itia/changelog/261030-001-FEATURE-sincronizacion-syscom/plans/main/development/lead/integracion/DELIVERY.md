# Entrega integrada — mejoras de sincronización

## Identificación

- Cambio: 261030-001-FEATURE-sincronizacion-syscom; plan main.
- Base: 638e297; rama feature/syscom-sync-hardening.
- Equipo: persistence, http y host, gpt-6-sol/high por selección explícita del usuario.
- Integración y commit: lead. [Plan](../../../PLAN.md).

## Resultado

Implementación local completa y lista para revisión independiente. Mantiene recálculo
global, incorpora staging por ejecución y promoción atómica, paginación, recuperación
HTTP y configuración sin secretos incrustados en consola/pruebas. No se activó en un
scheduler ni se ejecutó contra una cuenta o base remota.

## Evidencia local

| Comprobación | Resultado |
| --- | --- |
| dotnet build IDOneExtensions.sln -c Release --no-restore | PASS; 0 warnings, 0 errores |
| dotnet test IDOneExtensions.sln -c Release --no-build --no-restore con DB y simulación local | PASS; 40 aprobadas, 4 contra proveedor real omitidas |
| Suite sin variables locales | PASS; pruebas de PostgreSQL/simulación omitidas por diseño; ninguna conexión real |
| Consola Release sin configuración | Código 2; imprime nombres de variables, no valores |
| Simulación Postman CLI 1.83.1 | Tráfico real confirmado en logs: 429, renovación tras 401, recuperación 503 y fallo persistente acotado |
| PostgreSQL 17 desechable | Bloqueo, descarga incompleta, colisión, determinismo, ausentes, retención y rollback después del borrado de precios cubiertos |
| Alcance global | Prueba verifica productos de empresa 2 y listas porcentuales; lista fija preservada |

## Límites y activación pendiente

- Compatibilidad OAuth real no verificada; default histórico conservado, variante
  documentada por SYSCOM configurable.
- Secretos eliminados del código de consola y pruebas; siguen en historial Git.
  Rotación y provisión del scheduler remoto no realizadas.
- No hay medición con catálogo completo de producción; memoria acotada por página,
  pero la carga mantiene SQL por producto dentro de la transacción de página.
- Cambios del checkout original no importados. Program.cs implementado desde la base
  versionada; README principal, Docker, Heroku y script del checkout original intactos.
- Revisión independiente pendiente. Los reportes de agentes conservan evidencia
  previa; esta tabla es la verificación integrada posterior.
- La captura de baseline de artefactos no se realizó antes de iniciar las contribuciones;
  se verifican el diff contra 638e297 y los bytes staged, sin afirmar ese control previo.
