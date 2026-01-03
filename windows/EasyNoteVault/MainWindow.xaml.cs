using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EasyNoteVault
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<VaultItem> Items { get; } = new();

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

            // 左键单击复制
            VaultGrid.PreviewMouseLeftButtonUp += VaultGrid_PreviewMouseLeftButtonUp;

            // 编辑完成检测重复
            VaultGrid.CellEditEnding += VaultGrid_CellEditEnding;
        }

        // ==================================================
        // 左键单击复制 + 提示
        // ==================================================
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

        // ==================================================
        // 右键菜单：粘贴
        // ==================================================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText())
                return;

            var text = Clipboard.GetText();

            // 如果没有选中单元格，直接返回
            if (VaultGrid.CurrentCell.Item == null)
                return;

            // 如果选中的是“新行”，先创建一行
            if (VaultGrid.CurrentCell.Item == CollectionView.NewItemPlaceholder)
            {
                var newItem = new VaultItem();
                Items.Add(newItem);
                VaultGrid.CurrentCell = new DataGridCellInfo(
                    newItem,
                    VaultGrid.CurrentCell.Column);
            }

            if (VaultGrid.CurrentCell.Item is VaultItem item)
            {
                var column = VaultGrid.CurrentCell.Column?.Header?.ToString();

                switch (column)
                {
                    case "名称":
                        item.Name = text;
                        break;
                    case "网站":
                        item.Url = text;
                        break;
                    case "账号":
                        item.Account = text;
                        break;
                    case "密码":
                        item.Password = text;
                        break;
                    case "备注":
                        item.Remark = text;
                        break;
                }

                VaultGrid.Items.Refresh();
            }
        }

        // ==================================================
        // 编辑完成：网址重复检测
        // ==================================================
        private void VaultGrid_CellEditEnding(
            object? sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header?.ToString() != "网站")
                return;

            if (e.Row.Item is not VaultItem current)
                return;

            var currentUrl = NormalizeUrl(current.Url);
            if (string.IsNullOrEmpty(currentUrl))
                return;

            var duplicates = Items
                .Select((item, index) => new { item, index })
                .Where(x =>
                    x.item != current &&
                    NormalizeUrl(x.item.Url) == currentUrl)
                .ToList();

            if (duplicates.Any())
            {
                var msg = string.Join("\n",
                    duplicates.Select(d =>
                        $"第 {d.index + 1} 行（账号：{d.item.Account}）"));

                MessageBox.Show(
                    $"网址重复：\n{current.Url}\n\n已存在于：\n{msg}",
                    "网址重复提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // ==================================================
        // 网址标准化
        // ==================================================
        private static string NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            url = url.Trim().ToLower();
            if (url.EndsWith("/"))
                url = url.TrimEnd('/');

            return url;
        }
    }

    // ==================================================
    // 数据模型
    // ==================================================
    public class VaultItem
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Account { get; set; } = "";
        public string Password { get; set; } = "";
        public string Remark { get; set; } = "";
    }
}
