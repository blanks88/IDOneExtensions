# Entrega — modelos completos y reconciliación

## Alcance y resultado

Se conserva el modelo completo en portfolio_products y staging; se elimina el alias
puntual. Los códigos antiguos se reconcilian actualizando el mismo ID. Un candidato
único por prefijo/alias puede tener un título cambiado; múltiples candidatos exigen
un título único. Destinos ocupados o identidades ambiguas impiden la promoción.
Los renombres y el recálculo global de precios comparten transacción. Diagnósticos
seguros identifican validaciones sin publicar mensajes externos ni credenciales.

## Verificación

Release:94 pruebas PASS,9 omitidas (SYSCOM real y simulador). Debug compila sin
advertencias ni errores. PostgreSQL17 desechable; regresiones de colisión, orden,
repetición, alias histórico, títulos cambiados, identidad ambigua, destino ocupado,
IDs/referencias/listas fijas y rollback. El test de título cambiado falló antes del
ajuste y pasó después. La consulta de solo lectura del run reportado dejó0 candidatos
sin resolver con la regla nueva. No se ejecutó una sincronización real para validarlo.

IDOneApi:4 pruebas independientes de migración y validación PASS; no se ejecutó la
suite Rails completa. Se amplía code a text conservando vistas ordinarias, permisos,
propietarios y comentarios; el rollback rechaza truncar datos. Las dependencias no
soportadas detienen la migración transaccionalmente.

## Entrega y límites

Implementación local verificada; sin revisión independiente nueva de este fix.
No se aplicó migración productiva ni despliegue. Activar primero la migración API,
luego el worker; no volver a ejecutar el worker antiguo que recorta códigos.
Pendientes ajenos: development.rb en API; .gitignore del árbol principal Extensions;
configuración local de conexión fuera de Git. No forman parte del commit.

[Operación y reglas](../../../../../../../docs/sincronizacion-syscom.md).

## Corrección posterior — unidades SAT duplicadas

La promoción falló con SQLSTATE21000 por varias unidades con la misma clave SAT
y tipo. Se reprodujo con un test fallido en PostgreSQL desechable. JOIN LATERAL
selecciona una sola unidad: conserva la referencia válida del producto existente
o usa el menor ID válido, sin borrar ni fusionar unidades. Se añade diagnóstico
SQLSTATE validado y clasificación estática sin publicar Message/Detail/Hint.

Validación posterior:96 PASS,9 SKIP; Debug sin advertencias ni errores. La regresión
verifica dos ejecuciones, referencias, cantidades y precios globales/lista fija.
La consulta de solo lectura del run local devuelve32729 filas para32729 códigos.
No se ejecutó promoción real ni nueva revisión independiente.

## Corrección posterior — elegibilidad de precios y conversión cruzada

Con aprobación del usuario, staging admite únicamente precios positivos y el recálculo
global usa todos los productos elegibles de todas las empresas. Los productos históricos
con precio nulo, cero o negativo permanecen con sus IDs y listas fijas; no generan
precios porcentuales. EUR/USD usa las tasas principales configuradas de ambas monedas.
Los flags principal nulos se interpretan como secundarios solo si existe la moneda.
El diagnóstico separa invalid_count de los conteos esperado y generado.

Validación: 100 PASS, 9 SKIP; consulta local de solo lectura: 521550 candidatos,
0 inválidos. Las regresiones incluyen tasas modificadas entre ejecuciones, conversión
en ambos sentidos y preservación de productos históricos/listas fijas.
Sin ejecución real completa ni nueva revisión independiente.
