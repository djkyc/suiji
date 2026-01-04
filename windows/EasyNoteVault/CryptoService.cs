using System.Security.Cryptography;
using System.Text;

namespace EasyNoteVault
{
    public static class CryptoService
    {
        // v0.3：固定主密码（以后可改成启动输入）
        private const string MasterPassword = "EasyNoteVault@2026";

        private static readonly byte[] Salt =
            Encoding.UTF8.GetBytes("EasyNoteVault_Salt");

        public static byte[] Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            using var key = new Rfc2898DeriveBytes(
                MasterPassword, Salt, 100_000, HashAlgorithmName.SHA256);

            aes.Key = key.GetBytes(32);
            aes.IV = key.GetBytes(16);

            using var encryptor = aes.CreateEncryptor();
            var bytes = Encoding.UTF8.GetBytes(plainText);
            return encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        }

        public static string Decrypt(byte[] cipher)
        {
            using var aes = Aes.Create();
            using var key = new Rfc2898DeriveBytes(
                MasterPassword, Salt, 100_000, HashAlgorithmName.SHA256);

            aes.Key = key.GetBytes(32);
            aes.IV = key.GetBytes(16);

            using var decryptor = aes.CreateDecryptor();
            var bytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
