using System;
using System.Globalization;

namespace CryptoSpot.Domain.ValueObjects
{
    /// <summary>
    /// 价格值对象 - 确保价格的有效性和一致性
    /// </summary>
    public record Price
    {
        public decimal Value { get; }

        public Price(decimal value)
        {
            if (value < 0)
                throw new ArgumentException("价格不能为负数", nameof(value));
            
            if (value > 1000000) // 防止异常大的价格
                throw new ArgumentException("价格超出合理范围", nameof(value));

            Value = Math.Round(value, 8); // 保留8位小数
        }

        public static Price Zero => new(0);
        
        public static Price operator +(Price left, Price right) => new(left.Value + right.Value);
        public static Price operator -(Price left, Price right) => new(left.Value - right.Value);
        public static Price operator *(Price price, decimal multiplier) => new(price.Value * multiplier);
        public static Price operator /(Price price, decimal divisor) => new(price.Value / divisor);
        
        public static bool operator >(Price left, Price right) => left.Value > right.Value;
        public static bool operator <(Price left, Price right) => left.Value < right.Value;
        public static bool operator >=(Price left, Price right) => left.Value >= right.Value;
        public static bool operator <=(Price left, Price right) => left.Value <= right.Value;

        public override string ToString() => Value.ToString("F8", CultureInfo.InvariantCulture);
        
        public static implicit operator decimal(Price price) => price.Value;
        public static explicit operator Price(decimal value) => new(value);
    }
}
