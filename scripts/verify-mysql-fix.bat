@echo off
echo ================================================
echo MySQL 连接池并发问题验证脚本
echo ================================================
echo.

echo [1/5] 检查应用进程...
tasklist /FI "IMAGENAME eq dotnet.exe" 2>NUL | find /I "dotnet.exe" >NUL
if errorlevel 1 (
    echo ❌ 应用未运行，请先启动应用
    pause
    exit /b 1
) else (
    echo ✅ 应用正在运行
)
echo.

echo [2/5] 检查日志中的批处理服务启动...
findstr /C:"PriceUpdateBatchService 已启动" logs\app.log >NUL 2>&1
if errorlevel 1 (
    echo ⚠️ 未找到批处理服务启动日志
    echo    可能是日志文件路径错误或服务未启动
) else (
    echo ✅ 批处理服务已启动
)
echo.

echo [3/5] 检查是否有并发错误...
echo 最近 10 条 ServerSession 错误:
findstr /C:"ServerSession is not connected" logs\app.log | more +0 >NUL 2>&1
if errorlevel 1 (
    echo ✅ 没有发现 ServerSession 连接错误
) else (
    findstr /C:"ServerSession is not connected" logs\app.log | find /C "ServerSession" >NUL 2>&1
    echo ⚠️ 发现 ServerSession 连接错误，数量见上
)
echo.

echo 最近 10 条并发读取错误:
findstr /C:"read operation is pending" logs\app.log | more +0 >NUL 2>&1
if errorlevel 1 (
    echo ✅ 没有发现并发读取错误
) else (
    findstr /C:"read operation is pending" logs\app.log | find /C "read operation" >NUL 2>&1
    echo ⚠️ 发现并发读取错误，数量见上
)
echo.

echo [4/5] 检查批处理日志（最近 5 条）...
powershell -Command "Get-Content logs\app.log -Tail 1000 | Select-String '批处理价格更新' | Select-Object -Last 5"
echo.

echo [5/5] 检查 Ticker 推送日志（最近 5 条）...
powershell -Command "Get-Content logs\app.log -Tail 1000 | Select-String 'Ticker Relay 推送完成' | Select-Object -Last 5"
echo.

echo ================================================
echo 验证完成！
echo ================================================
echo.
echo 💡 建议：
echo 1. 如果仍有并发错误，检查 MySQL max_connections 配置
echo 2. 监控批处理服务日志确认正常工作
echo 3. 检查前端是否正常接收 24H 数据更新
echo.
pause
