using System.Text;

namespace EasyNoteVault;

public static class CryptoService
{
    // 🔥 验证版：不做任何加密
    public static byte[] Encrypt(string plainText)
    {
        return Encoding.UTF8.GetBytes(plainText);
    }

    public static string Decrypt(byte[] cipherBytes)
    {
        return Encoding.UTF8.GetString(cipherBytes);
    }
}
