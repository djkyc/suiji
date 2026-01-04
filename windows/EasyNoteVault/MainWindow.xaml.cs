using System.Collections.ObjectModel;
using System.Linq;
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

            // 左键复制
            VaultGrid.PreviewMouseLeftButtonUp += VaultGrid_PreviewMouseLeftButtonUp;

            // 编辑完成检测重复
            VaultGrid.CellEditEnding += VaultGrid_CellEditEnding;
        }

        // =============================
        // 左键单击复制 + 提示
        // =============================
        private void VaultGrid_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            TextBlock tb = e.OriginalSource as TextBlock;
            if (tb == null)
                return;

            if (string.IsNullOrWhiteSpace(tb.Text))
                return;

            Clipboard.SetText(tb.Text);

            MessageBox.Show(
                "已复制",
                "EasyNoteVault",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        // =============================
        // 右键菜单：粘贴
        // =============================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText())
                return;

            if (VaultGrid.CurrentCell.Item == null)
                return;

            string text = Clipboard.GetText();

            VaultItem item = VaultGrid.CurrentCell.Item as VaultItem;
            if (item == null)
                return;

            string column = VaultGrid.CurrentCell.Column.Header.ToString();

            if (column == "名称")
                item.Name = text;
            else if (column == "网站")
                item.Url = text;
            else if (column == "账号")
                item.Account = text;
            else if (column == "密码")
                item.Password = text;
            else if (column == "备注")
                item.Remark = text;

            VaultGrid.Items.Refresh();
        }

        // =============================
        // 编辑完成：网址重复检测
        // =============================
        private void VaultGrid_CellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString() != "网站")
                return;

            VaultItem current = e.Row.Item as VaultItem;
            if (current == null)
                return;

            string currentUrl = NormalizeUrl(current.Url);
            if (string.IsNullOrEmpty(currentUrl))
                return;

            var duplicates = Items
                .Select((item, index) => new { item, index })
                .Where(x =>
                    x.item != current &&
                    NormalizeUrl(x.item.Url) == currentUrl)
                .ToList();

            if (duplicates.Count > 0)
            {
                string msg = string.Join(
                    "\n",
                    duplicates.Select(d =>
                        "第 " + (d.index + 1) + " 行（账号：" + d.item.Account + "）")
                );

                MessageBox.Show(
                    "网址重复：\n" + current.Url + "\n\n已存在于：\n" + msg,
                    "网址重复提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        // =============================
        // 网址标准化
        // =============================
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

    // =============================
    // 数据模型
    // =============================
    public class VaultItem
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string Remark { get; set; }
    }
}
