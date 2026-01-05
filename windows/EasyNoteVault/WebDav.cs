#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EasyNoteVault
{
    public enum WebDavProvider
    {
        Jianguoyun = 0,
        Custom = 1
    }

    public class WebDavSettings
    {
        public bool Enabled { get; set; } = false;
        public WebDavProvider Provider { get; set; } = WebDavProvider.Jianguoyun;

        // 默认坚果云
        public string BaseUrl { get; set; } = "https://dav.jianguoyun.com/dav/";
        public string RemoteFolder { get; set; } = "EasyNoteVault";
        public string RemoteFileName { get; set; } = "data.enc";

        public string Username { get; set; } = "";

        // DPAPI 加密后的 Base64
        public string PasswordDpapiBase64 { get; set; } = "";
    }

    public static class WebDavSettingsStore
    {
        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webdav.json");

        public static WebDavSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    var s = new WebDavSettings(); // 默认坚果云模板（不开启）
                    Save(s);
                    return s;
                }

                var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                var s2 = JsonSerializer.Deserialize<WebDavSettings>(json);
                return s2 ?? new WebDavSettings();
            }
            catch
            {
                return new WebDavSettings();
            }
        }

        public static void Save(WebDavSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json, Encoding.UTF8);
        }

        public static void SetPassword(WebDavSettings settings, string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
            {
                settings.PasswordDpapiBase64 = "";
                return;
            }

            byte[] raw = Encoding.UTF8.GetBytes(plainPassword);
            byte[] enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
            settings.PasswordDpapiBase64 = Convert.ToBase64String(enc);
        }

        public static string GetPassword(WebDavSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.PasswordDpapiBase64))
                return "";

            try
            {
                byte[] enc = Convert.FromBase64String(settings.PasswordDpapiBase64);
                byte[] raw = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch
            {
                return "";
            }
        }
    }

    public static class WebDavUrlBuilder
    {
        public static string BuildRemoteFileUrl(WebDavSettings s)
        {
            var baseUrl = (s.BaseUrl ?? "").Trim();
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            var folder = (s.RemoteFolder ?? "").Trim().Trim('/');
            var file = (s.RemoteFileName ?? "").Trim().Trim('/');

            return $"{baseUrl}{folder}/{file}";
        }
    }

    public enum WebDavSyncState
    {
        Disabled = 0,
        Connected = 1,
        Queued = 2,
        Uploaded = 3,
        Failed = 4
    }

    public sealed class WebDavSyncService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly Func<string> _getLocalFilePath;
        private readonly Func<string> _getRemoteFileUrl;

        // 防抖 + 限频（避免免费 WebDAV 频率限制触发）
        private readonly TimeSpan _debounce = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _minUploadInterval = TimeSpan.FromSeconds(15);

        private CancellationTokenSource? _cts;
        private DateTime _lastUploadUtc = DateTime.MinValue;
        private readonly object _lock = new();

        public bool Enabled { get; set; } = false;

        // (state, shortMsg, detail)
        public event Action<WebDavSyncState, string, string>? StatusChanged;

        public WebDavSyncService(
            string username,
            string password,
            Func<string> getLocalFilePath,
            Func<string> getRemoteFileUrl)
        {
            _getLocalFilePath = getLocalFilePath;
            _getRemoteFileUrl = getRemoteFileUrl;

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler);

            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        private void Emit(WebDavSyncState state, string msg, string detail)
        {
            StatusChanged?.Invoke(state, msg, detail);
        }

        public void NotifyLocalChanged()
        {
            if (!Enabled) return;

            lock (_lock)
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                Emit(WebDavSyncState.Queued, "排队中",
                    $"[{DateTime.Now:HH:mm:ss}] 排队中（防抖等待）…");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(_debounce, ct);

                        var now = DateTime.UtcNow;
                        var wait = _lastUploadUtc + _minUploadInterval - now;
                        if (wait > TimeSpan.Zero)
                            await Task.Delay(wait, ct);

                        await UploadAsync(ct);
                        _lastUploadUtc = DateTime.UtcNow;

                        Emit(WebDavSyncState.Uploaded, "上传成功",
                            $"[{DateTime.Now:HH:mm:ss}] 刚上传成功");
                    }
                    catch (OperationCanceledException)
                    {
                        // 被新的保存动作打断，不算失败
                    }
                    catch (Exception ex)
                    {
                        Emit(WebDavSyncState.Failed, "上传失败",
                            $"[{DateTime.Now:HH:mm:ss}] 上传失败（点击查看原因）\n原因：{ex.Message}");
                    }
                }, ct);
            }
        }

        public async Task<bool> TestAsync(CancellationToken ct = default)
        {
            if (!Enabled)
            {
                Emit(WebDavSyncState.Disabled, "未启用",
                    $"[{DateTime.Now:HH:mm:ss}] 未启用 WebDAV");
                return false;
            }

            try
            {
                var url = _getRemoteFileUrl();

                using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
                req.Headers.Add("Depth", "0");

                using var resp = await _http.SendAsync(req, ct);

                bool ok = resp.StatusCode == HttpStatusCode.MultiStatus
                          || resp.IsSuccessStatusCode
                          || resp.StatusCode == HttpStatusCode.NotFound;

                if (ok)
                {
                    Emit(WebDavSyncState.Connected, "已连接",
                        $"[{DateTime.Now:HH:mm:ss}] 已连接");
                    return true;
                }

                Emit(WebDavSyncState.Failed, "连接失败",
                    $"[{DateTime.Now:HH:mm:ss}] 连接失败（点击查看原因）\n原因：HTTP {(int)resp.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Emit(WebDavSyncState.Failed, "连接失败",
                    $"[{DateTime.Now:HH:mm:ss}] 连接失败（点击查看原因）\n原因：{ex.Message}");
                return false;
            }
        }

        public async Task UploadAsync(CancellationToken ct = default)
        {
            var local = _getLocalFilePath();
            if (!File.Exists(local)) return;

            byte[] bytes = await File.ReadAllBytesAsync(local, ct);

            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var url = _getRemoteFileUrl();
            using var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            _http.Dispose();
        }
    }
}
