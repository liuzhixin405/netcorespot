# Redis-First 架构验证脚本

## 1. 编译验证
```bash
cd g:\github\netcorespot
dotnet build --no-incremental
# 预期: 0错误, 14警告(async/await)
```

## 2. 启动验证
```bash
cd g:\github\netcorespot\src\CryptoSpot.API
dotnet run

# 预期日志:
# ✅ Redis数据加载完成: 用户=XX, 资产=XX, 订单=XX
# ✅ Redis → MySQL 同步服务已启动 (间隔: 10秒)
# ✅ 实时价格推送服务已启动
```

## 3. Redis数据验证
```bash
redis-cli

# 查看用户数据
KEYS user:*
HGETALL user:1

# 查看资产数据
KEYS asset:*
HGETALL asset:1:USDT
HGETALL asset:1:BTC

# 查看活跃订单
KEYS order:*
HGETALL order:1

# 查看订单簿(SortedSet)
ZRANGE orders:active:BTCUSDT:Buy 0 10 WITHSCORES
ZRANGE orders:active:BTCUSDT:Sell 0 10 WITHSCORES

# 查看用户订单索引
SMEMBERS user_orders:1

# 查看同步队列
LLEN sync_queue:orders
LLEN sync_queue:trades  
LLEN sync_queue:assets
```

## 4. 功能测试 (可选,需重构OrderV2Controller)

### 4.1 下单测试
```bash
curl -X POST http://localhost:5000/api/v1/order \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
    "symbol": "BTCUSDT",
    "side": "buy",
    "type": "limit",
    "price": 50000,
    "quantity": 0.001
  }'

# 预期: 订单立即写入Redis
```

### 4.2 验证Redis写入
```bash
redis-cli
KEYS order:*  # 应该看到新订单
HGETALL order:新订单ID

# 验证订单簿更新
ZRANGE orders:active:BTCUSDT:Buy 0 -1 WITHSCORES
```

### 4.3 验证MySQL同步 (10秒后)
```sql
-- 连接MySQL
mysql -u root -p cryptospot

-- 查询订单表
SELECT * FROM orders ORDER BY id DESC LIMIT 10;

-- 应该在10-15秒后看到新订单
```

## 5. 性能测试

### 5.1 下单延迟测试
```bash
# 使用JMeter或Apache Bench
ab -n 1000 -c 10 \
  -H "Authorization: Bearer TOKEN" \
  -p order.json \
  http://localhost:5000/api/v1/order

# 预期: 平均响应时间 <10ms
```

### 5.2 Redis延迟监控
```bash
redis-cli --latency-history
# 预期: 大部分命令 <1ms
```

### 5.3 MySQL同步监控
```bash
# 观察应用日志
tail -f logs/app.log | grep "同步完成"

# 预期输出:
# ✅ 同步完成: 订单=50, 成交=30, 资产=100 (耗时: 123ms)
```

## 6. 压力测试

### 6.1 并发下单
```bash
# 使用wrk压测
wrk -t10 -c100 -d30s \
  -H "Authorization: Bearer TOKEN" \
  -s order.lua \
  http://localhost:5000/api/v1/order

# 预期: TPS >1000
```

### 6.2 Redis内存监控
```bash
redis-cli INFO memory
# 关注: used_memory_human, maxmemory_policy
```

### 6.3 MySQL负载监控
```bash
# 观察慢查询日志
tail -f /var/log/mysql/slow-query.log

# 预期: 查询频率大幅降低 (90%+)
```

## 7. 一致性验证

### 7.1 数据对比
```python
# Python脚本
import redis
import mysql.connector

r = redis.Redis()
db = mysql.connector.connect(user='root', password='', database='cryptospot')

# 对比订单数量
redis_orders = len(r.keys('order:*'))
cursor = db.cursor()
cursor.execute("SELECT COUNT(*) FROM orders WHERE status = 'Active'")
mysql_orders = cursor.fetchone()[0]

print(f"Redis订单: {redis_orders}")
print(f"MySQL订单: {mysql_orders}")
print(f"差异: {abs(redis_orders - mysql_orders)} (应该 <100, 10秒同步窗口)")
```

### 7.2 资产余额验证
```bash
# Redis查询
redis-cli HGET asset:1:USDT Available
redis-cli HGET asset:1:USDT Frozen

# MySQL查询
mysql> SELECT Available, Frozen FROM assets 
       WHERE UserId=1 AND Symbol='USDT';

# 对比两者差异,应该在同步间隔内一致
```

## 8. 故障恢复测试

### 8.1 Redis重启测试
```bash
# 1. 停止Redis
redis-cli SHUTDOWN

# 2. 重启Redis
redis-server

# 3. 重启应用
# 预期: RedisDataLoaderService自动从MySQL加载数据
```

### 8.2 MySQL重启测试
```bash
# 1. 停止MySQL
systemctl stop mysql

# 2. 应用继续运行
# 预期: 交易正常, 同步队列累积

# 3. 恢复MySQL
systemctl start mysql

# 预期: 同步服务自动恢复, 补写累积数据
```

## 9. 监控指标

### 9.1 关键指标
- Redis命令延迟: <1ms
- 下单延迟: <10ms
- 撮合延迟: <5ms
- 同步延迟: <15秒
- Redis内存使用: <80%
- MySQL写入QPS: 降低90%+
- 订单簿深度: >100档

### 9.2 告警阈值
- Redis延迟 >10ms
- 同步队列长度 >1000
- 同步失败次数 >5
- Redis内存使用 >90%
- MySQL主从延迟 >5秒

## 10. 问题排查

### 10.1 Redis连接问题
```bash
redis-cli PING
# 预期: PONG

netstat -an | grep 6379
# 预期: LISTEN状态
```

### 10.2 应用日志
```bash
cd g:\github\netcorespot\src\CryptoSpot.API
dotnet run --verbosity detailed

# 关注关键日志:
# - Redis数据加载完成
# - Redis → MySQL 同步服务已启动
# - 订单撮合成功
# - 同步完成统计
```

### 10.3 性能分析
```bash
# 使用dotnet-trace
dotnet-trace collect --process-id PID --duration 00:00:30

# 分析热点函数
dotnet-trace analyze trace.nettrace
```

---

## ✅ 验证清单

### 基础验证
- [ ] 项目编译成功 (0错误)
- [ ] 应用启动成功
- [ ] Redis连接正常
- [ ] MySQL连接正常

### 数据验证
- [ ] RedisDataLoaderService加载成功
- [ ] 用户数据已加载
- [ ] 资产数据已加载
- [ ] 活跃订单已加载
- [ ] 订单簿已构建

### 功能验证
- [ ] 下单功能正常
- [ ] 撮合功能正常
- [ ] 资产冻结/解冻正常
- [ ] 订单状态更新正常
- [ ] 实时推送正常

### 同步验证
- [ ] RedisMySqlSyncService启动成功
- [ ] 订单同步正常
- [ ] 成交同步正常
- [ ] 资产同步正常
- [ ] 同步延迟 <15秒

### 性能验证
- [ ] 下单延迟 <10ms
- [ ] 查询延迟 <1ms
- [ ] 并发TPS >1000
- [ ] Redis命令延迟 <1ms
- [ ] MySQL负载降低 >90%

### 一致性验证
- [ ] Redis与MySQL数据一致
- [ ] 资产余额准确
- [ ] 订单数量准确
- [ ] 成交记录完整

### 稳定性验证
- [ ] 长时间运行稳定 (>24h)
- [ ] 内存无泄漏
- [ ] 无死锁
- [ ] 故障恢复正常

---

**使用说明**: 
1. 按顺序执行各项验证
2. 记录每项结果
3. 发现问题及时修复
4. 全部通过后可上线

**预期结果**: 
✅ 所有验证通过
✅ 性能提升10-100倍
✅ 数据库压力降低90%+
✅ Redis-First架构完全可用
