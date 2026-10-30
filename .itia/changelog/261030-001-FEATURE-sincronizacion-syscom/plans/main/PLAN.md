# main: Mejorar la sincronización SYSCOM

## Identificación y alcance

- Cambio: [261030-001-FEATURE-sincronizacion-syscom](../../README.md).
- Plan: `main`.
- Origen: solicitud del usuario de analizar y proponer mejoras a la sincronización.
- Objetivo: mejorar aislamiento, consumo de memoria y recuperación, conservando el recálculo global.
- Dependencias: reglas comerciales de bajas y entornos de validación pendientes.
- Estado: propuesta; implementación y QA no iniciadas.
- Documentación actual: [sincronización SYSCOM](../../../../docs/sincronizacion-syscom.md).

## Tareas propuestas

| ID | Alcance y aceptación | Responsable | Carácter | Dependencias | Progreso | Entrega | QA independiente |
| --- | --- | --- | --- | --- | --- | --- | --- |
| secretos-pruebas | Externalizar credenciales y aislar pruebas; criterios de sección 5 | Sin asignar | Requerida | Configuración de ejecutores | No iniciado | Pendiente | Pendiente |
| aislamiento | run_id y exclusión mutua; criterios de sección 1 | Sin asignar | Requerida | secretos-pruebas | No iniciado | Pendiente | Pendiente |
| carga-paginada | Persistir por página; criterios de sección 2 | Sin asignar | Requerida | aislamiento | No iniciado | Pendiente | Pendiente |
| recuperacion-http | Recuperación y resultados operativos; criterios de sección 3 | Sin asignar | Requerida | secretos-pruebas | No iniciado | Pendiente | Pendiente |
| promocion-precios | Promoción atómica con alcance global; criterios de sección 4 | Sin asignar | Requerida | carga-paginada | No iniciado | Pendiente | Pendiente |


Estado: propuesta, sin cambios al comportamiento de ejecución.

## Decisiones

- Preservar el recálculo global: todas las listas con percent_reference y todos los
  productos elegibles, sin restringirlo a SYSCOM ni a una empresa.
- Mantener el scheduler externo y el sentido SYSCOM → IDOne.
- Separar la descarga de la promoción a las tablas finales.

## 1. Aislar cada ejecución y controlar productos ausentes

Añadir una ejecución con run_id, inicio, fin, estado y contadores. Guardar staging por
(run_id, code); promover exclusivamente una ejecución cuya descarga esté completa.
Impedir dos sincronizaciones simultáneas mediante un bloqueo PostgreSQL mantenido
durante la operación. Retener ejecuciones anteriores por un periodo configurable.

Registrar productos ausentes respecto de la última ejecución exitosa, sin borrarlos
automáticamente: definir antes la regla comercial de inactivación y la identificación
de productos propiedad de SYSCOM. Una falla de descarga nunca debe producir bajas.

Aceptación: una segunda ejecución no reutiliza filas viejas; una falla intermedia no
promueve datos incompletos; una ejecución concurrente no modifica el catálogo.

## 2. Descargar y persistir por página

Procesar cada página al recibirla, con lotes acotados y deduplicación por código dentro
de la ejecución. Establecer una regla determinista para modelos presentes en varias
categorías y detectar colisiones por truncamiento del código. Mantener concurrencia 1
inicialmente; medir tiempos y límites de SYSCOM antes de aumentar el paralelismo.
Validar avance de página para evitar bucles ante respuestas inconsistentes.

Aceptación: memoria acotada por lote; catálogo equivalente al recorrido completo;
respuestas con páginas repetidas terminan con un error identificable.

## 3. Recuperación HTTP y resultado operativo

Guardar la expiración del token y renovarlo anticipadamente; ante 401 renovar y
repetir una sola vez. Reintentar consultas GET ante 429 y errores transitorios con
límite de intentos, backoff, jitter y respeto de Retry-After. No reintentar errores
permanentes ni operaciones SQL destructivas de manera independiente.

Configurar timeout por petición y duración máxima de ejecución. Registrar resultado
Succeeded, Failed o Cancelled, etapa y contadores; diferenciarlo del log de duración.
Definir códigos de salida que el scheduler pueda interpretar y alertar por fallas.
Nunca registrar tokens, credenciales ni cadenas de conexión.

Aceptación: pruebas simuladas de expiración, 401 persistente, 429, 5xx, timeout y
cancelación; no hay reintentos infinitos ni secretos en logs.

## 4. Promoción atómica y recálculo global

Ejecutar promoción de SAT, unidades, productos y reemplazo global de precios dentro
de una transacción explícita. Preparar y validar el conjunto nuevo de precios antes
de reemplazar el anterior. Detectar tipos de cambio faltantes, conversiones no
soportadas y precios nulos; abortar sin perder los precios vigentes.

Preservar el alcance global aprobado. Medir duración, filas afectadas y bloqueo;
evaluar una tabla temporal de precios para reducir el tiempo de reemplazo.

Aceptación: una falla durante el recálculo conserva el estado anterior completo;
productos ajenos a SYSCOM siguen incluidos; ausencia de tipos de cambio no elimina
silenciosamente precios. Validar en PostgreSQL aislado con varias monedas y listas.

## 5. Credenciales sin interrupción

La aplicación de consola actual ya consume variables de entorno. Mover las
credenciales incrustadas de la prueba no cambia ese mecanismo, pero esa prueba
necesitará recibir valores externos para seguir ejecutándose.

Secuencia propuesta:

1. Inventariar los procesos que usan las credenciales sin imprimir sus valores:
   scheduler, ejecución manual, contenedor, CI y prueba de integración.
2. Configurar los valores en el entorno o gestor de secretos de cada ejecutor.
   No usar archivos versionados; un archivo .env por sí solo no es cargado por el
   Program.cs actual.
3. Cambiar la prueba a variables exclusivas de integración y ejecución opt-in,
   contra una base de datos aislada. Fallar con nombres de variables faltantes,
   nunca con sus valores. Evitar que dotnet test dispare una sincronización real.
4. Validar primero presencia de configuración sin conectar; después autenticación
   de solo lectura y conectividad a la base autorizada. Esto no prueba sincronización.
5. Rotar credenciales expuestas coordinando todos los consumidores; conservar una
   vía de reversión mientras se valida. Retirarlas del archivo no elimina el historial.

Conclusión: funciona si el proceso recibe las variables correctas; dejaría de funcionar
si solo se borran o se mueven a un archivo que nadie carga. No se ha verificado la
configuración del scheduler ni de servicios remotos y no se han rotado credenciales.

## Orden de implementación y validación

1. Aislar pruebas y configurar secretos externos.
2. Introducir run_id, exclusión mutua y carga por página.
3. Añadir recuperación HTTP y estados observables.
4. Promoción transaccional y validación del recálculo global.
5. Comparar resultados en una base aislada; medir memoria y duración; después
   programar un despliegue controlado con revisión de resultados y reversión.

La documentación del flujo actual se actualizó mediante inspección del código.
No se ejecutó la sincronización ni se realizaron escrituras en bases remotas.

## Integración y cierre

- Criterios: carga completa, recuperación acotada y promoción atómica; recálculo global preservado.
- Evaluación: pendiente; no hay implementación ni revisión independiente de estas propuestas.
- Decisiones pendientes: política de productos ausentes, responsables y entorno de prueba.
- Alcance confirmado: todas las listas porcentuales y todos los productos elegibles.
