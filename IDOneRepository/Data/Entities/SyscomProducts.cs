using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IDOneRepository.Data.Entities;

[Table("syscom_products_staging")]
public class SyscomProducts
{
    [Key]
    [Column("code")]
    [MaxLength(35)]
    public string Code { get; set; } = string.Empty;

    [Column("name")]
    [MaxLength(350)]
    public string? Name { get; set; }

    [Column("details")]
    public string? Details { get; set; }

    [Column("sat_unit")]
    [MaxLength(14)]
    public string? SatUnit { get; set; }

    [Column("sat_unit_desc")]
    [MaxLength(350)]
    public string? SatUnitDesc { get; set; }

    [Column("sat_key")]
    [MaxLength(14)]
    public string? SatKey { get; set; }

    [Column("sat_key_desc")]
    [MaxLength(350)]
    public string? SatKeyDesc { get; set; }

    [Column("price")]
    public decimal? Price { get; set; }

    [Column("currency")]
    [MaxLength(3)]
    public string? Currency { get; set; }

    [Column("trade_mark")]
    [MaxLength(70)]
    public string? TradeMark { get; set; }
}