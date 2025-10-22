@echo off
chcp 65001 >nul
echo ================================================
echo Redis-First 架构功能测试
echo ================================================
echo.

REM 设置 API 地址
set API_URL=http://localhost:5000/api/v2
set TOKEN=

REM 检查 curl 是否可用
curl --version >nul 2>&1
if errorlevel 1 (
    echo ❌ curl 未安装，请先安装 curl
    pause
    exit /b 1
)

echo [1/5] 用户登录...
echo.

REM 登录获取 Token
for /f "delims=" %%i in ('curl -s -X POST "%API_URL%/../auth/login" -H "Content-Type: application/json" -d "{\"username\":\"test_user_1\",\"password\":\"123456\"}" ^| jq -r ".data.token"') do set TOKEN=%%i

if "%TOKEN%"=="" (
    echo ❌ 登录失败，请检查用户名密码
    pause
    exit /b 1
)

echo ✅ 登录成功
echo Token: %TOKEN:~0,20%...
echo.

echo [2/5] 查询资产...
echo.
curl -s -X GET "%API_URL%/assets" -H "Authorization: Bearer %TOKEN%" | jq "."
echo.

echo [3/5] 下限价买单...
echo.
curl -s -X POST "%API_URL%/order" ^
  -H "Content-Type: application/json" ^
  -H "Authorization: Bearer %TOKEN%" ^
  -d "{\"symbol\":\"BTCUSDT\",\"side\":\"Buy\",\"type\":\"Limit\",\"price\":50000,\"quantity\":0.001}" ^
  | jq "."
echo.

echo [4/5] 查询我的订单...
echo.
curl -s -X GET "%API_URL%/order/my-orders" -H "Authorization: Bearer %TOKEN%" | jq ".data | .[] | {orderId, symbol, side, price, quantity, status}"
echo.

echo [5/5] Redis 数据验证...
echo.

echo 📊 订单数量:
redis-cli SCARD user_orders:1

echo.
echo 📊 活跃买单数量:
redis-cli ZCARD orders:active:BTCUSDT:Buy

echo.
echo 📊 活跃卖单数量:
redis-cli ZCARD orders:active:BTCUSDT:Sell

echo.
echo 📊 同步队列长度:
echo   订单队列:
redis-cli LLEN sync_queue:orders
echo   成交队列:
redis-cli LLEN sync_queue:trades
echo   资产队列:
redis-cli LLEN sync_queue:assets

echo.
echo ================================================
echo 测试完成！
echo ================================================
echo.
echo 💡 验证 Redis 数据:
echo   redis-cli KEYS "order:*"
echo   redis-cli KEYS "asset:*"
echo   redis-cli HGETALL order:10001
echo   redis-cli HGETALL asset:1:USDT
echo.
echo 💡 验证 MySQL 同步（等待 10 秒后）:
echo   mysql -u root -p -e "SELECT * FROM Orders ORDER BY Id DESC LIMIT 5;"
echo.
pause
