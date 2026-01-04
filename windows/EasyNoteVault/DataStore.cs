#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq; // ✅ 修复：ToList 扩展方法
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace EasyNoteVault
{
    public static class DataStore
    {
        // 存在程序目录：data.enc
        private static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.enc");

        public static List<VaultItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<VaultItem>();

                byte[] enc = File.ReadAllBytes(FilePath);
                if (enc.Length == 0)
                    return new List<VaultItem>();

                // DPAPI 解密（与当前 Windows 用户绑定）
                byte[] raw = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);

                string json = Encoding.UTF8.GetString(raw);
                var list = JsonSerializer.Deserialize<List<VaultItem>>(json);

                return list ?? new List<VaultItem>();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 data.enc 失败：\n{ex.Message}", "EasyNoteVault",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                return new List<VaultItem>();
            }
        }

        public static void Save(ObservableCollection<VaultItem> items)
        {
            try
            {
                var list = items?.ToList() ?? new List<VaultItem>();

                string json = JsonSerializer.Serialize(list, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                byte[] raw = Encoding.UTF8.GetBytes(json);

                // DPAPI 加密（与当前 Windows 用户绑定）
                byte[] enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);

                File.WriteAllBytes(FilePath, enc);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存 data.enc 失败：\n{ex.Message}", "EasyNoteVault",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
