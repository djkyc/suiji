
using System;
using System.Windows.Forms;
namespace SuijiVault {
  static class Program {
    [STAThread] static void Main() {
      ApplicationConfiguration.Initialize();
      Application.Run(new MainForm());
    }
  }
}
