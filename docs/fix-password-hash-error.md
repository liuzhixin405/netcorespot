# 密码哈希错误修复指南

## 问题描述

登录时出现错误：
```
System.Exception: PasswordHasherVerify:The input is not a valid Base-64 string
```

**原因：** Users 表缺少 `Email` 和 `PasswordHash` 字段

## 解决方案

### 方案1：修复现有数据库（推荐）

```bash
# 连接到 MySQL 并执行修复脚本
mysql -u root -p cryptospot < scripts/fix-user-table.sql
```

### 方案2：重新初始化数据库

```bash
# 删除并重建数据库（会丢失所有数据！）
mysql -u root -p < scripts/init-database.sql
```

## 测试账号

修复后可使用以下账号登录：

| 用户名 | 密码 | 类型 | 说明 |
|--------|------|------|------|
| test_user_1 | test123 | Regular | 普通测试用户1 |
| test_user_2 | test123 | Regular | 普通测试用户2 |
| test_user_3 | test123 | Regular | 普通测试用户3 |
| SystemMarketMaker | maker123 | MarketMaker | 系统做市商 |
| SystemAdmin | admin123 | Admin | 系统管理员 |

## 密码哈希生成

如需生成新的密码哈希，运行：

```powershell
.\scripts\generate-password-hash.ps1
```

## 验证修复

执行修复脚本后，应该看到：

```
✅ 数据库修复完成！

测试账号信息：
- test_user_1 / test123
- test_user_2 / test123
- test_user_3 / test123
- SystemMarketMaker / maker123
- SystemAdmin / admin123
```

然后尝试登录：

```bash
# 使用 curl 测试
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"test_user_1","password":"test123"}'
```

## 注意事项

⚠️ **生产环境警告**

1. 修复脚本中的密码是测试密码，**生产环境必须修改**
2. 建议在生产环境中：
   - 使用强密码
   - 重新生成密码哈希
   - 启用密码过期策略
   - 实施多因素认证

## 相关文件

- `scripts/fix-user-table.sql` - 数据库修复脚本
- `scripts/init-database.sql` - 完整初始化脚本（已更新）
- `scripts/generate-password-hash.ps1` - 密码哈希生成工具
- `src/CryptoSpot.Infrastructure/Identity/PasswordHasher.cs` - 密码哈希实现

## 密码哈希算法

当前使用 PBKDF2 算法：
- 盐大小：16 字节
- 哈希大小：20 字节
- 迭代次数：10,000
- 存储格式：Base64(Salt + Hash)
