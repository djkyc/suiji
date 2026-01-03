using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EasyNoteVault
{
    public static class AutoStore
    {
        private static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.enc");

        // 保存（自动、加密）
        public static void Save(IEnumerable<VaultItem> items)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("名称  网站  账号  密码  备注");

                foreach (var i in items)
                {
                    sb.AppendLine(
                        $"{i.Name}  {i.Url}  {i.Account}  {i.Password}  {i.Remark}");
                }

                var encrypted = CryptoHelper.Encrypt(sb.ToString());
                File.WriteAllBytes(FilePath, encrypted);
            }
            catch
            {
                // 自动保存失败不影响程序使用
            }
        }

        // 加载（启动时）
        public static List<VaultItem> Load()
        {
            var list = new List<VaultItem>();

            try
            {
                if (!File.Exists(FilePath))
                    return list;

                var bytes = File.ReadAllBytes(FilePath);
                var text = CryptoHelper.Decrypt(bytes);

                var lines = text.Split(
                    new[] { "\r\n", "\n" },
                    StringSplitOptions.RemoveEmptyEntries);

                for (int i = 1; i < lines.Length; i++) // 跳过表头
                {
                    var parts = lines[i]
                        .Split(new[] { "  " },
                            StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length < 5) continue;

                    list.Add(new VaultItem
                    {
                        Name = parts[0],
                        Url = parts[1],
                        Account = parts[2],
                        Password = parts[3],
                        Remark = parts[4]
                    });
                }
            }
            catch
            {
                // 解密失败 = 当作没数据
            }

            return list;
        }
    }
}
