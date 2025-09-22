using System;
using System.Globalization;

namespace CryptoSpot.Core.ValueObjects
{
    /// <summary>
    /// 数量值对象 - 确保数量的有效性和一致性
    /// </summary>
    public record Quantity
    {
        public decimal Value { get; }

        public Quantity(decimal value)
        {
            if (value < 0)
                throw new ArgumentException("数量不能为负数", nameof(value));
            
            if (value > 1000000) // 防止异常大的数量
                throw new ArgumentException("数量超出合理范围", nameof(value));

            Value = Math.Round(value, 8); // 保留8位小数
        }

        public static Quantity Zero => new(0);
        
        public static Quantity operator +(Quantity left, Quantity right) => new(left.Value + right.Value);
        public static Quantity operator -(Quantity left, Quantity right) => new(left.Value - right.Value);
        public static Quantity operator *(Quantity quantity, decimal multiplier) => new(quantity.Value * multiplier);
        public static Quantity operator /(Quantity quantity, decimal divisor) => new(quantity.Value / divisor);
        
        public static bool operator >(Quantity left, Quantity right) => left.Value > right.Value;
        public static bool operator <(Quantity left, Quantity right) => left.Value < right.Value;
        public static bool operator >=(Quantity left, Quantity right) => left.Value >= right.Value;
        public static bool operator <=(Quantity left, Quantity right) => left.Value <= right.Value;

        public override string ToString() => Value.ToString("F8", CultureInfo.InvariantCulture);
        
        public static implicit operator decimal(Quantity quantity) => quantity.Value;
        public static explicit operator Quantity(decimal value) => new(value);
    }
}
