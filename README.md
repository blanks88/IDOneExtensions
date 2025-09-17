# IDOneExtensions

This repository contains extensions and data access components for IDigital/IDOne. It includes an IDOneRepository project implementing the Unit of Work + Repository pattern over Entity Framework Core for PostgreSQL.

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
