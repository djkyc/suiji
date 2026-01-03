using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
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

            // 窗口真正显示后再加载（避免启动阶段炸）
            Loaded += MainWindow_Loaded;

            // 左键复制
            VaultGrid.PreviewMouseLeftButtonUp += VaultGrid_PreviewMouseLeftButtonUp;

            // 编辑完成检测重复 + 自动保存
            VaultGrid.CellEditEnding += VaultGrid_CellEditEnding;
        }

        // ===============================
        // 启动后自动加载（解密 data.enc）
        // ===============================
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var loaded = AutoStore.Load();
            Items.Clear();

            foreach (var item in loaded)
                Items.Add(item);
        }

        // ===============================
        // ＋ 新增一行
        // ===============================
        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            var item = new VaultItem();
            Items.Add(item);

            VaultGrid.SelectedItem = item;
            VaultGrid.ScrollIntoView(item);

            AutoStore.Save(Items);
        }

        // ===============================
        // 左键单击复制 + 提示
        // ===============================
        private void VaultGrid_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBlock tb &&
                !string.IsNullOrWhiteSpace(tb.Text))
            {
                Clipboard.SetText(tb.Text);

                MessageBox.Show(
                    "已复制",
                    "EasyNoteVault",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // ===============================
        // 右键菜单：粘贴（真正写入并保存）
        // ===============================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText())
                return;

            if (VaultGrid.CurrentCell.Item == null ||
                VaultGrid.CurrentCell.Column == null)
                return;

            string text = Clipboard.GetText();

            VaultGrid.BeginEdit();

            var item = VaultGrid.CurrentCell.Item as VaultItem;
            if (item == null)
                return;

            string column = VaultGrid.CurrentCell.Column.Header.ToString();

            if (column == "名称") item.Name = text;
            else if (column == "网站") item.Url = text;
            else if (column == "账号") item.Account = text;
            else if (column == "密码") item.Password = text;
            else if (column == "备注") item.Remark = text;

            VaultGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            VaultGrid.CommitEdit(DataGridEditingUnit.Row, true);

            AutoStore.Save(Items);
        }

        // ===============================
        // 编辑完成：网址重复检测 + 自动保存
        // ===============================
        private void VaultGrid_CellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString() == "网站")
            {
                var current = e.Row.Item as VaultItem;
                if (current != null)
                {
                    string url = NormalizeUrl(current.Url);
                    if (!string.IsNullOrEmpty(url))
                    {
                        var dup = Items
                            .Select((x, i) => new { x, i })
                            .Where(x =>
                                x.x != current &&
                                NormalizeUrl(x.x.Url) == url)
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
                }
            }

            // 延迟保存，确保值已提交
            Dispatcher.InvokeAsync(() => AutoStore.Save(Items));
        }

        // ===============================
        // 导出（明文双空格 txt）
        // ===============================
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            string fileName = DateTime.Now.ToString("yyyyMMddHHmm") + ".txt";

            SaveFileDialog dlg = new SaveFileDialog
            {
                FileName = fileName,
                Filter = "文本文件 (*.txt)|*.txt"
            };

            if (dlg.ShowDialog() != true)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("名称  网站  账号  密码  备注");

            foreach (var item in Items)
            {
                sb.AppendLine(
                    $"{item.Name}  {item.Url}  {item.Account}  {item.Password}  {item.Remark}");
            }

            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
        }

        // ===============================
        // 导入（明文双空格 txt）
        // ===============================
        private void Import_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt"
            };

            if (dlg.ShowDialog() != true)
                return;

            var lines = System.IO.File.ReadAllLines(dlg.FileName, Encoding.UTF8);

            foreach (var line in lines.Skip(1)) // 跳过表头
            {
                var parts = line
                    .Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 5)
                    continue;

                Items.Add(new VaultItem
                {
                    Name = parts[0],
                    Url = parts[1],
                    Account = parts[2],
                    Password = parts[3],
                    Remark = parts[4]
                });
            }

            AutoStore.Save(Items);
        }

        // ===============================
        // 网址标准化
        // ===============================
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

    // ===============================
    // 数据模型
    // ===============================
    public class VaultItem
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string Remark { get; set; }
    }
}
