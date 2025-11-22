# 生成密码哈希的 PowerShell 脚本
# 用途：为测试用户生成 PBKDF2 密码哈希

function Generate-PasswordHash {
    param(
        [string]$Password
    )
    
    $SaltSize = 16
    $HashSize = 20
    $Iterations = 10000
    
    # 生成随机盐
    $salt = New-Object byte[] $SaltSize
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($salt)
    
    # 使用 PBKDF2 生成哈希
    $pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($Password, $salt, $Iterations)
    $hash = $pbkdf2.GetBytes($HashSize)
    
    # 组合盐和哈希
    $hashBytes = New-Object byte[] ($SaltSize + $HashSize)
    [Array]::Copy($salt, 0, $hashBytes, 0, $SaltSize)
    [Array]::Copy($hash, 0, $hashBytes, $SaltSize, $HashSize)
    
    # 转换为 Base64
    return [Convert]::ToBase64String($hashBytes)
}

# 生成测试密码哈希
$passwords = @{
    "test123" = ""
    "admin123" = ""
    "maker123" = ""
}

Write-Host "=== CryptoSpot 密码哈希生成器 ===" -ForegroundColor Cyan
Write-Host ""

foreach ($pwd in $passwords.Keys) {
    $hash = Generate-PasswordHash -Password $pwd
    Write-Host "密码: $pwd" -ForegroundColor Yellow
    Write-Host "哈希: $hash" -ForegroundColor Green
    Write-Host ""
}

Write-Host "使用方法：" -ForegroundColor Cyan
Write-Host "1. 复制上面生成的哈希值"
Write-Host "2. 更新 init-database.sql 中的 INSERT 语句"
Write-Host "3. 执行数据库初始化脚本"
Write-Host ""
