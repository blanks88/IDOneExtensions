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
