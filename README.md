# IDOneExtensions

This repository contains extensions and data access components for IDigital/IDOne. It includes an IDOneRepository project implementing the Unit of Work + Repository pattern over Entity Framework Core for PostgreSQL.

## Syscom
Visit [README.md](Syscom/README.md) in the Syscom folder for more information.

### Running the Syscom sync (SyscomApp)
`Syscom/SyscomApp` is a console app that syncs the Syscom catalog into the database and then exits, so it can be
run by any scheduler (Heroku Scheduler, cron, etc.).

Configuration is read only from environment variables; nothing is hardcoded and the app fails fast if one is missing:

| Variable               | Description                                                                         |
|------------------------|-------------------------------------------------------------------------------------|
| `ALUSA_CONNECTION_STRING`    | Npgsql connection string for the IDOne database (not a `postgres://` URL)           |
| `SYSCOM_CLIENT_ID`     | Syscom API client id                                                                |
| `SYSCOM_CLIENT_SECRET` | Syscom API client secret                                                            |

Run locally:
```bash
export ALUSA_CONNECTION_STRING="Host=<host>;Port=5432;Database=<db>;User ID=<user>;Password=<password>;SSL Mode=Require"
export SYSCOM_CLIENT_ID="<value>"
export SYSCOM_CLIENT_SECRET="<value>"
dotnet run --project Syscom/SyscomApp
```

#### Deploy on Heroku with Heroku Scheduler
See [README.heroku.md](README.heroku.md) for the complete deployment process (config vars, image build and release,
manual test run, hourly Scheduler job running `/app/bin/run-syscom`, operations and troubleshooting).

## IDOneRepository
Location: Syscom/IDOneRepository

Features:
- EF Core DbContext (IDOneDbContext) with tables:
  - portfolio_products
  - catalogs_sat_products
  - catalogs_measurements
  - currencies
- Generic repository (IRepository<T>) with async CRUD/query helpers
- Upsert support via FlexLabs.EntityFrameworkCore.Upsert (single and batch) on IRepository<T>
- Unit of Work (IUnitOfWork) exposing repositories and transaction helpers
- PostgreSQL SQL schema script to create all tables
- Documentation for scaffolding models from an existing PostgreSQL database

### Install
Add the project reference to your solution or reference the compiled assembly.

### Configure DbContext and UnitOfWork via DI (example)
```csharp
services.AddDbContext<IDOneDbContext>(options =>
    options.UseNpgsql(Configuration.GetConnectionString("IDOneDb")));
services.AddScoped<IUnitOfWork, UnitOfWork>();
```

### Usage example
```csharp
await using var uow = provider.GetRequiredService<IUnitOfWork>();
var usd = (await uow.Currencies.Query().FirstOrDefaultAsync(c => c.Code == "USD"))!;
await uow.PortfolioProducts.AddAsync(new PortfolioProduct
{
    Sku = "SKU-001",
    Name = "Demo",
    CurrencyId = usd.Id,
    UnitPrice = 100m
});
await uow.SaveChangesAsync();

// Upsert example (insert if missing, else update price and name by SKU)
await uow.PortfolioProducts.UpsertAsync(
    new PortfolioProduct { Sku = "SKU-001", Name = "New Name", CurrencyId = usd.Id, UnitPrice = 120m },
    match: p => new { p.Sku },
    whenMatched: (src, dst) => new PortfolioProduct
    {
        // keep the primary key from destination
        Id = dst.Id,
        Sku = dst.Sku,
        Name = src.Name,
        CurrencyId = src.CurrencyId,
        UnitPrice = src.UnitPrice
    });
```

### Model Generation Docs
- See: Syscom/IDOneRepository/docs/GENERATE_MODELS.md
