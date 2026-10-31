---
name: syscom-sync-to-completion
description: Ejecutar, diagnosticar y corregir una sincronización o importación hasta verificar su resultado completo en la base destino, con un commit por fix y documentación actualizada. Usar cuando se pide cerrar un fallo de punta a punta; no para asumir autorización de producción ni para configurar un monitor recurrente.
---

# Sincronización hasta completar

El resultado es una ejecución real completa y comprobada en el destino autorizado.
Compilar, pasar pruebas o superar la etapa que fallaba son avances, no el cierre.

## Preparar una aceptación verificable

- Identificar checkout, rama, instrucciones del repositorio, cambios pendientes y documento operativo. Conservar cambios ajenos; no crear otro árbol si el existente sirve.
- Confirmar mediante configuración efectiva el proveedor y el destino, sin mostrar secretos. Una conexión disponible no autoriza sus escrituras. Aprovechar autorización ya otorgada; preguntar solo si falta el entorno o el alcance de la mutación. No trasladar una autorización local a producción.
- Fijar las invariantes acordadas: alcance global o parcial, empresas, identidad de productos, listas fijas, monedas y tasas, elegibilidad de datos, ausentes y efectos externos. No cambiar una regla de negocio para conseguir un estado exitoso.
- Capturar antes de la ejecución las referencias necesarias para compararlas después: conjunto de IDs, relación clave de negocio→ID y referencias protegidas. Para filas que deben quedar idénticas, usar una comparación exacta o un hash con orden y serialización deterministas. Registrar cualquier línea base que no pudo capturarse.
- Reutilizar pruebas y contratos existentes. Guardar logs y snapshots temporales en una ubicación local ignorada por Git; no versionar datos del catálogo, respuestas completas, credenciales ni volcados como documentación.

## Orquestar cuando se solicite un equipo

Usar las herramientas de subagentes disponibles y respetar modelo/esfuerzo seleccionados por el usuario. No fijar universalmente Sol ni sustituir el modelo solicitado en silencio.

Separar responsabilidades útiles:

| Responsable | Trabajo | Escrituras |
| --- | --- | --- |
| Orquestador | Ejecutar el ciclo, decidir hipótesis, integrar y hacer commits | Ejecución real y archivos compartidos |
| Diagnóstico | Revisar logs, código y causas posibles | Lectura; correcciones solo con propiedad explícita de archivos |
| Verificación | Contrastar aceptación e integridad | Consultas de lectura; pruebas aisladas si se asignan |

Un solo responsable controla las ejecuciones sobre la base compartida y el índice Git.
No lanzar sincronizaciones concurrentes para acelerar el diagnóstico. Asignar archivos
exclusivos a cada escritor y esperar su entrega antes de verificar la revisión final.
Si no hay agentes disponibles, ejecutar el mismo ciclo y declarar que no hubo revisión independiente.

## Ciclo de ejecución y corrección

1. **Ejecutar completo.** Compilar la revisión actual y usar el punto de entrada real contra el proveedor y destino autorizados. Recoger código de salida, identificador de ejecución, estado persistido, etapas y tiempos. Confirmar que los endpoints son los previstos: una simulación no demuestra una sincronización real.
2. **Ubicar el fallo.** Correlacionar logs con la ejecución y consultar el estado persistido en lectura. Distinguir descarga incompleta, staging, reconciliación, promoción, recálculo y commit. No atribuir una causa solo al tipo genérico de excepción.
3. **Obtener información suficiente.** Si faltan datos, instrumentar el punto donde se pierde el contexto. Preferir campos estructurados permitidos: etapa, motivo estable, SQLSTATE validado, página/categoría, conteos esperado/real/inválido y IDs técnicos necesarios. No registrar indiscriminadamente Message, InnerException, respuestas HTTP, SQL con valores o connection strings. Probar que los errores con secretos no los filtran.
4. **Probar una causa.** Formular una hipótesis y reproducirla con una prueba mínima o datos aislados. Si no se reproduce, reunir evidencia; no acumular cambios especulativos. Una nueva falla en una etapa posterior puede ser otro defecto: identificarla por separado.
5. **Corregir y validar.** Agregar una regresión que falle antes del cambio, aplicar la corrección mínima y ejecutar las pruebas afectadas. Verificar rollback, referencias y reglas que el cambio pueda afectar. No ocultar el problema omitiendo filas válidas, debilitando validaciones o aumentando tiempos sin causa demostrada.
6. **Documentar y hacer commit.** Un commit por corrección coherente, con sus pruebas y documentación relevante; instrumentación independiente puede tener su propio commit. Revisar el diff y agregar rutas explícitas. Respetar instrucciones del repositorio y una eventual petición de no hacer commit. No incluir cambios ajenos ni equiparar commit con permiso de push.
7. **Repetir desde la ejecución completa.** Usar el binario que corresponde al código corregido, no uno anterior. No finalizar con “vuelve a probar” cuando el agente dispone de autorización, datos y herramientas para hacerlo.

No repetir ciegamente una operación que puede duplicar efectos. Ante un timeout durante
el commit, comprobar primero el estado durable y la idempotencia antes de reintentar.
Respetar rate limits y reintentos acotados del proveedor. Si dos hipótesis se descartan,
volver a reunir evidencia; si tres correcciones no resuelven el mismo fallo, revisar el
diseño y presentar las decisiones necesarias antes de ampliar el alcance. No reejecutar
indefinidamente sin cambios ni información nueva.

Detener la parte dependiente y explicar el bloqueo cuando falten credenciales,
autorización, disponibilidad externa o una decisión de negocio. Continuar el trabajo
independiente posible. Nunca declarar éxito por agotar intentos.

## Comprobar el destino antes de cerrar

Exigir conjuntamente, según el contrato del sistema:

- Salida exitosa y estado final durable; ausencia de ejecución activa o transacción sin resolver.
- Todas las páginas/categorías esperadas procesadas, con omisiones y duplicados explicados. Registros recibidos no equivalen necesariamente a entidades únicas.
- Todas las entidades válidas del staging promovidas, con coincidencia de campos relevantes.
- Conteos y valores recalculados correctos en todo el alcance acordado, sin faltantes, sobrantes ni duplicados inesperados. Comparar datos persistidos, no solo el contador de éxito.
- Conservación de IDs y referencias; contrastar también clave de negocio→ID cuando se afirma identidad semántica. Conservar el conjunto de IDs por sí solo no prueba que cada ID siga representando la misma entidad.
- Contenido e identidad de las filas protegidas sin cambios, usando exactamente la comparación capturada antes.

Si falla una comprobación de integridad aunque el proceso termine con salida 0,
volver al diagnóstico. No repetir una ejecución exitosa solo por rutina; una segunda
pasada se justifica si falta demostrar idempotencia u otra condición de aceptación.

## Cerrar y actualizar documentación

Actualizar el documento operativo existente: reglas vigentes, flujo, diagnóstico,
ciclo de corrección, revisión probada y resumen de validación. Distinguir pruebas
unitarias, integración aislada, simulación y ejecución real. Incluir omisiones y límites
sin convertir logs o snapshots en un expediente versionado. Un cierre documental
posterior puede tener un commit separado del fix, sin alterar el código validado.

Informar entorno, duración, conteos relevantes, invariantes verificadas, commits,
archivo actualizado y límites restantes. No publicar ni hacer push sin la autorización
aplicable. No crear un monitor o tarea recurrente por inferencia.

Para adaptar este flujo a SYSCOM/IDOne, consultar [el caso de referencia](references/syscom-idone.md).
