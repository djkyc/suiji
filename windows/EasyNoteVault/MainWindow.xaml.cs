using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EasyNoteVault;

public partial class MainWindow : Window
{
    public ObservableCollection<VaultItem> Items { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        // 示例数据（可删）
        Items.Add(new VaultItem
        {
            Name = "百度",
            Url = "https://baidu.com",
            Account = "test01",
            Password = "123456",
            Remark = "示例账号"
        });

        VaultGrid.ItemsSource = Items;

        // 监听单元格点击
        VaultGrid.PreviewMouseLeftButtonUp += VaultGrid_PreviewMouseLeftButtonUp;
    }

    // 单击复制：网站 / 账号 / 密码
    private void VaultGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBlock tb &&
            !string.IsNullOrWhiteSpace(tb.Text))
        {
            Clipboard.SetText(tb.Text);
        }
    }
}

// 数据模型
public class VaultItem
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
    public string Remark { get; set; } = "";
}
