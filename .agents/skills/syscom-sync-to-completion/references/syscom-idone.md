# Caso de referencia: SYSCOM → IDOne

Leer solo al trabajar sobre esta integración. Estas decisiones pertenecen a este caso;
verificar el código y las instrucciones actuales antes de reutilizarlas.

## Contrato acordado

- Aplicación .NET 10; descarga de SYSCOM y promoción transaccional en PostgreSQL.
- Código de producto con modelo completo; conservar IDs y referencias históricos.
- Solo ingresar productos cuyo precio seleccionado sea positivo.
- Recalcular globalmente las listas porcentuales para productos elegibles, incluso
  otras empresas. Mantener productos históricos no elegibles y sus listas fijas.
- Convertir entre monedas secundarias usando precio × tasa principal de origen ÷
  tasa principal de destino. Usar tasas configuradas, sin valores incrustados.
- Interpretar principal nulo como falso para monedas existentes; no inventar monedas
  o tipos de cambio faltantes.
- Destino real validado en este caso: base local `one_development`. La configuración
  utiliza `ALUSA_CONNECTION_STRING`; no contiene aquí su valor ni credenciales.

## Puntos de comprobación

Localizar en el checkout vigente `SyscomWorker`, `SyscomDatabaseWorker`,
`SyscomFailureDiagnostics` y `.itia/docs/sincronizacion-syscom.md`.

`syscom_sync_runs` debe terminar en `Succeeded` / `Complete`, con `finished_at`.
Comparar `products_staged` con `syscom_sync_staging` del mismo run y verificar cada
modelo promovido por empresa/código, título, moneda y precio fuente.

Comparar `prices_replaced` con el producto cartesiano de listas porcentuales y
productos elegibles; reconciliar todos los valores persistidos con tasas, cantidad,
porcentaje y redondeo. Verificar que las filas de listas fijas no cambien.

## Qué enseñaron los fallos

- `ModelCodeCollision`: distinguir el modelo completo de una colisión introducida por
  truncamiento. No añadir aliases puntuales sin resolver la limitación del esquema.
- `LegacyCodeIdentityAmbiguous`: no escoger arbitrariamente un ID ante varios candidatos;
  analizar la reconciliación y su regla de identidad acordada.
- SQLSTATE `21000`: un JOIN con unidades SAT duplicadas multiplicaba filas de promoción.
  Resolver una unidad de forma determinista preservando referencias válidas.
- `InvalidPriceCandidates`: expected_count igual a actual_count no demuestra valores
  válidos. Separar invalid_count; investigar precios fuente y conversiones.
- Una consulta de candidatos sobre el catálogo anterior no sustituye verificar el
  catálogo actualizado tras la promoción completa.

## Resultado histórico, no garantía vigente

El 30 de octubre de 2026, la revisión `c844751` completó una ejecución real hacia la
base local en 105.9 s: 100 páginas, 46651 registros recibidos, 32728 modelos únicos y
583470 precios recalculados. Se conservaron 65355 IDs y las filas de listas fijas.
La documentación quedó en `22d3b23`. Estos números ilustran la aceptación y no deben
usarse como conteos esperados de futuras ejecuciones.

No se capturó antes una relación completa código→ID: se verificó la conservación del
conjunto de IDs y la coincidencia del staging, pero no se demostró independientemente
la identidad semántica de cada registro. Capturar esa relación en nuevas verificaciones.
