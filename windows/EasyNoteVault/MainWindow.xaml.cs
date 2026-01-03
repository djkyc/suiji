using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EasyNoteVault
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<VaultItem> Items { get; } =
            new ObservableCollection<VaultItem>();

        public MainWindow()
        {
            InitializeComponent();
            VaultGrid.ItemsSource = Items;

            // 示例数据
            Items.Add(new VaultItem
            {
                Name = "示例",
                Url = "https://example.com",
                Account = "test@example.com",
                Password = "123456",
                Remark = "这是示例数据"
            });

            VaultGrid.PreviewMouseLeftButtonUp += VaultGrid_PreviewMouseLeftButtonUp;
            VaultGrid.CellEditEnding += VaultGrid_CellEditEnding;
        }

        // ================= 新增行 =================
        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            var item = new VaultItem();
            Items.Add(item);
            VaultGrid.SelectedItem = item;
            VaultGrid.ScrollIntoView(item);
        }

        // ================= 单击复制 =================
        private void VaultGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                Clipboard.SetText(tb.Text);
                MessageBox.Show("已复制", "EasyNoteVault",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ================= 右键粘贴 =================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText()) return;
            if (VaultGrid.CurrentCell.Item == null ||
                VaultGrid.CurrentCell.Column == null) return;

            string text = Clipboard.GetText();
            VaultGrid.BeginEdit();

            var item = VaultGrid.CurrentCell.Item as VaultItem;
            if (item == null) return;

            string col = VaultGrid.CurrentCell.Column.Header.ToString();
            if (col == "名称") item.Name = text;
            else if (col == "网站") item.Url = text;
            else if (col == "账号") item.Account = text;
            else if (col == "密码") item.Password = text;
            else if (col == "备注") item.Remark = text;

            VaultGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            VaultGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        // ================= 重复检测 =================
        private void VaultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString() != "网站") return;

            var current = e.Row.Item as VaultItem;
            if (current == null) return;

            string url = NormalizeUrl(current.Url);
            if (string.IsNullOrEmpty(url)) return;

            var dup = Items
                .Select((x, i) => new { x, i })
                .Where(x => x.x != current && NormalizeUrl(x.x.Url) == url)
                .ToList();

            if (dup.Count > 0)
            {
                MessageBox.Show(
                    $"网址重复：{current.Url}\n已存在于第 {dup[0].i + 1} 行",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // ================= 导出（双空格分隔） =================
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            string fileName = DateTime.Now.ToString("yyyyMMddHHmm") + ".txt";

            SaveFileDialog dlg = new SaveFileDialog
            {
                FileName = fileName,
                Filter = "文本文件 (*.txt)|*.txt"
            };

            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();

            // 表头（双空格）
            sb.AppendLine("名称  网站  账号  密码  备注");

            foreach (var item in Items)
            {
                sb.AppendLine(
                    $"{item.Name}  {item.Url}  {item.Account}  {item.Password}  {item.Remark}");
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
        }

        // ================= 导入（双空格解析） =================
        private void Import_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt"
            };

            if (dlg.ShowDialog() != true) return;

            var lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);

            foreach (var line in lines.Skip(1)) // 跳过表头
            {
                // 用「两个及以上空格」切分
                var parts = line
                    .Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 5) continue;

                Items.Add(new VaultItem
                {
                    Name = parts[0],
                    Url = parts[1],
                    Account = parts[2],
                    Password = parts[3],
                    Remark = parts[4]
                });
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
        public string Name { get; set; }
        public string Url { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string Remark { get; set; }
    }
}
