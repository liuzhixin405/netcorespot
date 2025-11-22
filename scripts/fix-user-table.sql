-- ====================================================
-- CryptoSpot 数据库修复脚本
-- 用途：为现有 Users 表添加 Email 和 PasswordHash 字段
-- 日期：2025-11-22
-- ====================================================

USE cryptospot;

-- 1. 添加 Email 字段（如果不存在）
SET @dbname = 'cryptospot';
SET @tablename = 'Users';
SET @columnname = 'Email';
SET @preparedStatement = (SELECT IF(
  (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE
      (TABLE_SCHEMA = @dbname)
      AND (TABLE_NAME = @tablename)
      AND (COLUMN_NAME = @columnname)
  ) > 0,
  "SELECT 'Email 字段已存在' AS Message;",
  CONCAT("ALTER TABLE ", @tablename, " ADD COLUMN ", @columnname, " VARCHAR(100) NULL AFTER Username, ADD INDEX IX_Users_Email (Email);")
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- 2. 添加 PasswordHash 字段（如果不存在）
SET @columnname = 'PasswordHash';
SET @preparedStatement = (SELECT IF(
  (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE
      (TABLE_SCHEMA = @dbname)
      AND (TABLE_NAME = @tablename)
      AND (COLUMN_NAME = @columnname)
  ) > 0,
  "SELECT 'PasswordHash 字段已存在' AS Message;",
  CONCAT("ALTER TABLE ", @tablename, " ADD COLUMN ", @columnname, " VARCHAR(255) NULL COMMENT 'Base64 encoded PBKDF2 hash' AFTER Email;")
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- 3. 添加 LastLoginAt 字段（如果不存在）
SET @columnname = 'LastLoginAt';
SET @preparedStatement = (SELECT IF(
  (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE
      (TABLE_SCHEMA = @dbname)
      AND (TABLE_NAME = @tablename)
      AND (COLUMN_NAME = @columnname)
  ) > 0,
  "SELECT 'LastLoginAt 字段已存在' AS Message;",
  CONCAT("ALTER TABLE ", @tablename, " ADD COLUMN ", @columnname, " BIGINT NULL AFTER DailyTradedAmount;")
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- 4. 更新现有用户的邮箱和密码哈希
-- 注意：这些密码是测试密码，生产环境请修改！

-- SystemMarketMaker - 密码: maker123
UPDATE Users 
SET 
    Email = 'maker@cryptospot.local',
    PasswordHash = '/In3aL/qvVS/x50yEPUUFypFCkqnLfYH/ycRQY/dj5eUhjG2'
WHERE Username = 'SystemMarketMaker' AND (Email IS NULL OR PasswordHash IS NULL);

-- SystemAdmin - 密码: admin123
UPDATE Users 
SET 
    Email = 'admin@cryptospot.local',
    PasswordHash = '0grSBrIOk8nfnYCFn6BJL874YhOYO3ypa2fyiIn20ykxe7O1'
WHERE Username = 'SystemAdmin' AND (Email IS NULL OR PasswordHash IS NULL);

-- test_user_1 - 密码: test123
UPDATE Users 
SET 
    Email = 'test1@example.com',
    PasswordHash = 'uc1fdOpWT+T6ZaUHlNFpS3MVHfF3T4megX2YhhUeLoE5NZc8'
WHERE Username = 'test_user_1' AND (Email IS NULL OR PasswordHash IS NULL);

-- test_user_2 - 密码: test123
UPDATE Users 
SET 
    Email = 'test2@example.com',
    PasswordHash = 'uc1fdOpWT+T6ZaUHlNFpS3MVHfF3T4megX2YhhUeLoE5NZc8'
WHERE Username = 'test_user_2' AND (Email IS NULL OR PasswordHash IS NULL);

-- test_user_3 - 密码: test123
UPDATE Users 
SET 
    Email = 'test3@example.com',
    PasswordHash = 'uc1fdOpWT+T6ZaUHlNFpS3MVHfF3T4megX2YhhUeLoE5NZc8'
WHERE Username = 'test_user_3' AND (Email IS NULL OR PasswordHash IS NULL);

-- 5. 验证修复结果
SELECT '=== Users 表结构检查 ===' AS Info;
SHOW COLUMNS FROM Users LIKE 'Email';
SHOW COLUMNS FROM Users LIKE 'PasswordHash';
SHOW COLUMNS FROM Users LIKE 'LastLoginAt';

SELECT '=== 用户数据检查 ===' AS Info;
SELECT 
    Id,
    Username,
    Email,
    CASE 
        WHEN PasswordHash IS NULL THEN '❌ 无密码'
        WHEN LENGTH(PasswordHash) > 0 THEN '✅ 已设置'
        ELSE '❌ 空密码'
    END AS PasswordStatus,
    Type,
    IsActive
FROM Users
ORDER BY Id;

SELECT '✅ 数据库修复完成！' AS Status;
SELECT '' AS Info;
SELECT '测试账号信息：' AS Info;
SELECT '- test_user_1 / test123' AS Account;
SELECT '- test_user_2 / test123' AS Account;
SELECT '- test_user_3 / test123' AS Account;
SELECT '- SystemMarketMaker / maker123' AS Account;
SELECT '- SystemAdmin / admin123' AS Account;
