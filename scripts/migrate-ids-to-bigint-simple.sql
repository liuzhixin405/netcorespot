-- ============================================
-- 迁移脚本：将所有表的ID从INT改为BIGINT (简化版)
-- 说明：所有实体ID统一使用BIGINT类型（包括TradingPairId）
-- 数据库：CryptoSpot
-- 日期：2025-11-22
-- ============================================

USE cryptospotdb_dev;

-- ============================================
-- 步骤1：查看当前外键约束
-- ============================================
SELECT 
    CONSTRAINT_NAME,
    TABLE_NAME,
    COLUMN_NAME,
    REFERENCED_TABLE_NAME,
    REFERENCED_COLUMN_NAME
FROM 
    INFORMATION_SCHEMA.KEY_COLUMN_USAGE
WHERE 
    TABLE_SCHEMA = 'cryptospotdb_dev'
    AND REFERENCED_TABLE_NAME IS NOT NULL;

-- ============================================
-- 步骤2：删除外键约束（根据实际查询结果）
-- ============================================

-- 删除 KLineData 表的外键
ALTER TABLE KLineData DROP FOREIGN KEY FK_KLineData_TradingPairs_TradingPairId;

-- 删除 Orders 表的外键
ALTER TABLE Orders DROP FOREIGN KEY FK_Orders_TradingPairs_TradingPairId;

-- 删除 Trades 表的外键
ALTER TABLE Trades DROP FOREIGN KEY FK_Trades_Orders_BuyOrderId;
ALTER TABLE Trades DROP FOREIGN KEY FK_Trades_Orders_SellOrderId;
ALTER TABLE Trades DROP FOREIGN KEY FK_Trades_TradingPairs_TradingPairId;
ALTER TABLE Trades DROP FOREIGN KEY FK_Trades_Users_UserId;

-- ============================================
-- 步骤3：修改列类型
-- ============================================

-- 1. 修改 Users 表主键
ALTER TABLE Users 
    MODIFY COLUMN Id BIGINT NOT NULL AUTO_INCREMENT;

-- 2. 修改 Orders 表
ALTER TABLE Orders 
    MODIFY COLUMN Id BIGINT NOT NULL AUTO_INCREMENT,
    MODIFY COLUMN UserId BIGINT NULL;

-- 3. 修改 Trades 表
ALTER TABLE Trades 
    MODIFY COLUMN Id BIGINT NOT NULL AUTO_INCREMENT,
    MODIFY COLUMN BuyOrderId BIGINT NOT NULL,
    MODIFY COLUMN SellOrderId BIGINT NOT NULL,
    MODIFY COLUMN BuyerId BIGINT NOT NULL,
    MODIFY COLUMN SellerId BIGINT NOT NULL,
    MODIFY COLUMN UserId BIGINT NULL;

-- 4. 修改 Assets 表（如果有外键，需要先在步骤2删除）
ALTER TABLE Assets 
    MODIFY COLUMN Id BIGINT NOT NULL AUTO_INCREMENT,
    MODIFY COLUMN UserId BIGINT NOT NULL;

-- 5. 修改其他可能存在的用户关联表
-- ALTER TABLE UserLoginHistories MODIFY COLUMN Id BIGINT NOT NULL AUTO_INCREMENT, MODIFY COLUMN UserId BIGINT NOT NULL;
-- ALTER TABLE UserApiKeys MODIFY COLUMN Id BIGINT NOT NULL AUTO_INCREMENT, MODIFY COLUMN UserId BIGINT NOT NULL;

-- 6. 修改 KLineData 表
ALTER TABLE KLineData 
    MODIFY COLUMN Id BIGINT NOT NULL AUTO_INCREMENT,
    MODIFY COLUMN TradingPairId BIGINT NOT NULL;

-- 7. 修改 TradingPairs 表主键
ALTER TABLE TradingPairs 
    MODIFY COLUMN Id BIGINT NOT NULL AUTO_INCREMENT;

-- 8. 修改 Orders 表的 TradingPairId 外键
ALTER TABLE Orders 
    MODIFY COLUMN TradingPairId BIGINT NOT NULL;

-- 9. 修改 Trades 表的 TradingPairId 外键
ALTER TABLE Trades 
    MODIFY COLUMN TradingPairId BIGINT NOT NULL;

-- ============================================
-- 步骤4：重新创建外键约束
-- ============================================

-- KLineData 表外键 (TradingPairId 使用 BIGINT)
ALTER TABLE KLineData 
    ADD CONSTRAINT FK_KLineData_TradingPairs_TradingPairId 
    FOREIGN KEY (TradingPairId) REFERENCES TradingPairs(Id) ON DELETE CASCADE;

-- Orders 表外键 (TradingPairId 使用 BIGINT)
ALTER TABLE Orders 
    ADD CONSTRAINT FK_Orders_TradingPairs_TradingPairId 
    FOREIGN KEY (TradingPairId) REFERENCES TradingPairs(Id) ON DELETE CASCADE;

-- Trades 表外键
ALTER TABLE Trades 
    ADD CONSTRAINT FK_Trades_Orders_BuyOrderId 
    FOREIGN KEY (BuyOrderId) REFERENCES Orders(Id) ON DELETE CASCADE;

ALTER TABLE Trades 
    ADD CONSTRAINT FK_Trades_Orders_SellOrderId 
    FOREIGN KEY (SellOrderId) REFERENCES Orders(Id) ON DELETE CASCADE;

ALTER TABLE Trades 
    ADD CONSTRAINT FK_Trades_TradingPairs_TradingPairId 
    FOREIGN KEY (TradingPairId) REFERENCES TradingPairs(Id) ON DELETE CASCADE;

ALTER TABLE Trades 
    ADD CONSTRAINT FK_Trades_Users_UserId 
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE;

-- ============================================
-- 步骤5：验证修改
-- ============================================
SELECT 
    TABLE_NAME,
    COLUMN_NAME,
    DATA_TYPE,
    COLUMN_TYPE
FROM 
    INFORMATION_SCHEMA.COLUMNS
WHERE 
    TABLE_SCHEMA = 'cryptospotdb_dev'
    AND COLUMN_NAME IN ('Id', 'UserId', 'BuyerId', 'SellerId', 'BuyOrderId', 'SellOrderId', 'TradingPairId')
ORDER BY 
    TABLE_NAME, COLUMN_NAME;

SELECT '✅ 所有ID已迁移为BIGINT类型！' AS Status;
SELECT '⚠️  请验证所有ID字段都显示为bigint类型' AS Warning;
