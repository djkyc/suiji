#nullable enable
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        private ObservableCollection<VaultItem> AllItems = new ObservableCollection<VaultItem>();
        private ObservableCollection<VaultItem> ViewItems = new ObservableCollection<VaultItem>();

        public MainWindow()
        {
            InitializeComponent();

            VaultGrid.ItemsSource = ViewItems;

            Loaded += (_, _) => LoadData();
            Closing += (_, _) => { ForceCommitGridEdits(); SaveData(); };

            VaultGrid.PreviewMouseLeftButtonUp += VaultGrid_PreviewMouseLeftButtonUp;
            VaultGrid.CellEditEnding += VaultGrid_CellEditEnding;
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

        // ================= ✅ 右键菜单打开前：安全定位到你点的单元格（不会崩） =================
        private void VaultGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                var dep = e.OriginalSource as DependencyObject;
                if (dep == null) return;

                var cell = FindVisualParent<DataGridCell>(dep);
                var row = FindVisualParent<DataGridRow>(dep);

                // 点在表头/空白/滚动条：cell 或 row 可能为 null，直接放过，不做事
                if (cell == null || row == null) return;

                VaultGrid.SelectedItem = row.Item;
                VaultGrid.CurrentCell = new DataGridCellInfo(row.Item, cell.Column);
                VaultGrid.Focus();
            }
            catch
            {
                // ✅ 关键：任何异常都吞掉，避免右键直接把程序干掉
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject? current = child;
            while (current != null)
            {
                if (current is T typed) return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // ================= ✅ 右键粘贴 =================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsText()) return;

                VaultGrid.Focus();
                ForceCommitGridEdits();

                var colObj = VaultGrid.CurrentCell.Column;
                if (colObj == null) return;

                string col = colObj.Header?.ToString() ?? "";
                string text = Clipboard.GetText();

                // 当前行对象：可能是 VaultItem，也可能是 NewItemPlaceholder（新增占位）
                VaultItem item;
                if (VaultGrid.CurrentCell.Item is VaultItem vi)
                {
                    item = vi;
                }
                else
                {
                    // ✅ 点在空表/占位行：自动新建一条再粘贴
                    item = new VaultItem();
                    AllItems.Add(item);

                    if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                        SearchBox.Text = "";

                    RefreshView();
                    VaultGrid.SelectedItem = item;
                    VaultGrid.ScrollIntoView(item);
                    VaultGrid.CurrentCell = new DataGridCellInfo(item, colObj);
                }

                if (col == "网站")
                {
                    if (!TrySetUrl(item, text))
                        return; // 重复：提示+定位已在 TrySetUrl 做了
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
                // ✅ 给你一个明确错误，不再“直接退出没提示”
                MessageBox.Show($"粘贴出错：\n{ex.Message}", "EasyNoteVault",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= 编辑结束：网址重复校验 + 保存 =================
        private void VaultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row.Item is not VaultItem item) return;

            string col = e.Column.Header?.ToString() ?? "";
            if (col == "网站")
            {
                var tb = e.EditingElement as TextBox;
                if (tb == null) return;

                if (!TrySetUrl(item, tb.Text))
                {
                    e.Cancel = true;
                    return;
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
        private DataGridColumn? GetColumnByHeader(string header)
        {
            return VaultGrid.Columns.FirstOrDefault(c =>
                string.Equals(c.Header?.ToString(), header, StringComparison.Ordinal));
        }

        private void LocateItemAndFocusCell(VaultItem item, string columnHeader)
        {
            if (!ViewItems.Contains(item))
            {
                SearchBox.Text = "";
                RefreshView();
            }

            VaultGrid.SelectedItem = item;
            VaultGrid.ScrollIntoView(item);

            var col = GetColumnByHeader(columnHeader);
            if (col != null)
            {
                VaultGrid.CurrentCell = new DataGridCellInfo(item, col);
                VaultGrid.Focus();
            }
        }

        private bool TrySetUrl(VaultItem current, string newUrl)
        {
            string norm = NormalizeUrl(newUrl);
            if (string.IsNullOrEmpty(norm))
            {
                current.Url = newUrl ?? "";
                return true;
            }

            var dup = AllItems.FirstOrDefault(x => x != current && NormalizeUrl(x.Url) == norm);
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

        // ================= 导入 / 导出 =================
        private void Import_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|JSON 文件 (*.json)|*.json"
            };

            if (dlg.ShowDialog() != true) return;

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

            if (dlg.ShowDialog() != true) return;

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
                if (parts.Length < 5) continue;

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
            if (list == null) return;

            foreach (var item in list)
            {
                if (TrySetUrl(item, item.Url))
                    AllItems.Add(item);
            }
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

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            url = url.Trim().ToLower();
            if (url.EndsWith("/")) url = url.TrimEnd('/');
            return url;
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
}
