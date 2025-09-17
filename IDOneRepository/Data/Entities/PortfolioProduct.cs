using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace IDOneRepository.Data.Entities;

[Table("portfolio_products")]
[Index("CatalogsMeasurementId", Name = "index_portfolio_products_on_catalogs_measurement_id")]
[Index("CatalogsSatProductId", Name = "index_portfolio_products_on_catalogs_sat_product_id")]
[Index("CompanyId", Name = "index_portfolio_products_on_company_id")]
[Index("CurrencyId", Name = "index_portfolio_products_on_currency_id")]
[Index("ParentId", Name = "index_portfolio_products_on_parent_id")]
[Index("CompanyId", "Code", Name = "ux_portfolio_products_company_code", IsUnique = true)]
public partial class PortfolioProduct
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("title")]
    [StringLength(350)]
    public string? Title { get; set; }

    [Column("code")]
    [StringLength(35)]
    public string? Code { get; set; }

    [Column("catalogs_measurement_id")]
    public long? CatalogsMeasurementId { get; set; }

    [Column("details")]
    public string? Details { get; set; }

    [Column("parent_id")]
    public int? ParentId { get; set; }

    [Column("created_at", TypeName = "timestamp without time zone")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at", TypeName = "timestamp without time zone")]
    public DateTime UpdatedAt { get; set; }

    [Column("company_id")]
    public long? CompanyId { get; set; }

    [Column("currency_id")]
    public long? CurrencyId { get; set; }

    [Column("price")]
    [Precision(14, 4)]
    public decimal? Price { get; set; }

    [Column("catalogs_sat_product_id")]
    public long? CatalogsSatProductId { get; set; }

    [Column("trade_mark")]
    [StringLength(70)]
    public string? TradeMark { get; set; }

    [Column("ecommerce_status")]
    public int? EcommerceStatus { get; set; }

    [Column("pr_measurement_id")]
    public int? PrMeasurementId { get; set; }

    [Column("pr_quantity")]
    [Precision(14, 4)]
    public decimal? PrQuantity { get; set; }

    [Column("archived")]
    public bool? Archived { get; set; }

    [Column("min_stock")]
    [Precision(7, 4)]
    public decimal? MinStock { get; set; }

    [ForeignKey("CurrencyId")]
    [InverseProperty("PortfolioProducts")]
    public virtual Currency? Currency { get; set; }
}
