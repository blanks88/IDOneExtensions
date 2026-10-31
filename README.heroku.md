# Lanzamiento de la sincronización SYSCOM en Heroku

`Syscom/SyscomApp` ejecuta una sincronización completa y termina. La imagen se publica
como `web`, manteniendo `web=0`; cada ejecución programada usa un dyno independiente.
El destino previsto es `alusa-syscom-sync`. La configuración remota del programador
no se crea ni cambia por el solo hecho de desplegar el repositorio.

## Estado del lanzamiento

La imagen amd64 está compilada y el runner comprobado. **Aún no se ha desplegado esta
revisión ni comprobado dos ejecuciones programadas.** El usuario confirmó la BD remota compartida con alusa2-api y el plan trial-0
de Advanced Scheduler; falta desplegar la API con su fase release y verificar el esquema.

[Plan y estado](.itia/changelog/261030-004-CHORE-lanzamiento-syscom-heroku/README.md).
[Reglas y diagnóstico de la sincronización](.itia/docs/sincronizacion-syscom.md).

## Prerrequisitos

- Heroku CLI autenticada y Docker disponible; comandos desde la raíz del checkout.
- Identificar la BD efectiva sin imprimir su conexión ni credenciales.
- Aplicar la migración `20261030230000_expand_portfolio_product_codes.rb` de IDOneApi
  en la BD autorizada antes de activar el worker. `portfolio_products.code` debe ser
  `text` o `varchar` sin límite. La migración conserva vistas dependientes; aborta ante
  dependencias no soportadas. No sustituirla por un ALTER aislado ni truncar modelos.
- Capturar IDs, relación código→ID y filas completas de listas fijas antes de ejecutar.
- Comprobar que no exista otro job SYSCOM activo sobre la misma BD.

## Configuración

Obligatorias: `ALUSA_CONNECTION_STRING` en formato Npgsql, `SYSCOM_CLIENT_ID` y
`SYSCOM_CLIENT_SECRET`. La variable antigua `CONNECTION_STRING` no es leída por el host.
Al migrarla, conservar su valor mediante un canal que no lo imprima en consola ni lo
incluya en Git. No trasladar la conexión local del desarrollador al entorno Heroku.

Usar SSL para la BD remota. Evitar `Include Error Detail=true` y no copiar respuestas
de configuración, tokens o connection strings a logs, documentación o PRs. Ante una
rotación de credenciales, actualizar la conexión Npgsql configurada.

Opcionales: `SYSCOM_PAGE_SIZE` (500), `SYSCOM_BUFFERED_PAGES` (2),
`SYSCOM_COMPANY_ID` (1), `SYSCOM_REQUEST_TIMEOUT_SECONDS` (30),
`SYSCOM_MAXIMUM_DURATION_SECONDS` (1800), `SYSCOM_RETENTION_DAYS` (30).
Los valores entre paréntesis son los predeterminados. La empresa limita la promoción
del catálogo; el recálculo de listas porcentuales sigue siendo global.

## Compilar sin emular el SDK

El SDK usa `BUILDPLATFORM` y publica para `TARGETARCH`; la imagen final sigue siendo
`linux/amd64`. Esto evita ejecutar dotnet restore bajo QEMU en equipos ARM, donde
se reprodujo una terminación por señal 11. BuildKit/Buildx es requisito para estos
argumentos automáticos. Los diagnósticos locales y worktrees se excluyen del contexto.

## Construir y desplegar

```sh
docker build --platform linux/amd64 \
  -f Syscom/SyscomApp/Dockerfile \
  -t registry.heroku.com/alusa-syscom-sync/web .
heroku container:login
docker push registry.heroku.com/alusa-syscom-sync/web
heroku container:release web -a alusa-syscom-sync
heroku ps:scale web=0 -a alusa-syscom-sync
```

Registrar la revisión, digest de imagen y release desplegados. El runner
`/app/bin/run-syscom` usa `exec dotnet /app/SyscomApp.dll`, conserva salida y señales.
La imagen usa `CMD` para permitir que el programador establezca el comando.
Si existe una formación `worker` antigua, verificar que no ejecute otra sincronización.

Una prueba manual puede diagnosticar el despliegue, pero no cuenta como prueba de
periodicidad:

```sh
heroku run -a alusa-syscom-sync -x '/app/bin/run-syscom'
```

## Validar cada cinco minutos y pasar a diario

Heroku Scheduler básico no ofrece cinco minutos. La configuración propuesta usa
**Advanced Scheduler**, cuyo plan debe confirmarse antes de provisionarlo; el coste
del add-on es adicional al consumo de dynos. La prueba de 100 ejecuciones tiene un
límite finito y no debe confundirse con operación permanente sin límite.

Crear un único trigger con estos valores:

| Campo | Validación inicial | Operación diaria, tras dos éxitos |
|---|---|---|
| Comando | `/app/bin/run-syscom` | Sin cambio |
| Cron | `*/5 * * * *` | `0 3 * * *` |
| Zona horaria | `America/Mexico_City` | Sin cambio |
| Dyno | `standard-1x` | Sin cambio |

No medir los cinco minutos desde el final de una corrida: comparar los instantes
programados de dos ejecuciones distintas. Mantener el mismo trigger al cambiar a diario.

Para contar cada ejecución se requiere conjuntamente:

1. Origen `scheduled`, estado `task-succeeded`, salida 0 y ejecución identificable.
2. Un `run_id` distinto en la BD, `Succeeded` / `Complete` y `finished_at` informado.
3. Coincidencia entre staging y productos promovidos, precios globales correctos,
   ausencia de duplicados inesperados e integridad de IDs y listas fijas.

Después de dos éxitos, actualizar el cron a diario y consultar de nuevo el trigger
para comprobar su estado activo, zona horaria y próxima ejecución. Esa consulta
confirma la configuración; no demuestra todavía una ejecución a las 03:00.

El advisory lock PostgreSQL evita promociones simultáneas; una invocación que no
obtenga el bloqueo sale con código 4 y no cuenta como éxito. Si una corrida supera
cinco minutos, analizar su duración y las ejecuciones solapadas antes de aceptar la
periodicidad. No ocultar errores ni aumentar tiempos para obtener una aceptación.

## Diagnóstico, fixes y cierre

Los logs son Information y contienen etapas, `run_id`, motivo seguro, SQLSTATE y
contadores. No imprimir excepciones completas ni payloads con secretos.

```sh
heroku logs --tail -a alusa-syscom-sync --dyno advanced-scheduler
heroku ps -a alusa-syscom-sync
heroku releases -a alusa-syscom-sync
```

Si falla, analizar el log y la BD, mejorar el diagnóstico solo cuando falte información,
reproducir la causa y agregar una regresión. Cada fix lleva su commit y documentación;
volver a desplegar y comprobar la ejecución completa. Solo cerrar después de dos
corridas programadas válidas y del cambio diario verificado. Preparar el PR con los
resultados reales y obtener el VoBo aplicable antes de publicarlo.

Referencias oficiales: [Heroku Scheduler](https://devcenter.heroku.com/articles/scheduler),
[Advanced Scheduler](https://devcenter.heroku.com/articles/advanced-scheduler),
[Container Registry](https://devcenter.heroku.com/articles/container-registry-and-runtime).
