# 数据库迁移说明

## 概述
本文档说明如何将CryptoSpot数据库中的所有ID字段从INT类型迁移到BIGINT类型。

## 迁移原因
- **类型统一**: 所有实体ID（UserId、OrderId、TradeId、TradingPairId等）在代码中统一使用`long`类型（C#）
- **未来扩展**: BIGINT提供更大的数值范围，避免ID溢出问题
- **类型安全**: 避免EF Core中的类型不匹配错误

## 涉及的ID字段

### 主键ID (所有表)
- `Users.Id`: INT → BIGINT
- `TradingPairs.Id`: INT → BIGINT ⚠️ **重要变更**
- `Orders.Id`: INT → BIGINT
- `Trades.Id`: INT → BIGINT
- `Assets.Id`: INT → BIGINT
- `KLineData.Id`: INT → BIGINT

### 外键ID
- `Assets.UserId`: INT → BIGINT
- `Orders.UserId`: INT → BIGINT
- `Orders.TradingPairId`: INT → BIGINT ⚠️ **重要变更**
- `Trades.BuyerId`: INT → BIGINT
- `Trades.SellerId`: INT → BIGINT
- `Trades.BuyOrderId`: INT → BIGINT
- `Trades.SellOrderId`: INT → BIGINT
- `Trades.UserId`: INT → BIGINT
- `Trades.TradingPairId`: INT → BIGINT ⚠️ **重要变更**
- `KLineData.TradingPairId`: INT → BIGINT ⚠️ **重要变更**

## 可用脚本

### 1. init-database.sql
**用途**: 全新数据库初始化（推荐用于开发环境）

**使用场景**:
- 第一次部署
- 开发测试环境重置
- 数据可以丢弃时

**执行命令**:
```bash
mysql -u root -p < scripts/init-database.sql
```

**特点**:
- ✅ 创建数据库和所有表（使用BIGINT）
- ✅ 插入初始测试数据
- ✅ 创建系统用户（做市商、管理员）
- ✅ 使用 CREATE TABLE IF NOT EXISTS（幂等性）

### 2. migrate-ids-to-bigint-simple.sql
**用途**: 现有数据库迁移（用于生产环境）

**使用场景**:
- 生产环境迁移
- 需要保留现有数据
- 已有用户和交易数据

**执行前准备**:
```bash
# 1. 备份数据库（必须！）
mysqldump -u root -p cryptospotdb_dev > backup_before_migration_$(date +%Y%m%d_%H%M%S).sql

# 2. 验证备份
mysql -u root -p cryptospotdb_dev < backup_before_migration_*.sql

# 3. 执行迁移
mysql -u root -p cryptospotdb_dev < scripts/migrate-ids-to-bigint-simple.sql
```

**迁移步骤**:
1. 删除所有外键约束
2. 修改主键和外键列类型为BIGINT
3. 重新创建外键约束
4. 验证结果

**预期输出**:
```
✅ 所有ID已迁移为BIGINT类型！
⚠️  请验证所有ID字段都显示为bigint类型
```

## 迁移验证

### 验证数据类型
```sql
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
```

### 期望结果
所有列的 `DATA_TYPE` 应该显示为 `bigint`:

| TABLE_NAME | COLUMN_NAME | DATA_TYPE | COLUMN_TYPE |
|------------|-------------|-----------|-------------|
| Assets | Id | bigint | bigint |
| Assets | UserId | bigint | bigint |
| KLineData | Id | bigint | bigint |
| KLineData | TradingPairId | bigint | bigint |
| Orders | Id | bigint | bigint |
| Orders | TradingPairId | bigint | bigint |
| Orders | UserId | bigint | bigint |
| Trades | BuyOrderId | bigint | bigint |
| Trades | BuyerId | bigint | bigint |
| Trades | Id | bigint | bigint |
| Trades | SellOrderId | bigint | bigint |
| Trades | SellerId | bigint | bigint |
| Trades | TradingPairId | bigint | bigint |
| Trades | UserId | bigint | bigint |
| TradingPairs | Id | bigint | bigint |
| Users | Id | bigint | bigint |

### 验证外键约束
```sql
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
    AND REFERENCED_TABLE_NAME IS NOT NULL
ORDER BY
    TABLE_NAME, CONSTRAINT_NAME;
```

### 验证数据完整性
```sql
-- 检查记录数
SELECT 'Users' AS TableName, COUNT(*) AS RecordCount FROM Users
UNION ALL
SELECT 'TradingPairs', COUNT(*) FROM TradingPairs
UNION ALL
SELECT 'Orders', COUNT(*) FROM Orders
UNION ALL
SELECT 'Trades', COUNT(*) FROM Trades
UNION ALL
SELECT 'Assets', COUNT(*) FROM Assets
UNION ALL
SELECT 'KLineData', COUNT(*) FROM KLineData;
```

## 回滚方案

如果迁移出现问题，可以使用备份恢复:

```bash
# 恢复完整备份
mysql -u root -p cryptospotdb_dev < backup_before_migration_*.sql

# 或者只恢复特定表
mysql -u root -p cryptospotdb_dev -e "DROP TABLE IF EXISTS Orders, Trades, KLineData"
mysql -u root -p cryptospotdb_dev < backup_before_migration_*.sql
```

## 注意事项

### ⚠️ 重要提醒

1. **生产环境迁移**:
   - 必须在维护窗口执行
   - 提前通知用户
   - 准备回滚方案
   - 完整备份数据库

2. **TradingPairId变更**:
   - 这是从之前版本的重要变更
   - 之前TradingPairId保持为INT
   - 现在统一为BIGINT以匹配代码

3. **外键约束**:
   - 迁移过程会临时删除外键
   - 确保在迁移期间不进行数据操作
   - 迁移完成后会重新创建外键

4. **性能影响**:
   - BIGINT占用8字节，INT占用4字节
   - 数据库体积会增加
   - 索引大小会增加
   - 对于小型系统影响可忽略

5. **兼容性**:
   - MySQL 5.7+
   - MariaDB 10.2+
   - 确保应用程序代码已更新为使用`long`类型

## 测试建议

### 单元测试
```bash
# 构建项目
dotnet build CryptoSpot.sln

# 运行测试
dotnet test CryptoSpot.sln
```

### 集成测试
1. 创建测试订单
2. 执行撮合交易
3. 查询K线数据
4. 验证所有ID都是long类型
5. 检查性能指标

## 相关文件

- `scripts/init-database.sql` - 数据库初始化脚本
- `scripts/migrate-ids-to-bigint-simple.sql` - 迁移脚本
- `src/CryptoSpot.Domain/Entities/` - 实体定义
- `src/CryptoSpot.Persistence/Data/ApplicationDbContext.cs` - EF Core配置

## 更新日期

- 2025-11-22: 初始版本，TradingPairId统一为BIGINT

## 联系支持

如有问题，请联系开发团队或查阅项目文档。
