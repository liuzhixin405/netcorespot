-- ====================================================
-- CryptoSpot 数据库初始化脚本
-- 用途：手动初始化数据库（当自动初始化失败时使用）
-- 版本：2.0
-- 日期：2025-11-22
-- 说明：所有实体ID统一使用BIGINT类型（UserId, OrderId, TradeId, TradingPairId等）
-- ====================================================

-- 1. 创建数据库（如果不存在）
CREATE DATABASE IF NOT EXISTS cryptospot 
DEFAULT CHARACTER SET utf8mb4 
DEFAULT COLLATE utf8mb4_unicode_ci;

USE cryptospot;

-- ====================================================
-- 1.5. 创建表结构（如果不存在）
-- ====================================================

-- TradingPairs 表 (Id 使用 BIGINT)
CREATE TABLE IF NOT EXISTS TradingPairs (
    Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Symbol VARCHAR(20) NOT NULL UNIQUE,
    BaseAsset VARCHAR(10) NOT NULL,
    QuoteAsset VARCHAR(10) NOT NULL,
    MinQuantity DECIMAL(18,8) NOT NULL DEFAULT 0.00000001,
    MaxQuantity DECIMAL(18,8) NOT NULL DEFAULT 1000000.00000000,
    PricePrecision INT NOT NULL DEFAULT 2,
    QuantityPrecision INT NOT NULL DEFAULT 8,
    IsActive TINYINT(1) NOT NULL DEFAULT 1,
    LastUpdated BIGINT NOT NULL,
    CreatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    UpdatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    IsDeleted TINYINT(1) NOT NULL DEFAULT 0,
    DeletedAt BIGINT NULL,
    Version TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX IX_TradingPairs_Symbol (Symbol),
    INDEX IX_TradingPairs_IsActive (IsActive)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Users 表 (Id 使用 BIGINT)
CREATE TABLE IF NOT EXISTS Users (
    Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Username VARCHAR(50) NOT NULL UNIQUE,
    Email VARCHAR(100) NULL,
    PasswordHash VARCHAR(255) NULL COMMENT 'Base64 encoded PBKDF2 hash',
    Type INT NOT NULL DEFAULT 1 COMMENT '1=Regular, 2=MarketMaker, 3=RiskManagement, 4=LiquidityProvider, 5=Admin',
    Description VARCHAR(200) NULL,
    IsActive TINYINT(1) NOT NULL DEFAULT 1,
    IsAutoTradingEnabled TINYINT(1) NOT NULL DEFAULT 0,
    MaxRiskRatio DECIMAL(5,4) NOT NULL DEFAULT 0.1000,
    DailyTradingLimit DECIMAL(18,8) NOT NULL DEFAULT 100000.00000000,
    DailyTradedAmount DECIMAL(18,8) NOT NULL DEFAULT 0.00000000,
    LastLoginAt BIGINT NULL,
    CreatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    UpdatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    IsDeleted TINYINT(1) NOT NULL DEFAULT 0,
    DeletedAt BIGINT NULL,
    Version TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX IX_Users_Username (Username),
    INDEX IX_Users_Email (Email),
    INDEX IX_Users_Type (Type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Assets 表 (Id 使用 BIGINT, UserId 使用 BIGINT)
CREATE TABLE IF NOT EXISTS Assets (
    Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    UserId BIGINT NOT NULL,
    Symbol VARCHAR(10) NOT NULL,
    Available DECIMAL(18,8) NOT NULL DEFAULT 0.00000000,
    Frozen DECIMAL(18,8) NOT NULL DEFAULT 0.00000000,
    MinReserve DECIMAL(18,8) NOT NULL DEFAULT 0.00000000,
    TargetBalance DECIMAL(18,8) NOT NULL DEFAULT 0.00000000,
    AutoRefillEnabled TINYINT(1) NOT NULL DEFAULT 0,
    CreatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    UpdatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    IsDeleted TINYINT(1) NOT NULL DEFAULT 0,
    DeletedAt BIGINT NULL,
    Version TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE INDEX IX_Assets_UserId_Symbol (UserId, Symbol),
    INDEX IX_Assets_UserId (UserId),
    INDEX IX_Assets_Symbol (Symbol)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Orders 表 (Id 使用 BIGINT, UserId 使用 BIGINT, TradingPairId 使用 BIGINT)
CREATE TABLE IF NOT EXISTS Orders (
    Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    OrderId VARCHAR(50) NOT NULL UNIQUE,
    UserId BIGINT NULL,
    TradingPairId BIGINT NOT NULL,
    ClientOrderId VARCHAR(50) NULL,
    Side INT NOT NULL COMMENT '0=Buy, 1=Sell',
    Type INT NOT NULL COMMENT '0=Limit, 1=Market',
    Status INT NOT NULL DEFAULT 0 COMMENT '0=Pending, 1=Active, 2=PartiallyFilled, 3=Filled, 4=Cancelled',
    Price DECIMAL(18,8) NULL,
    Quantity DECIMAL(18,8) NOT NULL,
    FilledQuantity DECIMAL(18,8) NOT NULL DEFAULT 0.00000000,
    AveragePrice DECIMAL(18,8) NULL,
    CreatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    UpdatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    IsDeleted TINYINT(1) NOT NULL DEFAULT 0,
    DeletedAt BIGINT NULL,
    Version TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX IX_Orders_OrderId (OrderId),
    INDEX IX_Orders_UserId (UserId),
    INDEX IX_Orders_TradingPairId (TradingPairId),
    INDEX IX_Orders_Status (Status),
    CONSTRAINT FK_Orders_TradingPairs_TradingPairId 
        FOREIGN KEY (TradingPairId) REFERENCES TradingPairs(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Trades 表 (所有ID使用 BIGINT)
CREATE TABLE IF NOT EXISTS Trades (
    Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    TradeId VARCHAR(50) NOT NULL UNIQUE,
    TradingPairId BIGINT NOT NULL,
    BuyOrderId BIGINT NOT NULL,
    SellOrderId BIGINT NOT NULL,
    BuyerId BIGINT NOT NULL,
    SellerId BIGINT NOT NULL,
    UserId BIGINT NULL COMMENT 'Deprecated: for compatibility',
    Price DECIMAL(18,8) NOT NULL,
    Quantity DECIMAL(18,8) NOT NULL,
    Fee DECIMAL(18,8) NOT NULL DEFAULT 0.00000000,
    FeeAsset VARCHAR(10) NULL,
    ExecutedAt BIGINT NOT NULL,
    CreatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    UpdatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    IsDeleted TINYINT(1) NOT NULL DEFAULT 0,
    DeletedAt BIGINT NULL,
    Version TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX IX_Trades_TradeId (TradeId),
    INDEX IX_Trades_TradingPairId (TradingPairId),
    INDEX IX_Trades_BuyOrderId (BuyOrderId),
    INDEX IX_Trades_SellOrderId (SellOrderId),
    INDEX IX_Trades_BuyerId (BuyerId),
    INDEX IX_Trades_SellerId (SellerId),
    INDEX IX_Trades_ExecutedAt (ExecutedAt),
    CONSTRAINT FK_Trades_TradingPairs_TradingPairId 
        FOREIGN KEY (TradingPairId) REFERENCES TradingPairs(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Trades_Orders_BuyOrderId 
        FOREIGN KEY (BuyOrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Trades_Orders_SellOrderId 
        FOREIGN KEY (SellOrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Trades_Users_UserId 
        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- KLineData 表 (Id 使用 BIGINT, TradingPairId 使用 BIGINT)
CREATE TABLE IF NOT EXISTS KLineData (
    Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    TradingPairId BIGINT NOT NULL,
    TimeFrame VARCHAR(10) NOT NULL,
    OpenTime BIGINT NOT NULL,
    CloseTime BIGINT NOT NULL,
    Open DECIMAL(18,8) NOT NULL,
    High DECIMAL(18,8) NOT NULL,
    Low DECIMAL(18,8) NOT NULL,
    Close DECIMAL(18,8) NOT NULL,
    Volume DECIMAL(18,8) NOT NULL,
    QuoteVolume DECIMAL(18,8) NOT NULL,
    TradeCount INT NOT NULL DEFAULT 0,
    CreatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    UpdatedAt BIGINT NOT NULL DEFAULT (UNIX_TIMESTAMP() * 1000),
    IsDeleted TINYINT(1) NOT NULL DEFAULT 0,
    DeletedAt BIGINT NULL,
    Version TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE INDEX IX_KLineData_TradingPairId_TimeFrame_OpenTime (TradingPairId, TimeFrame, OpenTime),
    INDEX IX_KLineData_TradingPairId (TradingPairId),
    INDEX IX_KLineData_OpenTime (OpenTime),
    CONSTRAINT FK_KLineData_TradingPairs_TradingPairId 
        FOREIGN KEY (TradingPairId) REFERENCES TradingPairs(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

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
-- 系统做市商（UserType: 2 = MarketMaker）- 密码: maker123
INSERT INTO Users (Username, Email, PasswordHash, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'SystemMarketMaker', 'maker@cryptospot.local', '/In3aL/qvVS/x50yEPUUFypFCkqnLfYH/ycRQY/dj5eUhjG2', 2, '系统做市商账号', 1, 1, 0.10, 1000000.00, 0.00
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'SystemMarketMaker');

-- 系统管理员（UserType: 5 = Admin）- 密码: admin123
INSERT INTO Users (Username, Email, PasswordHash, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'SystemAdmin', 'admin@cryptospot.local', '0grSBrIOk8nfnYCFn6BJL874YhOYO3ypa2fyiIn20ykxe7O1', 5, '系统管理员账号', 1, 0, 0.05, 500000.00, 0.00
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'SystemAdmin');

-- ====================================================
-- 5. 插入测试用户（UserType: 1 = Regular）- 密码: test123
-- ====================================================
INSERT INTO Users (Username, Email, PasswordHash, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'test_user_1', 'test1@example.com', 'uc1fdOpWT+T6ZaUHlNFpS3MVHfF3T4megX2YhhUeLoE5NZc8', 1, '测试用户1', 1, 0, 0.30, 10000.00, 0.00
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'test_user_1');

INSERT INTO Users (Username, Email, PasswordHash, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'test_user_2', 'test2@example.com', 'uc1fdOpWT+T6ZaUHlNFpS3MVHfF3T4megX2YhhUeLoE5NZc8', 1, '测试用户2', 1, 0, 0.30, 10000.00, 0.00
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'test_user_2');

INSERT INTO Users (Username, Email, PasswordHash, Type, Description, IsActive, IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount)
SELECT 'test_user_3', 'test3@example.com', 'uc1fdOpWT+T6ZaUHlNFpS3MVHfF3T4megX2YhhUeLoE5NZc8', 1, '测试用户3', 1, 0, 0.30, 10000.00, 0.00
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
