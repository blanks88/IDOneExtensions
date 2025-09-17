using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IDOneRepository.Data.Entities;

[Table("currencies")]
public partial class Currency
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("title")]
    [StringLength(140)]
    public string? Title { get; set; }

    [Column("symbol")]
    [StringLength(3)]
    public string? Symbol { get; set; }

    [Column("iso_title")]
    [StringLength(3)]
    public string? IsoTitle { get; set; }

    [Column("created_at", TypeName = "timestamp without time zone")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at", TypeName = "timestamp without time zone")]
    public DateTime UpdatedAt { get; set; }

    [Column("principal")]
    public bool? Principal { get; set; }

    public ICollection<PortfolioProduct> PortfolioProducts { get; set; } = [];
}
