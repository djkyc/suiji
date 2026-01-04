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

            Closing += (_, _) =>
            {
                ForceCommitGridEdits();
                SaveData();
            };

            VaultGrid.PreviewMouseLeftButtonUp += VaultGrid_PreviewMouseLeftButtonUp;

            // ✅ 右键点哪格就选中哪格（否则 CurrentCell 不对）
            VaultGrid.PreviewMouseRightButtonDown += VaultGrid_PreviewMouseRightButtonDown;

            VaultGrid.CellEditEnding += VaultGrid_CellEditEnding;
            VaultGrid.CurrentCellChanged += VaultGrid_CurrentCellChanged;
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

        // ================= 右键：选中你点的单元格 =================
        private void VaultGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            if (dep == null) return;

            var cell = FindVisualParent<DataGridCell>(dep);
            if (cell == null) return;

            var row = FindVisualParent<DataGridRow>(cell);
            if (row == null) return;

            VaultGrid.SelectedItem = row.Item;
            VaultGrid.CurrentCell = new DataGridCellInfo(row.Item, cell.Column);
            VaultGrid.Focus();
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject current = child;
            while (current != null)
            {
                if (current is T typed) return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // ================= 定位到指定行 + 指定列 =================
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

        // ================= ✅ 右键粘贴（没选中行时也能自动新建） =================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText()) return;

            VaultGrid.Focus();
            ForceCommitGridEdits();

            var colObj = VaultGrid.CurrentCell.Column;
            if (colObj == null) return;

            string col = colObj.Header?.ToString() ?? "";
            string text = Clipboard.GetText();

            VaultItem item;

            if (VaultGrid.CurrentCell.Item is VaultItem vi)
            {
                item = vi;
            }
            else
            {
                // ✅ 没有有效行（比如空表），自动创建一条
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

        // ================= 单元格变化后台提交保存 =================
        private void VaultGrid_CurrentCellChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ForceCommitGridEdits();
                SaveData();
            }), DispatcherPriority.Background);
        }

        // ================= 编辑结束：网站列重复校验 + 自动保存 =================
        private void VaultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row.Item is not VaultItem item)
                return;

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

        // ================= 🔥 导入 =================
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

            // ✅ 导入后：自动在末尾追加一个空白行（如果末尾已经是空行就不再加）
            EnsureTrailingBlankRowAfterImport();

            RefreshView();
            SaveData();

            // ✅ 导入后定位到末尾空行的“名称”列，方便继续填
            var last = AllItems.LastOrDefault();
            if (last != null)
                LocateItemAndFocusCell(last, "名称");
        }

        private void EnsureTrailingBlankRowAfterImport()
        {
            if (AllItems.Count == 0)
            {
                AllItems.Add(new VaultItem());
                return;
            }

            var last = AllItems.Last();
            if (!IsEmptyRow(last))
            {
                AllItems.Add(new VaultItem());
            }

            // 如果用户正在搜索，会看不到空行；导入后清空搜索更符合“继续录入”
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                SearchBox.Text = "";
        }

        private static bool IsEmptyRow(VaultItem v)
        {
            return string.IsNullOrWhiteSpace(v.Name)
                && string.IsNullOrWhiteSpace(v.Url)
                && string.IsNullOrWhiteSpace(v.Account)
                && string.IsNullOrWhiteSpace(v.Password)
                && string.IsNullOrWhiteSpace(v.Remark);
        }

        // ================= 🔥 导出 =================
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

            // ✅ 导出时跳过全空行（避免导入后那条空白行写进文件）
            foreach (var v in AllItems.Where(x => !IsEmptyRow(x)))
            {
                sb.AppendLine($"{v.Name}  {v.Url}  {v.Account}  {v.Password}  {v.Remark}");
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
        }

        // ================= 导入实现 =================
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

        // ================= 统一网址校验：重复 -> 提示 + 定位 + 拒绝 =================
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
                MessageBox.Show(
                    $"该网站已存在，不能重复添加：\n{dup.Url}",
                    "重复网址",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                LocateItemAndFocusCell(dup, "网站");
                return false;
            }

            current.Url = newUrl ?? "";
            return true;
        }

        // ================= 刷新视图 =================
        private void RefreshView()
        {
            string key = (SearchBox.Text ?? "").Trim().ToLower();
            ViewItems.Clear();

            foreach (var v in AllItems)
            {
                string name = v.Name ?? "";
                string url = v.Url ?? "";
                string acc = v.Account ?? "";
                string remark = v.Remark ?? "";

                if (string.IsNullOrEmpty(key) ||
                    name.ToLower().Contains(key) ||
                    url.ToLower().Contains(key) ||
                    acc.ToLower().Contains(key) ||
                    remark.ToLower().Contains(key))
                {
                    ViewItems.Add(v);
                }
            }
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
