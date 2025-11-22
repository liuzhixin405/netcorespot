using CryptoSpot.Application.Common.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace CryptoSpot.Infrastructure.Identity
{
    /// <summary>
    /// 密码哈希服务实现（使用 BCrypt 或 PBKDF2）
    /// </summary>
    public class PasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 20;
        private const int Iterations = 10000;

        public string Hash(string password)
        {
            // 生成盐
            byte[] salt;
            RandomNumberGenerator.Create().GetBytes(salt = new byte[SaltSize]);

            // 生成哈希
            var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations);
            byte[] hash = pbkdf2.GetBytes(HashSize);

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
                byte[] salt = new byte[SaltSize];
                Array.Copy(hashBytes, 0, salt, 0, SaltSize);

                // 计算输入密码的哈希
                var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations);
                byte[] passwordHash = pbkdf2.GetBytes(HashSize);

                // 比较
                for (int i = 0; i < HashSize; i++)
                {
                    if (hashBytes[i + SaltSize] != passwordHash[i])
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
