using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IDOneRepository.Data.Entities;

[Table("catalogs_measurements")]
public partial class CatalogsMeasurement
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("title")]
    [StringLength(70)]
    public string? Title { get; set; }

    [Column("created_at", TypeName = "timestamp without time zone")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at", TypeName = "timestamp without time zone")]
    public DateTime UpdatedAt { get; set; }

    [Column("sat_description")]
    [StringLength(350)]
    public string? SatDescription { get; set; }

    [Column("sat_key")]
    [StringLength(14)]
    public string? SatKey { get; set; }

    [Column("store_type")]
    public int? StoreType { get; set; }
}
