using System.Security.Cryptography;
using System.Text;

namespace BlazorApp.Api.Utils
{
    /// <summary>
    /// 密码加密工具类
    /// 🔐 提供统一的密码哈希和验证功能
    /// 使用SHA256算法 + Salt进行密码加密
    /// </summary>
    public static class PasswordHasher
    {
        /// <summary>
        /// 密码加密盐值
        /// 🧂 用于增强密码安全性，防止彩虹表攻击
        /// </summary>
        private const string SALT = "h_platform_2025_Salt";

        /// <summary>
        /// 对密码进行哈希加密
        /// 🔐 使用SHA256算法 + Salt进行单向加密
        /// </summary>
        /// <param name="password">原始密码</param>
        /// <returns>Base64编码的哈希值</returns>
        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("密码不能为空", nameof(password));
            }

            try
            {
                // 🔧 创建SHA256哈希算法实例
                using var sha256 = SHA256.Create();

                // 📝 将密码 + Salt转换为字节数组
                var passwordWithSalt = password + SALT;
                var passwordBytes = Encoding.UTF8.GetBytes(passwordWithSalt);

                // 🔄 计算哈希值
                var hashedBytes = sha256.ComputeHash(passwordBytes);

                // 📄 将哈希字节数组转换为Base64字符串
                // Base64编码便于存储和传输
                return Convert.ToBase64String(hashedBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("密码加密失败", ex);
            }
        }

        /// <summary>
        /// 验证密码是否正确
        /// ✅ 将输入的密码进行哈希后与存储的哈希值比较
        /// </summary>
        /// <param name="password">待验证的原始密码</param>
        /// <param name="hashedPassword">数据库中存储的哈希密码</param>
        /// <returns>true表示密码正确，false表示密码错误</returns>
        public static bool VerifyPassword(string password, string hashedPassword)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
            {
                return false;
            }

            try
            {
                // 🔐 对输入的密码进行哈希
                var inputHash = HashPassword(password);

                // ✅ 比较哈希值是否相等
                return inputHash.Equals(hashedPassword, StringComparison.Ordinal);
            }
            catch
            {
                // 💥 如果加密过程出现异常，返回false
                return false;
            }
        }

        /// <summary>
        /// 生成随机密码
        /// 🔑 生成包含大小写字母、数字和特殊字符的随机密码
        /// </summary>
        /// <param name="length">密码长度，默认12位</param>
        /// <returns>随机生成的密码</returns>
        public static string GenerateRandomPassword(int length = 12)
        {
            if (length < 8)
            {
                throw new ArgumentException("密码长度不能少于8位", nameof(length));
            }

            try
            {
                // 📋 定义字符集
                const string lowercase = "abcdefghijklmnopqrstuvwxyz";
                const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                const string digits = "0123456789";
                const string special = "!@#$%^&*()_+-=[]{}|;:,.<>?";

                // 🔧 创建随机数生成器（非过时API）
                using var rng = RandomNumberGenerator.Create();
                var password = new char[length];
                var allChars = lowercase + uppercase + digits + special;

                // 🎲 生成随机密码
                for (int i = 0; i < length; i++)
                {
                    var idx = RandomNumberGenerator.GetInt32(allChars.Length);
                    password[i] = allChars[idx];
                }

                // ✅ 确保密码包含至少一个字符类型
                password[0] = lowercase[Random.Shared.Next(lowercase.Length)];
                password[1] = uppercase[Random.Shared.Next(uppercase.Length)];
                password[2] = digits[Random.Shared.Next(digits.Length)];
                password[3] = special[Random.Shared.Next(special.Length)];

                // 🔀 打乱密码字符顺序
                for (int i = password.Length - 1; i > 0; i--)
                {
                    var j = RandomNumberGenerator.GetInt32(i + 1);
                    (password[i], password[j]) = (password[j], password[i]);
                }

                return new string(password);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("生成随机密码失败", ex);
            }
        }

        /// <summary>
        /// 计算SHA256哈希值（不加盐）
        /// 用于模拟前端哈希行为（例如重置密码时）
        /// </summary>
        /// <param name="rawData">原始数据</param>
        /// <returns>SHA256哈希值（小写Hex字符串）</returns>
        public static string ComputeSha256(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        /// <summary>
        /// 检查密码强度
        /// 📊 评估密码的安全强度
        /// </summary>
        /// <param name="password">待检查的密码</param>
        /// <returns>密码强度评估结果</returns>
        public static PasswordStrength CheckPasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return PasswordStrength.VeryWeak;
            }

            // 如果是64位Hex字符串，认为是前端传来的哈希，视为强密码
            // 前端哈希通常是 SHA256 Hex 字符串 (64字符)
            if (password.Length == 64 && System.Text.RegularExpressions.Regex.IsMatch(password, "^[0-9a-fA-F]{64}$"))
            {
                return PasswordStrength.Strong;
            }

            var score = 0;

            // 📏 长度检查
            if (password.Length >= 8)
                score++;
            if (password.Length >= 12)
                score++;

            // 🔤 字符类型检查
            if (password.Any(char.IsLower))
                score++;
            if (password.Any(char.IsUpper))
                score++;
            if (password.Any(char.IsDigit))
                score++;
            if (password.Any(c => !char.IsLetterOrDigit(c)))
                score++;

            // 🔄 重复字符检查
            if (password.Distinct().Count() >= password.Length * 0.8)
                score++;

            // 📊 返回强度等级
            return score switch
            {
                0 or 1 => PasswordStrength.VeryWeak,
                2 => PasswordStrength.Weak,
                3 => PasswordStrength.Medium,
                4 => PasswordStrength.Strong,
                _ => PasswordStrength.VeryStrong,
            };
        }
    }

    /// <summary>
    /// 密码强度枚举
    /// 📊 定义密码的安全强度等级
    /// </summary>
    public enum PasswordStrength
    {
        /// <summary>
        /// 非常弱
        /// </summary>
        VeryWeak = 0,

        /// <summary>
        /// 弱
        /// </summary>
        Weak = 1,

        /// <summary>
        /// 中等
        /// </summary>
        Medium = 2,

        /// <summary>
        /// 强
        /// </summary>
        Strong = 3,

        /// <summary>
        /// 非常强
        /// </summary>
        VeryStrong = 4,
    }
}
