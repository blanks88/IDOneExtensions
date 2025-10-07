# Execution Summary

| Step | Description                                       | Component                                     |
|------|---------------------------------------------------|-----------------------------------------------|
| 1️⃣  | Create daily table "YYYYMMDD_products"            | SyscomStagingImporter.CreateStagingTableAsync |
| 2️⃣  | Fetch all categories/products via API             | ISyscomClient.GetProductsAsync()              |
| 3️⃣  | Bulk insert (COPY) to staging                     | SyscomImportWorker.BulkInsertAsync()          |
| 4️⃣  | SQL import to production tables                   | SyscomWorker.BuildImportSql()                 |
| 5️⃣  | Rerun daily – staging table rotates automatically | Fargate task (cron 03:00 MX)                  |



