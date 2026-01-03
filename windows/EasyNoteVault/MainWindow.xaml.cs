using System.Collections.ObjectModel;
using System.Linq;
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

        // 1️⃣ 启动时加载本地数据
        var loaded = DataStore.Load();
        foreach (var item in loaded)
            Items.Add(item);

        VaultGrid.ItemsSource = Items;

        // 2️⃣ 单击复制
        VaultGrid.PreviewMouseLeftButtonUp += VaultGrid_PreviewMouseLeftButtonUp;

        // 3️⃣ 编辑完成：检测重复 + 自动保存
        VaultGrid.CellEditEnding += VaultGrid_CellEditEnding;

        // 4️⃣ 行删除 / 新增后也保存
        Items.CollectionChanged += (_, _) => Save();
    }

    // =============================
    // 单击复制（网站 / 账号 / 密码）
    // =============================
    private void VaultGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBlock tb &&
            !string.IsNullOrWhiteSpace(tb.Text))
        {
            Clipboard.SetText(tb.Text);
        }
    }

    // =============================
    // 编辑完成：检测重复 + 保存
    // =============================
    private void VaultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Column.Header?.ToString() == "网站")
            CheckDuplicateUrl(e.Row.Item as VaultItem);

        // 延迟保存（确保值已提交）
        Dispatcher.InvokeAsync(Save);
    }

    // =============================
    // 网址重复检测
    // =============================
    private void CheckDuplicateUrl(VaultItem? current)
    {
        if (current == null)
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

    // =============================
    // 保存数据
    // =============================
    private void Save()
    {
        DataStore.Save(Items);
    }

    // =============================
    // 网址标准化
    // =============================
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

// =============================
// 数据模型
// =============================
public class VaultItem
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
    public string Remark { get; set; } = "";
}
