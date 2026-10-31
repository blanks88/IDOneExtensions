# Lanzamiento Heroku — en curso

La app alusa-syscom-sync existe con stack container. Se provisionó Advanced Scheduler
trial-0 con autorización del usuario. Trigger cd2dc8ac-742e-48c7-9698-e6e282bd317c guardado inactivo: cada cinco minutos,
America/Mexico_City, Standard-1X, timeout1800. Sin corridas programadas aún.
Se copió la conexión remota existente a ALUSA_CONNECTION_STRING sin imprimir secretos.
La comparación normalizada confirmó que esta BD es la misma que DATABASE_URL de
alusa2-api; una comparación inicial sin normalizar había informado una diferencia.

La BD conserva code varchar(35),65322 productos y migraciones hasta20260622233000.
Se capturaron IDs,código→ID y hash de listas fijas antes de modificarla. El usuario
pidió desplegar IDOneApi para aplicar la migración existente por la vía de Rails.
La API desplegada está en8162002; el fix2d8ff2d aún no llegó a Heroku. El commit local
d53a53d añade release: bundle exec rails db:migrate; cuatro pruebas aisladas pasaron.
El despliegue de ese commit está pendiente del VoBo concreto requerido para publicación.
No se ha migrado todavía la BD remota.

Imagen Docker amd64 del worker compilada desde78c5b24. Runner verificado sin
credenciales: salida2 y nombres de variables requeridas, sin datos sensibles.
Dos agentes Sol/medium revisaron despliegue y periodicidad. No se ha publicado la
imagen, ejecutado corridas programadas ni creado PR.

## Fix — SDK nativo durante compilación cruzada

El SDK amd64 ejecutado por QEMU en el builder ARM falló en dotnet restore con señal11
(salida139). El Dockerfile ahora ejecuta el SDK en BUILDPLATFORM y restaura/publica
para TARGETARCH, manteniendo runtime amd64 y publicación framework-dependent.
Se excluyen worktrees y diagnósticos temporales del contexto Docker.
Validación: compilación Buildx completa, imagen amd64 y runner con salida2 al faltar
configuración. Revisión independiente sin bloqueos. El código funcional no cambió.

API desplegada: d53a53d, release v19 succeeded. Migración20261030230000 aplicada,
code=text. Comparación posterior:65322 identidades previas y listas fijas intactas;
web.1/worker.1 up. Todavía no se activó el trigger de sincronización.

## Fix — formato de manifiesto compatible con Heroku

El push original subió capas pero Heroku rechazó el manifiesto OCI con unsupported.
La inspección local confirmó application/vnd.oci.image.manifest.v1+json. Se corrigió
el procedimiento para publicar directamente desde Buildx con oci-mediatypes=false,
provenance=false y sbom=false. El registro aceptó la imagen y la inspección remota
confirmó application/vnd.docker.distribution.manifest.v2+json.
Digest: sha256:8ad727820a1e89286ca8a912ddee5d5bc7c4d36cd00d7121c1e8b6ef9e49c56e.
Este fix afecta el formato de publicación; no altera las reglas de sincronización.
