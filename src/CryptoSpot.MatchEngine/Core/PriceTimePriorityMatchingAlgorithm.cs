using System;
using System.Collections.Generic;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.MatchEngine.Core
{
    /// <summary>
    /// 价格优先 + 同价位 FIFO（依赖 IOrderBook 的内部队列顺序）。
    /// 不修改订单的 FilledQuantity，仅生成匹配切片，由上层协调更新与结算。
    /// </summary>
    public class PriceTimePriorityMatchingAlgorithm : IMatchingAlgorithm
    {
        public IEnumerable<MatchSlice> Match(IOrderBook book, Order taker)
        {
            while (taker.FilledQuantity < taker.Quantity)
            {
                var maker = book.GetBestOpposite(taker.Side);
                if (maker == null) yield break;

                if (!PriceCross(taker, maker)) yield break;
                if (maker.UserId == taker.UserId)
                {
                    book.Remove(maker); // 自成交避免
                    continue;
                }

                var remainingTaker = taker.Quantity - taker.FilledQuantity;
                var remainingMaker = maker.Quantity - maker.FilledQuantity;
                var qty = Math.Min(remainingTaker, remainingMaker);
                var price = maker.Price ?? taker.Price ?? 0m;

                yield return new MatchSlice(maker, taker, price, qty);

                // 上层会根据结果刷新状态; 此处不推进数量以便迭代者控制终止条件
                taker.FilledQuantity += qty; // 暂时这里直接推进，避免上层重复逻辑（后续可改成纯函数，返回更新策略）
                maker.FilledQuantity += qty;

                if (maker.FilledQuantity >= maker.Quantity)
                {
                    book.Remove(maker);
                }
            }
        }

        private bool PriceCross(Order taker, Order maker)
        {
            if (taker.Type == OrderType.Market || maker.Type == OrderType.Market) return true;
            if (taker.Price.HasValue && maker.Price.HasValue)
            {
                if (taker.Side == OrderSide.Buy) return taker.Price >= maker.Price;
                return taker.Price <= maker.Price;
            }
            return true; // 缺价格时放宽（可后续收紧）
        }
    }
}
