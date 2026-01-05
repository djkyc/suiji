using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyNoteVault
{
    public partial class MainWindow : Window
    {
        // 真正的数据源
        private ObservableCollection<VaultItem> AllItems = new ObservableCollection<VaultItem>();

        // 当前显示数据
        private ObservableCollection<VaultItem> ViewItems = new ObservableCollection<VaultItem>();

        // WebDAV
        private WebDavSettings _webdavSettings = new WebDavSettings();
        private WebDavSyncService _webdav = null;   // 允许为 null（不使用 ?，避免 CS8632）
        private string _webdavLastDetail = "未启用 WebDAV";

        public MainWindow()
        {
            InitializeComponent();

            VaultGrid.ItemsSource = ViewItems;

            Loaded += (_, _) =>
            {
                LoadData();
                LoadWebDavSettingsAndSetup();
            };

            Closing += (_, _) =>
            {
                ForceCommitGridEdits();
                SaveData();
                try { if (_webdav != null) _webdav.Dispose(); } catch { }
            };
        }

        // ================= 工具：强制提交 DataGrid 编辑 =================
        private void ForceCommitGridEdits()
        {
            try
            {
                VaultGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                VaultGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }
        }

        // ================= 加载 / 保存 =================
        private void LoadData()
        {
            AllItems.Clear();
            ViewItems.Clear();

            foreach (var v in DataStore.Load())
                AllItems.Add(v);

            RefreshView();
        }

        private void SaveData()
        {
            ForceCommitGridEdits();
            DataStore.Save(AllItems);

            // 保存后触发 WebDAV 自动上传（黄->绿/红）
            if (_webdav != null) _webdav.NotifyLocalChanged();
        }

        // ================= 搜索 =================
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshView();
        }

        // ================= 左键复制 =================
        private void VaultGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                Clipboard.SetText(tb.Text);
                MessageBox.Show("已复制", "EasyNoteVault",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ================= ✅ 右键菜单打开前：只选中单元格（避免 SelectionUnit=Cell 报错/闪退） =================
        private void VaultGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                var dep = e.OriginalSource as DependencyObject;
                if (dep == null) return;

                var cell = FindVisualParent<DataGridCell>(dep);
                var row = FindVisualParent<DataGridRow>(dep);

                // 点表头/空白/滚动条：可能为空，直接放过
                if (cell == null || row == null) return;

                SetCurrentCellOnly(row.Item, cell.Column);
            }
            catch
            {
                // 永不崩
            }
        }

        private void SetCurrentCellOnly(object rowItem, DataGridColumn column)
        {
            VaultGrid.CurrentCell = new DataGridCellInfo(rowItem, column);
            VaultGrid.SelectedCells.Clear();
            VaultGrid.SelectedCells.Add(VaultGrid.CurrentCell);
            VaultGrid.ScrollIntoView(rowItem, column);
            VaultGrid.Focus();
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject current = child;
            while (current != null)
            {
                if (current is T typed) return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // ================= 右键粘贴 =================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText())
                return;

            try
            {
                VaultGrid.Focus();
                ForceCommitGridEdits();

                var colObj = VaultGrid.CurrentCell.Column;
                if (colObj == null) return;

                string col = colObj.Header == null ? "" : colObj.Header.ToString();
                string text = Clipboard.GetText();

                // 当前行对象：VaultItem 或 新增占位符
                VaultItem item;
                if (VaultGrid.CurrentCell.Item is VaultItem vi)
                {
                    item = vi;
                }
                else
                {
                    // 空表/占位行：自动新建一条
                    item = new VaultItem();
                    AllItems.Add(item);

                    // 搜索中粘贴：清空搜索保证新行可见
                    if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                        SearchBox.Text = "";

                    RefreshView();
                    SetCurrentCellOnly(item, colObj);
                }

                if (col == "网站")
                {
                    if (!TrySetUrl(item, text))
                        return;
                }
                else if (col == "名称") item.Name = text;
                else if (col == "账号") item.Account = text;
                else if (col == "密码") item.Password = text;
                else if (col == "备注") item.Remark = text;

                ForceCommitGridEdits();
                RefreshView();
                SaveData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"粘贴出错：\n{ex.Message}",
                    "EasyNoteVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= 编辑结束：网站列重复校验 + 保存 =================
        private void VaultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (!(e.Row.Item is VaultItem item))
                return;

            string col = e.Column.Header == null ? "" : e.Column.Header.ToString();

            if (col == "网站")
            {
                if (e.EditingElement is TextBox tb)
                {
                    if (!TrySetUrl(item, tb.Text))
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ForceCommitGridEdits();
                RefreshView();
                SaveData();
            }), DispatcherPriority.Background);
        }

        // ================= 网址去重：重复 -> 提示 + 定位 + 拒绝 =================
        private DataGridColumn GetColumnByHeader(string header)
        {
            return VaultGrid.Columns.FirstOrDefault(c =>
                string.Equals(c.Header == null ? "" : c.Header.ToString(), header, StringComparison.Ordinal));
        }

        private void LocateItemAndFocusCell(VaultItem item, string columnHeader)
        {
            if (!ViewItems.Contains(item))
            {
                SearchBox.Text = "";
                RefreshView();
            }

            var col = GetColumnByHeader(columnHeader);
            if (col == null) return;

            SetCurrentCellOnly(item, col);
        }

        private bool TrySetUrl(VaultItem current, string newUrl)
        {
            string norm = NormalizeUrl(newUrl);
            if (string.IsNullOrEmpty(norm))
            {
                current.Url = newUrl ?? "";
                return true;
            }

            var dup = AllItems.FirstOrDefault(x =>
                x != current && NormalizeUrl(x.Url) == norm);

            if (dup != null)
            {
                MessageBox.Show($"该网站已存在，不能重复添加：\n{dup.Url}",
                    "重复网址", MessageBoxButton.OK, MessageBoxImage.Warning);

                LocateItemAndFocusCell(dup, "网站");
                return false;
            }

            current.Url = newUrl ?? "";
            return true;
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            url = url.Trim().ToLower();
            if (url.EndsWith("/"))
                url = url.TrimEnd('/');

            return url;
        }

        // ================= 刷新视图 =================
        private void RefreshView()
        {
            string key = (SearchBox.Text ?? "").Trim().ToLower();
            ViewItems.Clear();

            foreach (var v in AllItems)
            {
                if (string.IsNullOrEmpty(key) ||
                    (v.Name ?? "").ToLower().Contains(key) ||
                    (v.Url ?? "").ToLower().Contains(key) ||
                    (v.Account ?? "").ToLower().Contains(key) ||
                    (v.Remark ?? "").ToLower().Contains(key))
                {
                    ViewItems.Add(v);
                }
            }
        }

        // ================= 导入 =================
        private void Import_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|JSON 文件 (*.json)|*.json"
            };

            if (dlg.ShowDialog() != true)
                return;

            string ext = Path.GetExtension(dlg.FileName).ToLower();
            if (ext == ".txt") ImportTxt(dlg.FileName);
            else if (ext == ".json") ImportJson(dlg.FileName);

            RefreshView();
            SaveData();
        }

        // ================= 导出 =================
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            ForceCommitGridEdits();

            string fileName = DateTime.Now.ToString("yyyyMMddHH") + ".txt";
            SaveFileDialog dlg = new SaveFileDialog
            {
                FileName = fileName,
                Filter = "文本文件 (*.txt)|*.txt"
            };

            if (dlg.ShowDialog() != true)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("名称  网站  账号  密码  备注");

            foreach (var v in AllItems)
                sb.AppendLine($"{v.Name}  {v.Url}  {v.Account}  {v.Password}  {v.Remark}");

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
        }

        // ================= 导入实现 =================
        private void ImportTxt(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);

            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                    continue;

                var item = new VaultItem
                {
                    Name = parts[0],
                    Account = parts[2],
                    Password = parts[3],
                    Remark = parts[4]
                };

                if (TrySetUrl(item, parts[1]))
                    AllItems.Add(item);
            }
        }

        private void ImportJson(string path)
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var list = JsonSerializer.Deserialize<VaultItem[]>(json);
            if (list == null)
                return;

            foreach (var item in list)
            {
                if (TrySetUrl(item, item.Url))
                    AllItems.Add(item);
            }
        }

        // ================= WebDAV：设置 + 状态点 =================
        private void WebDav_Click(object sender, RoutedEventArgs e)
        {
            var win = new WebDavSettingsWindow(_webdavSettings) { Owner = this };
            if (win.ShowDialog() == true)
            {
                LoadWebDavSettingsAndSetup();
            }
        }

        private void WebDavStatus_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(_webdavLastDetail, "WebDAV 状态",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadWebDavSettingsAndSetup()
        {
            _webdavSettings = WebDavSettingsStore.Load();
            SetupWebDavService();
        }

        private void SetupWebDavService()
        {
            try { if (_webdav != null) _webdav.Dispose(); } catch { }
            _webdav = null;

            if (!_webdavSettings.Enabled)
            {
                SetDot(Brushes.Gray, "未启用 WebDAV");
                return;
            }

            var pass = WebDavSettingsStore.GetPassword(_webdavSettings);
            if (string.IsNullOrWhiteSpace(_webdavSettings.Username) || string.IsNullOrWhiteSpace(pass))
            {
                SetDot(Brushes.IndianRed, "WebDAV 未配置完整（账号/密码为空）");
                return;
            }

            // 本地文件路径：与 DataStore 一致（程序目录/data.enc）
            string localPath = DataStore.FilePath;
            string remoteUrl = WebDavUrlBuilder.BuildRemoteFileUrl(_webdavSettings);

            _webdav = new WebDavSyncService(
                _webdavSettings.Username,
                pass,
                () => localPath,
                () => remoteUrl);

            _webdav.Enabled = true;

            _webdav.StatusChanged += (state, msg, detail) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _webdavLastDetail = detail;
                    WebDavStatusBtn.ToolTip = detail;

                    if (state == WebDavSyncState.Queued)
                        WebDavStatusBtn.Background = Brushes.Gold;       // 黄
                    else if (state == WebDavSyncState.Connected || state == WebDavSyncState.Uploaded)
                        WebDavStatusBtn.Background = Brushes.LimeGreen;  // 绿
                    else if (state == WebDavSyncState.Failed)
                        WebDavStatusBtn.Background = Brushes.IndianRed;  // 红
                    else
                        WebDavStatusBtn.Background = Brushes.Gray;
                });
            };

            _ = _webdav.TestAsync();
        }

        private void SetDot(Brush brush, string detail)
        {
            _webdavLastDetail = $"[{DateTime.Now:HH:mm:ss}] {detail}";
            WebDavStatusBtn.Background = brush;
            WebDavStatusBtn.ToolTip = _webdavLastDetail;
        }
    }

    public class VaultItem
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Account { get; set; } = "";
        public string Password { get; set; } = "";
        public string Remark { get; set; } = "";
    }

    // ================= ✅ DataStore：补齐缺失（解决 CS0103），保存到 data.enc =================
    public static class DataStore
    {
        // 固定保存到程序目录（与 WebDAV 上传一致）
        public static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.enc");

        public static VaultItem[] Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new VaultItem[0];

                var bytes = File.ReadAllBytes(FilePath);

                // 1) 优先按 DPAPI 解密
                string json;
                try
                {
                    var raw = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                    json = Encoding.UTF8.GetString(raw);
                }
                catch
                {
                    // 2) 解密失败：兼容旧版本（可能是明文 JSON）
                    json = Encoding.UTF8.GetString(bytes);
                }

                var list = JsonSerializer.Deserialize<VaultItem[]>(json);
                return list ?? new VaultItem[0];
            }
            catch
            {
                return new VaultItem[0];
            }
        }

        public static void Save(ObservableCollection<VaultItem> items)
        {
            try
            {
                var json = JsonSerializer.Serialize(items.ToList());
                var raw = Encoding.UTF8.GetBytes(json);

                // DPAPI 加密写入
                var enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, enc);
            }
            catch
            {
                // 不弹窗，避免影响使用
            }
        }
    }
}
