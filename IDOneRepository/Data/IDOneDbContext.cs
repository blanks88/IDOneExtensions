using IDOneRepository.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IDOneRepository.Data;

// ReSharper disable once InconsistentNaming
public partial class IDOneDbContext : DbContext
{
    public IDOneDbContext()
    {
    }

    public IDOneDbContext(DbContextOptions<IDOneDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<CatalogsMeasurement> CatalogsMeasurements { get; set; }

    public virtual DbSet<CatalogsSatProduct> CatalogsSatProducts { get; set; }

    public virtual DbSet<Currency> Currencies { get; set; }

    public virtual DbSet<PortfolioProduct> PortfolioProducts { get; set; }

    public virtual DbSet<SyscomProducts> SyscomProductsStaging { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("heroku_ext", "pg_stat_statements");

        modelBuilder.Entity<CatalogsMeasurement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("catalogs_measurements_pkey");
        });

        modelBuilder.Entity<CatalogsSatProduct>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("catalogs_sat_products_pkey");
        });

        modelBuilder.Entity<Currency>(entity => { entity.HasKey(e => e.Id).HasName("currencies_pkey"); });

        modelBuilder.Entity<PortfolioProduct>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("portfolio_products_pkey");

            entity.Property(e => e.MinStock).HasDefaultValueSql("0.0");

            entity.HasOne(d => d.Currency).WithMany(p => p.PortfolioProducts).HasConstraintName("fk_rails_ec29e2277d");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}