-- MySQL 连接数和性能检查脚本
-- 使用方法: mysql -u root -p < check-mysql-config.sql

-- 显示当前连接数配置
SELECT '=== MySQL 连接数配置 ===' AS '';
SHOW VARIABLES LIKE 'max_connections';
SHOW VARIABLES LIKE 'max_user_connections';
SHOW VARIABLES LIKE 'wait_timeout';
SHOW VARIABLES LIKE 'interactive_timeout';

-- 显示当前活跃连接
SELECT '\n=== 当前活跃连接统计 ===' AS '';
SHOW STATUS LIKE 'Threads_connected';
SHOW STATUS LIKE 'Threads_running';
SHOW STATUS LIKE 'Max_used_connections';
SHOW STATUS LIKE 'Aborted_connects';

-- 显示当前所有连接详情
SELECT '\n=== 当前连接详情（前 20 条） ===' AS '';
SELECT 
    ID,
    USER,
    HOST,
    DB,
    COMMAND,
    TIME,
    STATE,
    LEFT(INFO, 50) AS QUERY_PREVIEW
FROM INFORMATION_SCHEMA.PROCESSLIST
ORDER BY TIME DESC
LIMIT 20;

-- 检查锁等待
SELECT '\n=== InnoDB 锁等待 ===' AS '';
SELECT 
    r.trx_id AS waiting_trx_id,
    r.trx_mysql_thread_id AS waiting_thread,
    r.trx_query AS waiting_query,
    b.trx_id AS blocking_trx_id,
    b.trx_mysql_thread_id AS blocking_thread,
    b.trx_query AS blocking_query
FROM INFORMATION_SCHEMA.INNODB_LOCK_WAITS w
INNER JOIN INFORMATION_SCHEMA.INNODB_TRX b ON b.trx_id = w.blocking_trx_id
INNER JOIN INFORMATION_SCHEMA.INNODB_TRX r ON r.trx_id = w.requesting_trx_id;

-- 检查慢查询配置
SELECT '\n=== 慢查询日志配置 ===' AS '';
SHOW VARIABLES LIKE 'slow_query_log';
SHOW VARIABLES LIKE 'long_query_time';
SHOW STATUS LIKE 'Slow_queries';

-- 优化建议
SELECT '\n=== 优化建议 ===' AS '';
SELECT 
    CASE 
        WHEN (SELECT VARIABLE_VALUE FROM INFORMATION_SCHEMA.GLOBAL_STATUS WHERE VARIABLE_NAME='Max_used_connections') > 
             (SELECT VARIABLE_VALUE FROM INFORMATION_SCHEMA.GLOBAL_VARIABLES WHERE VARIABLE_NAME='max_connections') * 0.8
        THEN '⚠️ 建议增加 max_connections（当前接近上限）'
        ELSE '✅ 连接池配置正常'
    END AS connection_pool_status;

SELECT 
    CASE 
        WHEN (SELECT VARIABLE_VALUE FROM INFORMATION_SCHEMA.GLOBAL_STATUS WHERE VARIABLE_NAME='Aborted_connects') > 100
        THEN '⚠️ 发现较多中断连接，检查超时配置'
        ELSE '✅ 连接稳定性正常'
    END AS connection_stability;

-- 建议的优化配置
SELECT '\n=== 建议的 MySQL 配置（如需优化） ===' AS '';
SELECT '-- 在 my.ini 或 my.cnf 中添加/修改以下配置:' AS '';
SELECT 'max_connections = 500' AS '';
SELECT 'max_user_connections = 400' AS '';
SELECT 'wait_timeout = 600' AS '';
SELECT 'interactive_timeout = 600' AS '';
SELECT 'max_allowed_packet = 64M' AS '';
SELECT 'innodb_buffer_pool_size = 1G' AS '';
SELECT 'innodb_log_file_size = 256M' AS '';
