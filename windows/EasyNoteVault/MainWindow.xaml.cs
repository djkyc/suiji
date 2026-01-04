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

namespace EasyNoteVault
{
    public partial class MainWindow : Window
    {
        // 全量数据
        private ObservableCollection<VaultItem> AllItems =
            new ObservableCollection<VaultItem>();

        // 当前显示数据（搜索过滤）
        private ObservableCollection<VaultItem> ViewItems =
            new ObservableCollection<VaultItem>();

        public MainWindow()
        {
            InitializeComponent();

            VaultGrid.ItemsSource = ViewItems;

            Loaded += (_, _) => LoadData();
            Closing += (_, _) => SaveData();
        }

        // ================= 加载 / 保存 =================
        private void LoadData()
        {
            try
            {
                AllItems.Clear();
                ViewItems.Clear();

                foreach (var v in DataStore.Load())
                {
                    AllItems.Add(v);
                    ViewItems.Add(v);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("数据加载失败：\n" + ex.Message);
            }
        }

        private void SaveData()
        {
            try
            {
                DataStore.Save(AllItems);
            }
            catch (Exception ex)
            {
                MessageBox.Show("数据保存失败：\n" + ex.Message);
            }
        }

        // ================= 新增一行 =================
        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            var item = new VaultItem();
            AllItems.Add(item);
            ViewItems.Add(item);

            VaultGrid.SelectedItem = item;
            VaultGrid.ScrollIntoView(item);
        }

        // ================= 搜索过滤 =================
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string key = SearchBox.Text.Trim().ToLower();
            ViewItems.Clear();

            foreach (var v in AllItems)
            {
                if (string.IsNullOrEmpty(key) ||
                    v.Name.ToLower().Contains(key) ||
                    v.Url.ToLower().Contains(key) ||
                    v.Account.ToLower().Contains(key) ||
                    v.Remark.ToLower().Contains(key))
                {
                    ViewItems.Add(v);
                }
            }
        }

        // ================= 左键单击复制（恢复） =================
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

        // ================= 右键粘贴（保持） =================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText()) return;
            if (VaultGrid.CurrentCell.Item == null ||
                VaultGrid.CurrentCell.Column == null) return;

            VaultGrid.BeginEdit();

            var item = VaultGrid.CurrentCell.Item as VaultItem;
            if (item == null) return;

            string text = Clipboard.GetText();
            string col = VaultGrid.CurrentCell.Column.Header.ToString();

            if (col == "名称") item.Name = text;
            else if (col == "网站") item.Url = text;
            else if (col == "账号") item.Account = text;
            else if (col == "密码") item.Password = text;
            else if (col == "备注") item.Remark = text;

            VaultGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            VaultGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        // ================= 🔥 重复网址提示（恢复） =================
        private void VaultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString() != "网站")
                return;

            var current = e.Row.Item as VaultItem;
            if (current == null)
                return;

            string currentUrl = NormalizeUrl(current.Url);
            if (string.IsNullOrEmpty(currentUrl))
                return;

            var duplicates = AllItems
                .Where(x => x != current &&
                            NormalizeUrl(x.Url) == currentUrl)
                .ToList();

            if (duplicates.Count > 0)
            {
                MessageBox.Show(
                    $"网站重复：\n{current.Url}\n\n已存在 {duplicates.Count} 条记录",
                    "重复提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // ================= 导出 =================
        private void Export_Click(object sender, RoutedEventArgs e)
        {
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
            {
                sb.AppendLine(
                    $"{v.Name}  {v.Url}  {v.Account}  {v.Password}  {v.Remark}");
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
        }

        // ================= 导入 =================
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
        }

        private void ImportTxt(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);

            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;

                var v = new VaultItem
                {
                    Name = parts[0],
                    Url = parts[1],
                    Account = parts[2],
                    Password = parts[3],
                    Remark = parts[4]
                };

                AllItems.Add(v);
                ViewItems.Add(v);
            }
        }

        private void ImportJson(string path)
        {
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var list = JsonSerializer.Deserialize<VaultItem[]>(json);
                if (list == null) return;

                foreach (var v in list)
                {
                    AllItems.Add(v);
                    ViewItems.Add(v);
                }
            }
            catch
            {
                MessageBox.Show("JSON 文件格式不正确");
            }
        }

        // ================= 工具 =================
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
