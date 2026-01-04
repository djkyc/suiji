using System.Security.Cryptography;
using System.Text;

namespace EasyNoteVault
{
    public static class CryptoService
    {
        // v0.4 固定主密码（以后可以做成启动输入）
        private const string MasterPassword = "EasyNoteVault@2026";

        private static readonly byte[] Salt =
            Encoding.UTF8.GetBytes("EasyNoteVault_Salt_2026");

        public static byte[] Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            using var key = new Rfc2898DeriveBytes(
                MasterPassword,
                Salt,
                100_000,
                HashAlgorithmName.SHA256);

            aes.Key = key.GetBytes(32); // AES-256
            aes.IV = key.GetBytes(16);

            using var encryptor = aes.CreateEncryptor();
            var bytes = Encoding.UTF8.GetBytes(plainText);
            return encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        }

        public static string Decrypt(byte[] cipherBytes)
        {
            using var aes = Aes.Create();
            using var key = new Rfc2898DeriveBytes(
                MasterPassword,
                Salt,
                100_000,
                HashAlgorithmName.SHA256);

            aes.Key = key.GetBytes(32);
            aes.IV = key.GetBytes(16);

            using var decryptor = aes.CreateDecryptor();
            var plainBytes =
                decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
