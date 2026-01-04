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
using System.Windows.Threading;

namespace EasyNoteVault
{
    public partial class MainWindow : Window
    {
        // 真正的数据源
        private ObservableCollection<VaultItem> AllItems =
            new ObservableCollection<VaultItem>();

        // 当前显示数据
        private ObservableCollection<VaultItem> ViewItems =
            new ObservableCollection<VaultItem>();

        public MainWindow()
        {
            InitializeComponent();

            VaultGrid.ItemsSource = ViewItems;

            Loaded += (_, _) => LoadData();

            // ✅ 关闭时：强制提交正在编辑的单元格，再保存
            Closing += (_, _) =>
            {
                ForceCommitGridEdits();
                SaveData();
            };

            // 明确注册，防止再丢
            VaultGrid.PreviewMouseLeftButtonUp += VaultGrid_PreviewMouseLeftButtonUp;
            VaultGrid.CellEditEnding += VaultGrid_CellEditEnding;

            // ✅ 任何单元格切换都后台提交保存（防止编辑没提交就退出/导出）
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
            catch
            {
                // 忽略：关闭时或特殊状态可能抛异常
            }
        }

        // ================= 定位到指定行 + 指定列（网站列） =================
        private DataGridColumn? GetColumnByHeader(string header)
        {
            return VaultGrid.Columns.FirstOrDefault(c =>
                string.Equals(c.Header?.ToString(), header, StringComparison.Ordinal));
        }

        private void LocateItemAndFocusCell(VaultItem item, string columnHeader)
        {
            // 如果当前搜索过滤导致 item 不在 ViewItems，则清空搜索让它出现
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
            {
                AllItems.Add(v);
            }

            RefreshView();
        }

        private void SaveData()
        {
            ForceCommitGridEdits();
            DataStore.Save(AllItems);
        }

        // ================= 新增一行 =================
        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            var item = new VaultItem();
            AllItems.Add(item);

            // 如果当前在搜索过滤中，新增项可能看不到；这里清空搜索确保能看到新增行
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                SearchBox.Text = "";

            RefreshView();
            SaveData();

            VaultGrid.SelectedItem = item;
            VaultGrid.ScrollIntoView(item);

            // 可选：定位到“名称”列开始输入
            var nameCol = GetColumnByHeader("名称");
            if (nameCol != null)
            {
                VaultGrid.CurrentCell = new DataGridCellInfo(item, nameCol);
                VaultGrid.Focus();
            }
        }

        // ================= 搜索 =================
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshView();
        }

        // ================= 左键复制 =================
        private void VaultGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBlock tb &&
                !string.IsNullOrWhiteSpace(tb.Text))
            {
                Clipboard.SetText(tb.Text);
                MessageBox.Show("已复制",
                    "EasyNoteVault",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // ================= ✅ 右键粘贴（重复网址：提示+拒绝+定位到已有项） =================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText())
                return;

            VaultGrid.Focus();
            ForceCommitGridEdits();

            if (VaultGrid.CurrentCell.Item is not VaultItem item)
                return;

            string col = VaultGrid.CurrentCell.Column?.Header?.ToString() ?? "";
            string text = Clipboard.GetText();

            if (col == "网站")
            {
                // ✅ 重复：TrySetUrl 内部会提示并定位到已有项
                // ✅ 返回 false，直接拒绝粘贴（不写入、不保存）
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

        // ================= ✅ 单元格变化后台提交保存 =================
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

                // ✅ 重复：提示 + 定位到已有项 + 取消编辑（保持原值）
                if (!TrySetUrl(item, tb.Text))
                {
                    e.Cancel = true;
                    return;
                }
            }

            // ✅ 任何列：编辑结束后台提交+保存
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ForceCommitGridEdits();
                RefreshView();
                SaveData();
            }), DispatcherPriority.Background);
        }

        // ================= 🔥 导入（XAML 需要） =================
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

        // ================= 🔥 导出（XAML 需要） =================
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

        // ================= 统一网址校验：重复 -> 提示 + 定位 + 拒绝 =================
        private bool TrySetUrl(VaultItem current, string newUrl)
        {
            string norm = NormalizeUrl(newUrl);

            // 允许空（不做重复判断）
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

                // ✅ 定位到已存在那条，聚焦“网站”列
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
