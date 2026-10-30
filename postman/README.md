# Simulación local SYSCOM

La simulación usa datos ficticios y reproduce solo autenticación, categorías y productos.
No es un servidor publicado ni verifica el contrato de una cuenta real. La búsqueda
de mocks del proveedor no pudo realizarse porque Postman no tiene sesión iniciada;
Orbit no devolvió recursos pertinentes. Se reutilizó la forma de las respuestas del
cliente y colección locales. Guía actual: https://developers.syscom.mx/guide.

## Ejecución reproducible

1. Crear PostgreSQL desechable, sin volumen persistente:

   ```sh
   docker run --detach --rm --name idone-sync-tests \
     -e POSTGRES_HOST_AUTH_METHOD=trust -e POSTGRES_DB=idone_sync_tests \
     -p 127.0.0.1::5432 postgres:17-alpine
   docker port idone-sync-tests 5432
   ```

2. En una terminal, iniciar la simulación con Postman CLI >=1.70:

   ```sh
   postman simulation run postman/simulations/syscom-sync/syscom-sync.sim.yaml --port 45123 --no-history
   ```

3. En otra terminal, usar el puerto PostgreSQL mostrado en el paso 1:

   ```sh
   export SYSCOM_TEST_DB_CONNECTION='Host=127.0.0.1;Port=<puerto>;Database=idone_sync_tests;Username=postgres'
   export SYSCOM_SIMULATION_URL=http://127.0.0.1:45123
   dotnet test Syscom/SyscomTestProject
   ```

Las pruebas crean y eliminan únicamente sus esquemas `sync_<uuid>` y se serializan
porque el bloqueo de la sincronización es global por base. La prueba con SYSCOM real
permanece deshabilitada; no configurar `SYSCOM_RUN_INTEGRATION_TESTS` para esta suite.
Sin las dos variables locales, las pruebas correspondientes aparecen como omitidas.

La simulación usa routing por cabecera `x-mock-slug: syscom`, añadida solo por las
pruebas. Los controles `/__control/reset?mode=...` y `/__control/state` son exclusivos
del fixture. Modos: normal, transient (429/401/503), fail-page, repeat-page, missing e incomplete (cantidad anunciada mayor que lo descargado).
Nunca usar credenciales reales ni publicar este mock como contrato del proveedor.

Al terminar, detener la simulación con Ctrl+C y eliminar el contenedor propio:

```sh
docker stop idone-sync-tests
```
