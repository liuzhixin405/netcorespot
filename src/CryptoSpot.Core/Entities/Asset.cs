using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    [Table("Assets")]
    public class Asset
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [StringLength(10)]
        public string Symbol { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Available { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Frozen { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        // Computed properties
        [NotMapped]
        public decimal Total => Available + Frozen;
    }
}
