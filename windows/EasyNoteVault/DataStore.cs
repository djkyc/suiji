using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EasyNoteVault.Sync;

namespace EasyNoteVault;

公共 static class DataStore
{
    private static readonly string FilePath =
        Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "data.enc");

    // v1.0：WebDAV 配置（后面会做成设置界面）
    private static readonly WebDavSyncService? SyncService = null;
        new WebDavSyncService(
            baseUrl: "https://dav.example.com/yourpath",
            username: "username",
            password: "password"
        );

    public static List<VaultItem> Load()
    {
        // 启动时尝试从云端拉取
        TryDownloadFromCloud();

        if (!File.Exists(FilePath))
            return new List<VaultItem>();

        var encryptedBytes = File.ReadAllBytes(FilePath);
        var json = CryptoService.Decrypt(encryptedBytes);

        return JsonSerializer.Deserialize<List<VaultItem>>(json)
               ?? new List<VaultItem>();
    }

    public static void Save(IEnumerable<VaultItem> items)
    {
        var json = JsonSerializer.Serialize(
            items,
            new JsonSerializerOptions { WriteIndented = true });

        var encryptedBytes = CryptoService.Encrypt(json);
        File.WriteAllBytes(FilePath, encryptedBytes);

        // 保存后自动上传
        TryUploadToCloud();
    }

    private static async void TryUploadToCloud()
    {
        if (SyncService == null)
            return;

        try
        {
            await SyncService.UploadAsync(FilePath);
        }
        catch
        {
            // v1.0：忽略网络异常（不影响本地）
        }
    }

    private static async void TryDownloadFromCloud()
    {
        if (SyncService == null)
            return;

        try
        {
            var remoteTime = await SyncService.GetRemoteLastModifiedAsync();
            var localTime = File.Exists(FilePath)
                ? File.GetLastWriteTimeUtc(FilePath)
                : (DateTime?)null;

            // 云端较新，下载覆盖本地
            if (remoteTime != null &&
                (localTime == null || remoteTime > localTime))
            {
                await SyncService.DownloadAsync(FilePath);
            }
        }
        catch
        {
            // v1.0：忽略异常
        }
    }
}
