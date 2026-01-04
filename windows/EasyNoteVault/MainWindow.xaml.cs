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

            // 关键：重复检测
            VaultGrid.CellEditEnding += VaultGrid_CellEditEnding;
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

        // ================= 右键粘贴 =================
        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText())
                return;

            if (VaultGrid.CurrentCell.Item == null ||
                VaultGrid.CurrentCell.Column == null)
                return;

            VaultGrid.BeginEdit();

            var item = VaultGrid.CurrentCell.Item as VaultItem;
            if (item == null)
                return;

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

        // ================= 🔥 重复网址：禁止 + 定位 =================
        private void VaultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString() != "网站")
                return;

            var current = e.Row.Item as VaultItem;
            if (current == null)
                return;

            string newUrl = NormalizeUrl(current.Url);
            if (string.IsNullOrEmpty(newUrl))
                return;

            // 查找第一个重复项
            var duplicate = AllItems
                .FirstOrDefault(x =>
                    x != current &&
                    NormalizeUrl(x.Url) == newUrl);

            if (duplicate != null)
            {
                // 回滚当前输入
                current.Url = string.Empty;

                MessageBox.Show(
                    $"该网站已存在，不能重复添加兄弟：\n{duplicate.Url}",
                    "重复网址",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                // 定位到重复行
                VaultGrid.SelectedItem = duplicate;
                VaultGrid.ScrollIntoView(duplicate);

                // 强制取消本次编辑
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    VaultGrid.CancelEdit(DataGridEditingUnit.Cell);
                    VaultGrid.CancelEdit(DataGridEditingUnit.Row);
                }));
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
