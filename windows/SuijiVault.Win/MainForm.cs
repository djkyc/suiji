
using System.Windows.Forms;
namespace SuijiVault {
  public class MainForm : Form {
    public MainForm() {
      Text = "随记（Suiji Vault）";
      Width = 900; Height = 600;
      Controls.Add(new Label{
        Dock=DockStyle.Fill,
        TextAlign=System.Drawing.ContentAlignment.MiddleCenter,
        Text="Suiji Vault Windows\n完整工程骨架（可直接扩展）"
      });
    }
  }
}
