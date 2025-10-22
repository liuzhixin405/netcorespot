-- ====================================================
-- CryptoSpot 数据库初始化脚本
-- 用途：手动初始化数据库（当自动初始化失败时使用）
-- 版本：1.0
-- 日期：2025-10-22
-- ====================================================

-- 1. 创建数据库（如果不存在）
CREATE DATABASE IF NOT EXISTS cryptospot 
DEFAULT CHARACTER SET utf8mb4 
DEFAULT COLLATE utf8mb4_unicode_ci;

USE cryptospot;

-- ====================================================
-- 2. 清空现有数据（可选，谨慎使用）
-- ====================================================
-- TRUNCATE TABLE Trades;
-- TRUNCATE TABLE Orders;
-- TRUNCATE TABLE KLineData;
-- TRUNCATE TABLE Assets;
-- TRUNCATE TABLE Users;
-- TRUNCATE TABLE TradingPairs;

-- ====================================================
-- 3. 插入交易对数据
-- ====================================================
INSERT INTO TradingPairs (Symbol, BaseAsset, QuoteAsset, MinQuantity, MaxQuantity, PricePrecision, QuantityPrecision, IsActive, LastUpdated)
SELECT 'BTCUSDT', 'BTC', 'USDT', 0.00001, 1000, 2, 5, 1, UNIX_TIMESTAMP() * 1000
WHERE NOT EXISTS (SELECT 1 FROM TradingPairs WHERE Symbol = 'BTCUSDT');

INSERT INTO TradingPairs (Symbol, BaseAsset, QuoteAsset, MinQuantity, MaxQuantity, PricePrecision, QuantityPrecision, IsActive, LastUpdated)
SELECT 'ETHUSDT', 'ETH', 'USDT', 0.001, 10000, 2, 3, 1, UNIX_TIMESTAMP() * 1000
WHERE NOT EXISTS (SELECT 1 FROM TradingPairs WHERE Symbol = 'ETHUSDT');

INSERT INTO TradingPairs (Symbol, BaseAsset, QuoteAsset, MinQuantity, MaxQuantity, PricePrecision, QuantityPrecision, IsActive, LastUpdated)
SELECT 'SOLUSDT', 'SOL', 'USDT', 0.01, 100000, 3, 2, 1, UNIX_TIMESTAMP() * 1000
WHERE NOT EXISTS (SELECT 1 FROM TradingPairs WHERE Symbol = 'SOLUSDT');

-- ====================================================
-- 4. 插入系统用户
-- ====================================================
-- 系统做市商（UserType: 1 = MarketMaker）
INSERT INTO Users (Username, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'SystemMarketMaker', 1, '系统做市商账号', 1, 1, 0.10, 1000000.00, 0.00
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'SystemMarketMaker' AND Type = 1);

-- 系统管理员（UserType: 2 = Admin）
INSERT INTO Users (Username, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'SystemAdmin', 2, '系统管理员账号', 1, 0, 0.05, 500000.00, 0.00
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'SystemAdmin' AND Type = 2);

-- ====================================================
-- 5. 插入测试用户（UserType: 0 = Regular）
-- ====================================================
INSERT INTO Users (Username, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'test_user_1', 0, '测试用户1', 1, 0, 0.30, 10000.00, 0.00
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'test_user_1');

INSERT INTO Users (Username, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'test_user_2', 0, '测试用户2', 1, 0, 0.30, 10000.00, 0.00
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'test_user_2');

INSERT INTO Users (Username, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'test_user_3', 0, '测试用户3', 1, 0, 0.30, 10000.00, 0.00
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'test_user_3');

-- ====================================================
-- 6. 插入系统做市商资产
-- ====================================================
-- 注意：这里假设 SystemMarketMaker 的 ID 是 1
-- 如果不是，请根据实际情况修改 UserId

-- 获取做市商用户ID
SET @maker_user_id = (SELECT Id FROM Users WHERE Username = 'SystemMarketMaker' AND Type = 1 LIMIT 1);

-- USDT 资产
INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @maker_user_id, 'USDT', 1000000.00, 0.00, 100000.00, 1000000.00, 1
WHERE @maker_user_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @maker_user_id AND Symbol = 'USDT');

-- BTC 资产
INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @maker_user_id, 'BTC', 100.00000, 0.00000, 10.00000, 100.00000, 1
WHERE @maker_user_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @maker_user_id AND Symbol = 'BTC');

-- ETH 资产
INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @maker_user_id, 'ETH', 5000.000, 0.000, 500.000, 5000.000, 1
WHERE @maker_user_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @maker_user_id AND Symbol = 'ETH');

-- SOL 资产
INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @maker_user_id, 'SOL', 50000.00, 0.00, 5000.00, 50000.00, 1
WHERE @maker_user_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @maker_user_id AND Symbol = 'SOL');

-- ====================================================
-- 7. 插入测试用户 1 资产
-- ====================================================
SET @test_user_1_id = (SELECT Id FROM Users WHERE Username = 'test_user_1' LIMIT 1);

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_1_id, 'USDT', 10000.00, 0.00, 0.00, 10000.00, 0
WHERE @test_user_1_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_1_id AND Symbol = 'USDT');

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_1_id, 'BTC', 1.00000, 0.00000, 0.00000, 1.00000, 0
WHERE @test_user_1_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_1_id AND Symbol = 'BTC');

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_1_id, 'ETH', 10.000, 0.000, 0.000, 10.000, 0
WHERE @test_user_1_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_1_id AND Symbol = 'ETH');

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_1_id, 'SOL', 100.00, 0.00, 0.00, 100.00, 0
WHERE @test_user_1_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_1_id AND Symbol = 'SOL');

-- ====================================================
-- 8. 插入测试用户 2 资产
-- ====================================================
SET @test_user_2_id = (SELECT Id FROM Users WHERE Username = 'test_user_2' LIMIT 1);

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_2_id, 'USDT', 10000.00, 0.00, 0.00, 10000.00, 0
WHERE @test_user_2_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_2_id AND Symbol = 'USDT');

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_2_id, 'BTC', 1.00000, 0.00000, 0.00000, 1.00000, 0
WHERE @test_user_2_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_2_id AND Symbol = 'BTC');

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_2_id, 'ETH', 10.000, 0.000, 0.000, 10.000, 0
WHERE @test_user_2_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_2_id AND Symbol = 'ETH');

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_2_id, 'SOL', 100.00, 0.00, 0.00, 100.00, 0
WHERE @test_user_2_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_2_id AND Symbol = 'SOL');

-- ====================================================
-- 9. 插入测试用户 3 资产
-- ====================================================
SET @test_user_3_id = (SELECT Id FROM Users WHERE Username = 'test_user_3' LIMIT 1);

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_3_id, 'USDT', 10000.00, 0.00, 0.00, 10000.00, 0
WHERE @test_user_3_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_3_id AND Symbol = 'USDT');

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_3_id, 'BTC', 1.00000, 0.00000, 0.00000, 1.00000, 0
WHERE @test_user_3_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_3_id AND Symbol = 'BTC');

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_3_id, 'ETH', 10.000, 0.000, 0.000, 10.000, 0
WHERE @test_user_3_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_3_id AND Symbol = 'ETH');

INSERT INTO Assets (UserId, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled)
SELECT @test_user_3_id, 'SOL', 100.00, 0.00, 0.00, 100.00, 0
WHERE @test_user_3_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Assets WHERE UserId = @test_user_3_id AND Symbol = 'SOL');

-- ====================================================
-- 10. 验证数据
-- ====================================================
SELECT '=== 交易对数据 ===' AS Info;
SELECT * FROM TradingPairs;

SELECT '=== 用户数据 ===' AS Info;
SELECT Id, Username, Type, Description, IsActive FROM Users;

SELECT '=== 资产数据 ===' AS Info;
SELECT 
    a.Id,
    u.Username,
    a.Symbol,
    a.Available,
    a.Frozen,
    (a.Available + a.Frozen) AS Total
FROM Assets a
JOIN Users u ON a.UserId = u.Id
ORDER BY u.Id, a.Symbol;

SELECT '=== 数据统计 ===' AS Info;
SELECT 
    '交易对数量' AS Item,
    COUNT(*) AS Count
FROM TradingPairs
UNION ALL
SELECT 
    '用户数量' AS Item,
    COUNT(*) AS Count
FROM Users
UNION ALL
SELECT 
    '资产记录数量' AS Item,
    COUNT(*) AS Count
FROM Assets;

-- ====================================================
-- 完成
-- ====================================================
SELECT '✅ 数据库初始化完成！' AS Status;
