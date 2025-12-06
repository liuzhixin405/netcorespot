using CryptoSpot.Application.Common.Interfaces;
using System.Security.Cryptography;

namespace CryptoSpot.Infrastructure.Identity
{
    /// <summary>
    /// 密码哈希服务实现（使用 PBKDF2 + SHA256）
    /// </summary>
    public class PasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100000;

        public string Hash(string password)
        {
            // 生成盐
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

            // 使用 SHA256 生成哈希
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            // 组合盐和哈希
            byte[] hashBytes = new byte[SaltSize + HashSize];
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);

            return Convert.ToBase64String(hashBytes);
        }

        public bool Verify(string password, string hash)
        {
            try
            {
                // 提取盐和哈希
                byte[] hashBytes = Convert.FromBase64String(hash);
                
                // 检查长度是否有效
                if (hashBytes.Length < SaltSize)
                    return false;
                
                byte[] salt = new byte[SaltSize];
                Array.Copy(hashBytes, 0, salt, 0, SaltSize);

                // 计算输入密码的哈希
                int storedHashSize = hashBytes.Length - SaltSize;
                byte[] passwordHash = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    Iterations,
                    HashAlgorithmName.SHA256,
                    storedHashSize);

                // 使用固定时间比较防止时序攻击
                return CryptographicOperations.FixedTimeEquals(
                    hashBytes.AsSpan(SaltSize),
                    passwordHash);
            }
            catch
            {
                return false;
            }
        }
    }
}
