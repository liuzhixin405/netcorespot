using System;
using System.Collections.Generic;
using System.Diagnostics;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Infrastructure.MatchEngine.Core
{
    public class PriceTimePriorityMatchingAlgorithm : IMatchingAlgorithm
    {
        public IEnumerable<MatchSlice> Match(IOrderBook book, Order taker)
        {
            while (taker.FilledQuantity < taker.Quantity)
            {
                var maker = book.GetBestOpposite(taker.Side);
                if (maker == null)
                {
                    Debug.WriteLine($"[Match] Taker {taker.OrderId} ({taker.Side}) - no opposite order found");
                    yield break;
                }

                Debug.WriteLine($"[Match] Taker {taker.OrderId} P={taker.Price} S={taker.Side} vs Maker {maker.OrderId} P={maker.Price} S={maker.Side}");

                if (!PriceCross(taker, maker))
                {
                    Debug.WriteLine($"[Match] Price cross FAILED - stopping match for {taker.OrderId}");
                    yield break;
                }
                if (maker.UserId == taker.UserId)
                {
                    Debug.WriteLine($"[Match] Self-trade skip {taker.OrderId} vs {maker.OrderId}");
                    book.Remove(maker);
                    continue;
                }

                var remainingTaker = taker.Quantity - taker.FilledQuantity;
                var remainingMaker = maker.Quantity - maker.FilledQuantity;
                var qty = Math.Min(remainingTaker, remainingMaker);
                var price = maker.Price ?? taker.Price ?? 0m;

                yield return new MatchSlice(maker, taker, price, qty);
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
