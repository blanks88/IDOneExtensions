# Lanzamiento Heroku — ciclo operativo completo

API `alusa2-api`: commit `d53a53d`, release `v19` succeeded/current. Su fase release
aplicó la migración `20261030230000`; `portfolio_products.code=text`.
Worker `alusa-syscom-sync`: release `v5` succeeded/current, imagen linux/amd64,
formación `web=0`, dynos programados Standard-1X.

Se provisionó Advanced Scheduler trial-0 (100 ejecuciones) con autorización del
usuario. Trigger `cd2dc8ac-742e-48c7-9698-e6e282bd317c`, comando
`/app/bin/run-syscom`, zona `America/Mexico_City`, timeout 1800. La prueba cada cinco
minutos completó dos éxitos; el mismo trigger quedó activo con `0 3 * * *`.
Se verificó el cron persistido y la zona. Próxima fecha calculada: 31/10/2026 03:00
locales (09:00 UTC); el dashboard no muestra un campo de próxima fecha y no se afirma
haber observado aún una corrida diaria.

Se copió la conexión remota existente a `ALUSA_CONNECTION_STRING` sin imprimir
secretos. La comparación normalizada confirmó que la BD es la misma que DATABASE_URL
de `alusa2-api`; la comparación inicial sin normalizar había informado una diferencia.

El baseline anterior al despliegue contiene 65,322 identidades y un hash de las filas
completas de listas fijas. Tras la primera corrida programada: todos los IDs y sus
empresas se conservan; siete códigos reconciliados coinciden con modelos completos
aceptados a partir del código legado de 35 caracteres. El hash fijo sigue idéntico.

## Validación

- Imagen Buildx amd64 compilada; runner sin configuración termina con salida 2 sin secretos.
- Pruebas .NET: 61 aprobadas, 39 omitidas por requerir integración; ninguna fallida.
- Migración API: cuatro pruebas aisladas aprobadas; ejecución real de release exitosa.
- Dos revisores GPT-6 Sol, esfuerzo medio, verifican despliegue e integridad en lectura.
- Primera programada 18:15: `21a02488-bafc-458b-b46e-9e65014a0a79`, dyno
  `advanced-scheduler.1866`, origen scheduled, scheduler succeeded y proceso salida 0.
  100 páginas, 32,728 productos staged, 583,524 precios globales; total 113,859 ms.
- Auditoría independiente: 0 diferencias de promoción y 0 precios globales inválidos,
  faltantes, adicionales o distintos a la fórmula con tasas configuradas.
- La corrida manual anterior no cuenta como evidencia de periodicidad.

## Fix — SDK nativo durante compilación cruzada

El SDK amd64 ejecutado por QEMU en el builder ARM falló en dotnet restore con señal11
(salida139). El Dockerfile ahora ejecuta el SDK en BUILDPLATFORM y restaura/publica
para TARGETARCH, manteniendo runtime amd64 y publicación framework-dependent.
Se excluyen worktrees y diagnósticos temporales del contexto Docker.
Validación: compilación Buildx completa, imagen amd64 y runner con salida2 al faltar
configuración. Revisión independiente sin bloqueos. El código funcional no cambió.

API desplegada: d53a53d, release v19 succeeded. Migración20261030230000 aplicada,
code=text. Comparación posterior:65322 identidades previas y listas fijas intactas;
web.1/worker.1 up. La activación posterior se valida en las ejecuciones programadas de esta entrega.

## Fix — formato de manifiesto compatible con Heroku

El push original subió capas pero Heroku rechazó el manifiesto OCI con unsupported.
La inspección local confirmó application/vnd.oci.image.manifest.v1+json. Se corrigió
el procedimiento para publicar directamente desde Buildx con oci-mediatypes=false,
provenance=false y sbom=false. El registro aceptó la imagen y la inspección remota
confirmó application/vnd.docker.distribution.manifest.v2+json.
Digest: sha256:8ad727820a1e89286ca8a912ddee5d5bc7c4d36cd00d7121c1e8b6ef9e49c56e.
Este fix afecta el formato de publicación; no altera las reglas de sincronización.

## Segunda ejecución y cierre

Segunda programada 18:20: `ce4f0374-6504-4847-9f67-e359901633ca`, dyno
`advanced-scheduler-cd2dc8ac-742e-48c7-9698-e6e282bd317c.9655`, origen scheduled,
scheduler succeeded y proceso salida 0 a las 00:22:18 UTC del 31/10/2026.
100 páginas, 32,728 staged, 583,524 precios, total 121,121 ms.
Ambas ejecuciones están separadas por cinco minutos en el calendario del scheduler.
Auditoría independiente de la segunda: cero diferencias de promoción/precios,
65,322 IDs originales y empresas conservados, siete códigos reconciliados válidos,
hash de listas fijas idéntico. El campo finished_at de BD se fija antes de concluir
la promoción; el fin real se verificó con logs del proceso y métricas.

El plan trial-0 tiene 100 ejecuciones; no se contrató un plan de pago. El horario
diario permanece configurado, sujeto a los límites del trial y disponibilidad externa.
Los cambios ajenos del main tree y de config/environments/development.rb se excluyeron.
La publicación GitHub y el PR requieren VoBo del contenido y alcance final; aún no
se han publicado las ramas de este lanzamiento.
