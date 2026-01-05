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
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EasyNoteVault
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<VaultItem> AllItems = new ObservableCollection<VaultItem>();
        private ObservableCollection<VaultItem> ViewItems = new ObservableCollection<VaultItem>();

        private WebDavSettings _webdavSettings = new WebDavSettings();
        private WebDavSyncService _webdav = null;
        private string _webdavLastDetail = "未启用 WebDAV";

        private DispatcherTimer _toastTimer = null;

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

            _toastTimer = new DispatcherTimer();
            _toastTimer.Interval = TimeSpan.FromMilliseconds(900);
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                FadeOutToast();
            };
        }

        // ================= ✅ Toast：不打断提示 =================
        private void ShowToast(string message)
        {
            try
            {
                ToastText.Text = message;

                ToastBorder.Visibility = Visibility.Visible;
                ToastBorder.Opacity = 1;

                _toastTimer.Stop();
                _toastTimer.Start();
            }
            catch { }
        }

        private void FadeOutToast()
        {
            try
            {
                var anim = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(280),
                    FillBehavior = FillBehavior.Stop
                };

                anim.Completed += (_, _) =>
                {
                    ToastBorder.Opacity = 0;
                    ToastBorder.Visibility = Visibility.Collapsed;
                };

                ToastBorder.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            catch
            {
                ToastBorder.Opacity = 0;
                ToastBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void ForceCommitGridEdits()
        {
            try
            {
                VaultGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                VaultGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }
        }

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

            if (_webdav != null) _webdav.NotifyLocalChanged();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshView();
        }

        // ================= ✅ 单击：进入编辑（可直接输入） =================
        private void VaultGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount != 1) return;

                var dep = e.OriginalSource as DependencyObject;
                if (dep == null) return;

                var cell = FindVisualParent<DataGridCell>(dep);
                if (cell == null) return;

                if (cell.Column == null || cell.IsReadOnly) return;

                if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox)
                    return;

                var rowItem = cell.DataContext;
                if (rowItem == null) return;

                VaultGrid.CurrentCell = new DataGridCellInfo(rowItem, cell.Column);
                VaultGrid.SelectedCells.Clear();
                VaultGrid.SelectedCells.Add(VaultGrid.CurrentCell);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    VaultGrid.BeginEdit();
                }), DispatcherPriority.Input);
            }
            catch { }
        }

        // ================= ✅ 双击：复制单元格内容（Toast 不打断） =================
        private void VaultGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                string text = "";

                if (e.OriginalSource is TextBlock tb)
                    text = tb.Text;

                if (string.IsNullOrWhiteSpace(text) && e.OriginalSource is TextBox tbox)
                    text = tbox.Text;

                if (string.IsNullOrWhiteSpace(text))
                    return;

                Clipboard.SetText(text);
                ShowToast("已复制");

                e.Handled = true;
            }
            catch { }
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

                if (cell == null || row == null) return;

                SetCurrentCellOnly(row.Item, cell.Column);
            }
            catch { }
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
                string clip = Clipboard.GetText();

                VaultItem item;
                if (VaultGrid.CurrentCell.Item is VaultItem vi)
                {
                    item = vi;
                }
                else
                {
                    item = new VaultItem();
                    AllItems.Add(item);

                    if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                        SearchBox.Text = "";

                    RefreshView();
                    SetCurrentCellOnly(item, colObj);
                }

                if (col == "网站")
                {
                    if (!TrySetUrl(item, clip))
                        return;
                }
                else if (col == "名称") item.Name = clip;
                else if (col == "账号") item.Account = clip;
                else if (col == "密码") item.Password = clip;
                else if (col == "备注") item.Remark = clip;

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
                        WebDavStatusBtn.Background = Brushes.Gold;
                    else if (state == WebDavSyncState.Connected || state == WebDavSyncState.Uploaded)
                        WebDavStatusBtn.Background = Brushes.LimeGreen;
                    else if (state == WebDavSyncState.Failed)
                        WebDavStatusBtn.Background = Brushes.IndianRed;
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

    public static class DataStore
    {
        public static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.enc");

        public static VaultItem[] Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new VaultItem[0];

                var bytes = File.ReadAllBytes(FilePath);

                string json;
                try
                {
                    var raw = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                    json = Encoding.UTF8.GetString(raw);
                }
                catch
                {
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

                var enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, enc);
            }
            catch
            {
                // 不弹窗
            }
        }
    }
}
