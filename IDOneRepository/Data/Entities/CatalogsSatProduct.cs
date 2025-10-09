using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IDOneRepository.Data.Entities;

[Table("catalogs_sat_products")]
public partial class CatalogsSatProduct
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("code")]
    [StringLength(14)]
    public string? Code { get; set; }

    [Column("title")]
    [StringLength(350)]
    public string? Title { get; set; }

    [Column("created_at", TypeName = "timestamp without time zone")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at", TypeName = "timestamp without time zone")]
    public DateTime UpdatedAt { get; set; }
}
