using System.Security.Cryptography;
using System.Text;

namespace EasyNoteVault;

public static class CryptoService
{
    // v1.0：固定主密码（下一步会改为启动输入）
    private const string MasterPassword = "EasyNoteVault@123";

    // 固定盐（Windows / Android 必须一致）
    private static readonly byte[] Salt =
        Encoding.UTF8.GetBytes("EasyNoteVault_Salt_2024");

    public static byte[] Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        using var key = new Rfc2898DeriveBytes(
            MasterPassword,
            Salt,
            100_000,
            HashAlgorithmName.SHA256);

        aes.Key = key.GetBytes(32); // AES-256
        aes.IV = key.GetBytes(16);  // 128-bit IV

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
        var plainBytes = decryptor.TransformFinalBlock(
            cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
