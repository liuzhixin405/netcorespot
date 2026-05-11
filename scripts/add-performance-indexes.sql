-- ============================================================
-- CryptoSpot 性能优化索引
-- 执行前建议先检查是否已有同名索引，避免重复创建报错
-- ============================================================

-- 1. Orders 表: 订单簿查询（覆盖率 90%+）
CREATE INDEX IX_Orders_TradingPairId_Status_Side_Price
    ON Orders (TradingPairId, Status, Side, Price);

-- 2. Orders 表: 用户订单历史
CREATE INDEX IX_Orders_UserId_CreatedAt
    ON Orders (UserId, CreatedAt);

-- 3. Orders 表: 过期订单清理
CREATE INDEX IX_Orders_Status_CreatedAt
    ON Orders (Status, CreatedAt);

-- 4. Trades 表: 市场成交记录
CREATE INDEX IX_Trades_TradingPairId_ExecutedAt
    ON Trades (TradingPairId, ExecutedAt);

-- 5. Trades 表: 用户买入记录
CREATE INDEX IX_Trades_BuyerId_ExecutedAt
    ON Trades (BuyerId, ExecutedAt);

-- 6. Trades 表: 用户卖出记录
CREATE INDEX IX_Trades_SellerId_ExecutedAt
    ON Trades (SellerId, ExecutedAt);

-- 7. KLineData: 改为 UNIQUE 索引（先删后建）
-- 注意：如果表中已有 (TradingPairId, TimeFrame, OpenTime) 重复数据，需先清理
DROP INDEX IX_KLineData_TradingPairId_TimeFrame_OpenTime
    ON KLineData;

CREATE UNIQUE INDEX IX_KLineData_TradingPairId_TimeFrame_OpenTime
    ON KLineData (TradingPairId, TimeFrame, OpenTime);
