-- 快速验证用户密码哈希
-- 用途：检查所有用户是否都有有效的密码哈希

USE cryptospot;

SELECT 
    Id,
    Username,
    Email,
    CASE 
        WHEN PasswordHash IS NULL THEN '❌ NULL'
        WHEN PasswordHash = '' THEN '❌ 空字符串'
        WHEN LENGTH(PasswordHash) < 10 THEN '❌ 太短'
        ELSE '✅ 正常'
    END AS PasswordHashStatus,
    Type,
    IsActive
FROM Users
ORDER BY Id;

-- 如果发现有问题的用户，使用以下语句修复：
-- UPDATE Users SET PasswordHash = 'uc1fdOpWT+T6ZaUHlNFpS3MVHfF3T4megX2YhhUeLoE5NZc8' WHERE PasswordHash IS NULL OR PasswordHash = '';
