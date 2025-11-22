using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CryptoSpot.Domain.Extensions;

namespace CryptoSpot.Domain.Entities
{
    /// <summary>
    /// 基础实体类 - 包含所有表的公共字段
    /// </summary>
    public abstract class BaseEntity
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// 创建时间戳 (Unix timestamp in milliseconds)
        /// </summary>
        [Column(TypeName = "bigint")]
        public long CreatedAt { get; set; }

        /// <summary>
        /// 更新时间戳 (Unix timestamp in milliseconds)
        /// </summary>
        [Column(TypeName = "bigint")]
        public long UpdatedAt { get; set; }

        /// <summary>
        /// 是否已删除 (软删除标记)
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// 删除时间戳 (Unix timestamp in milliseconds)
        /// </summary>
        [Column(TypeName = "bigint")]
        public long? DeletedAt { get; set; }

        /// <summary>
        /// 版本号 (乐观锁)
        /// </summary>
        [Timestamp]
        public byte[] Version { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 构造函数 - 自动设置创建时间戳
        /// </summary>
        protected BaseEntity()
        {
            var now = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();
            CreatedAt = now;
            UpdatedAt = now;
        }

        /// <summary>
        /// 更新实体时调用，自动设置更新时间戳
        /// </summary>
        public virtual void Touch()
        {
            UpdatedAt = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();
        }

        /// <summary>
        /// 获取创建时间的DateTime对象
        /// </summary>
        [NotMapped]
        public DateTime CreatedDateTime => DateTimeExtensions.FromUnixTimeMilliseconds(CreatedAt);

        /// <summary>
        /// 获取更新时间的DateTime对象
        /// </summary>
        [NotMapped]
        public DateTime UpdatedDateTime => DateTimeExtensions.FromUnixTimeMilliseconds(UpdatedAt);
    }
}
