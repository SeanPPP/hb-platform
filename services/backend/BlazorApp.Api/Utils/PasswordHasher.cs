using System.Security.Cryptography;
using System.Text;

namespace BlazorApp.Api.Utils
{
    /// <summary>
    /// 密码加密工具类
    /// 🔐 提供统一的密码哈希和验证功能
    /// 新密码使用 PBKDF2 慢哈希；旧 SHA256 固定盐格式仅用于登录迁移兼容。
    /// </summary>
    public static class PasswordHasher
    {
        public const string PasswordFormatRaw = "raw";
        public const string PasswordFormatClientSha256 = "clientSha256";

        private const string LegacySalt = "h_platform_2025_Salt";
        private const string Pbkdf2Prefix = "pbkdf2-sha256";
        private const int Pbkdf2Iterations = 100_000;
        private const int SaltByteSize = 16;
        private const int HashByteSize = 32;

        /// <summary>
        /// 对原始密码进行哈希加密
        /// 🔐 新密码统一使用每用户随机盐 + PBKDF2，存储格式自带版本信息。
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
                var saltBytes = RandomNumberGenerator.GetBytes(SaltByteSize);
                var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    saltBytes,
                    Pbkdf2Iterations,
                    HashAlgorithmName.SHA256,
                    HashByteSize
                );

                return string.Join(
                    "$",
                    Pbkdf2Prefix,
                    Pbkdf2Iterations.ToString(),
                    Convert.ToBase64String(saltBytes),
                    Convert.ToBase64String(hashBytes)
                );
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("密码加密失败", ex);
            }
        }

        /// <summary>
        /// 生成旧版 SHA256 + 固定盐哈希，仅用于兼容历史数据和测试夹具。
        /// </summary>
        public static string HashLegacyPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("密码不能为空", nameof(password));
            }

            using var sha256 = SHA256.Create();
            var passwordBytes = Encoding.UTF8.GetBytes(password + LegacySalt);
            var hashedBytes = sha256.ComputeHash(passwordBytes);
            return Convert.ToBase64String(hashedBytes);
        }

        /// <summary>
        /// 根据客户端提交格式生成入库哈希；旧客户端 SHA256 值继续按 legacy 存储，便于后续 raw 登录迁移。
        /// </summary>
        public static string HashSubmittedPassword(string password, string? passwordFormat)
        {
            var normalizedFormat = NormalizePasswordFormat(passwordFormat);
            return normalizedFormat == PasswordFormatRaw
                ? HashPassword(password)
                : HashLegacyPassword(password);
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
            return VerifyPassword(password, hashedPassword, PasswordFormatRaw, out _);
        }

        /// <summary>
        /// 验证密码并判断是否需要迁移旧哈希。
        /// </summary>
        /// <param name="password">客户端提交的密码值，可能是原始密码或旧客户端 SHA256。</param>
        /// <param name="hashedPassword">数据库中的密码哈希。</param>
        /// <param name="passwordFormat">客户端密码格式：raw 或 clientSha256。</param>
        /// <param name="needsRehash">旧哈希用原始密码验证成功时返回 true，调用方应重算新哈希。</param>
        public static bool VerifyPassword(
            string password,
            string hashedPassword,
            string? passwordFormat,
            out bool needsRehash
        )
        {
            needsRehash = false;
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
            {
                return false;
            }

            try
            {
                var normalizedFormat = NormalizePasswordFormat(passwordFormat);
                if (IsPbkdf2Hash(hashedPassword))
                {
                    // 新格式只接受原始密码，旧客户端提交的 SHA256 不能再作为登录凭据。
                    return normalizedFormat == PasswordFormatRaw
                        && VerifyPbkdf2Password(password, hashedPassword);
                }

                var legacyPasswordValue =
                    normalizedFormat == PasswordFormatRaw ? ComputeSha256(password) : password;
                var inputHash = HashLegacyPassword(legacyPasswordValue);
                var isValid = CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(inputHash),
                    Encoding.UTF8.GetBytes(hashedPassword)
                );

                // 只有拿到原始密码时才能安全迁移，旧客户端 SHA256 登录只兼容不升级。
                needsRehash = isValid && normalizedFormat == PasswordFormatRaw;
                return isValid;
            }
            catch
            {
                // 💥 如果加密过程出现异常，返回false
                return false;
            }
        }

        private static string NormalizePasswordFormat(string? passwordFormat)
        {
            return string.Equals(passwordFormat, PasswordFormatRaw, StringComparison.OrdinalIgnoreCase)
                ? PasswordFormatRaw
                : PasswordFormatClientSha256;
        }

        private static bool IsPbkdf2Hash(string hashedPassword)
        {
            return hashedPassword.StartsWith($"{Pbkdf2Prefix}$", StringComparison.Ordinal);
        }

        private static bool VerifyPbkdf2Password(string password, string hashedPassword)
        {
            var parts = hashedPassword.Split('$');
            if (parts.Length != 4 || parts[0] != Pbkdf2Prefix)
            {
                return false;
            }

            if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
            {
                return false;
            }

            var saltBytes = Convert.FromBase64String(parts[2]);
            var storedHashBytes = Convert.FromBase64String(parts[3]);
            var inputHashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                iterations,
                HashAlgorithmName.SHA256,
                storedHashBytes.Length
            );

            return CryptographicOperations.FixedTimeEquals(inputHashBytes, storedHashBytes);
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
        /// 仅用于识别旧客户端 SHA256 登录值，新增密码不得再调用它做入库哈希。
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
