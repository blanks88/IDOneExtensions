# Model Generation (Scaffolding) for PostgreSQL

This project already contains code-first entities for:
- portfolio_products
- catalogs_sat_products
- catalogs_measurements
- currencies

If you prefer database-first, you can scaffold models from an existing PostgreSQL database with EF Core.

Prerequisites:
- Install EF Core tools
  - dotnet tool install --global dotnet-ef
- Ensure Npgsql provider is referenced (already in csproj)

Connection string example:
```
postgres://ucq2a6a2im22q7:pae331ddf86eea08f7b4a031ac422058b0c76e3952d8a293761667d09cb8cd437@ec2-3-211-36-220.compute-1.amazonaws.com:5432/dfcb63f4c9513c
```
- Host=ec2-3-211-36-220.compute-1.amazonaws.com;Port=5432;Database=dfcb63f4c9513c;Username=ucq2a6a2im22q7;Password=pae331ddf86eea08f7b4a031ac422058b0c76e3952d8a293761667d09cb8cd437;Include Error Detail=true

Scaffold only the required tables:
- dotnet ef dbcontext scaffold "Host=ec2-3-211-36-220.compute-1.amazonaws.com;Port=5432;Database=dfcb63f4c9513c;Username=ucq2a6a2im22q7;Password=pae331ddf86eea08f7b4a031ac422058b0c76e3952d8a293761667d09cb8cd437;Include Error Detail=true" Npgsql.EntityFrameworkCore.PostgreSQL \
  --table portfolio_products \
  --table catalogs_sat_products \
  --table catalogs_measurements \
  --table currencies \
  --context IDOneDbContext \
  --output-dir Data/Entities \
  --data-annotations \
  --force

Notes:
- --data-annotations adds attributes instead of Fluent API where possible.
- --force overwrites existing entity classes; omit if you want to keep customizations.
- You can add --no-onconfiguring if you plan to configure the DbContext via DI only.

Creating migrations (for code-first):
- dotnet ef migrations add InitCatalogsAndPortfolio
- dotnet ef database update

Applying SQL manually:
- See sql/schema.sql in this project for PostgreSQL DDL you can run directly.
