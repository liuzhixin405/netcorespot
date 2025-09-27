using System;
using System.Globalization;

namespace CryptoSpot.Domain.ValueObjects
{
    /// <summary>
    /// 金额值对象 - 表示货币金额
    /// </summary>
    public record Money
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency = "USDT")
        {
            if (amount < 0)
                throw new ArgumentException("金额不能为负数", nameof(amount));
            
            if (string.IsNullOrWhiteSpace(currency))
                throw new ArgumentException("货币代码不能为空", nameof(currency));

            Amount = Math.Round(amount, 8);
            Currency = currency.ToUpperInvariant();
        }

        public static Money Zero(string currency = "USDT") => new(0, currency);
        
        public static Money operator +(Money left, Money right)
        {
            if (left.Currency != right.Currency)
                throw new InvalidOperationException($"不能对不同货币进行运算: {left.Currency} vs {right.Currency}");
            
            return new Money(left.Amount + right.Amount, left.Currency);
        }
        
        public static Money operator -(Money left, Money right)
        {
            if (left.Currency != right.Currency)
                throw new InvalidOperationException($"不能对不同货币进行运算: {left.Currency} vs {right.Currency}");
            
            return new Money(left.Amount - right.Amount, left.Currency);
        }
        
        public static Money operator *(Money money, decimal multiplier) => new(money.Amount * multiplier, money.Currency);
        public static Money operator /(Money money, decimal divisor) => new(money.Amount / divisor, money.Currency);
        
        public static bool operator >(Money left, Money right)
        {
            if (left.Currency != right.Currency)
                throw new InvalidOperationException($"不能比较不同货币: {left.Currency} vs {right.Currency}");
            
            return left.Amount > right.Amount;
        }
        
        public static bool operator <(Money left, Money right)
        {
            if (left.Currency != right.Currency)
                throw new InvalidOperationException($"不能比较不同货币: {left.Currency} vs {right.Currency}");
            
            return left.Amount < right.Amount;
        }

        public override string ToString() => $"{Amount:F8} {Currency}";
        
        public static implicit operator decimal(Money money) => money.Amount;
        public static explicit operator Money(decimal amount) => new(amount);
    }
}
