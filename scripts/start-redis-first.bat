@echo off
chcp 65001 >nul
echo ================================================
echo Redis-First 架构启动验证脚本
echo ================================================
echo.

REM 检查 Redis 是否运行
echo [1/6] 检查 Redis 服务...
redis-cli ping >nul 2>&1
if errorlevel 1 (
    echo ❌ Redis 未运行，请先启动 Redis
    echo.
    echo 启动 Redis:
    echo   Windows: redis-server
    echo   Linux/Mac: sudo service redis-server start
    pause
    exit /b 1
) else (
    echo ✅ Redis 正常运行
)
echo.

REM 检查 MySQL 连接
echo [2/6] 检查 MySQL 连接...
mysql -u root -p123456 -e "SELECT 1" >nul 2>&1
if errorlevel 1 (
    echo ⚠️ MySQL 连接失败，请检查用户名密码
) else (
    echo ✅ MySQL 连接正常
)
echo.

REM 清理 Redis 数据（可选）
echo [3/6] 是否清理 Redis 数据？(y/n)
set /p CLEAN_REDIS="选择 (默认 n): "
if /i "%CLEAN_REDIS%"=="y" (
    echo 🧹 清理 Redis 数据...
    redis-cli FLUSHALL
    echo ✅ Redis 数据已清理
) else (
    echo ⏭️ 跳过 Redis 清理
)
echo.

REM 编译项目
echo [4/6] 编译项目...
cd /d "%~dp0..\src\CryptoSpot.API"
dotnet build -c Release >nul 2>&1
if errorlevel 1 (
    echo ❌ 编译失败，请查看错误信息
    dotnet build
    pause
    exit /b 1
) else (
    echo ✅ 编译成功
)
echo.

REM 启动应用
echo [5/6] 启动应用...
echo.
echo 📝 关键启动日志监控点:
echo   1. "🚀 开始从 MySQL 加载数据到 Redis..."
echo   2. "✅ 数据加载完成！Redis-First 架构已就绪"
echo   3. "✅ Redis → MySQL 同步服务已启动"
echo.
echo ================================================
echo 正在启动应用...
echo ================================================
echo.

start cmd /k "title CryptoSpot API && dotnet run"

REM 等待启动
timeout /t 15 /nobreak >nul

echo.
echo [6/6] 验证启动状态...
echo.

REM 检查 Redis 数据加载
echo 📊 Redis 数据统计:
redis-cli DBSIZE
echo.

echo 📋 检查关键 Key:
redis-cli EXISTS user:1
redis-cli EXISTS trading_pair:BTCUSDT
redis-cli EXISTS orders:active:BTCUSDT:Buy
echo.

echo ================================================
echo 启动完成！
echo ================================================
echo.
echo 📍 API 地址: http://localhost:5000
echo 📍 Swagger: http://localhost:5000/swagger
echo 📍 SignalR Hub: ws://localhost:5000/tradingHub
echo.
echo 💡 测试 Redis-First API:
echo.
echo   POST http://localhost:5000/api/v2/order
echo   {
echo     "symbol": "BTCUSDT",
echo     "side": "Buy",
echo     "type": "Limit",
echo     "price": 50000,
echo     "quantity": 0.1
echo   }
echo.
echo   GET http://localhost:5000/api/v2/order/my-orders
echo   GET http://localhost:5000/api/v2/assets
echo.
echo ================================================
echo.
pause
