using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineBookStoreManagement.Models
{
    public class Coupon
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// "Percentage" or "Flat"
        /// </summary>
        [Required]
        [StringLength(20)]
        public string DiscountType { get; set; } = "Percentage";

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MinimumOrderAmount { get; set; } = 0.00m;

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MaximumDiscountAmount { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? StartDate { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public int? UsageLimit { get; set; }

        public int TimesUsed { get; set; } = 0;
    }
}
