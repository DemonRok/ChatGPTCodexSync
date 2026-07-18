using System.Windows;
using ChatGPTCodexSync.App.ViewModels;

namespace ChatGPTCodexSync.App;

public partial class MainWindow : Window
{
  public MainWindow(MainWindowViewModel viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
  }

  private void LogTextBoxTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
  {
    LogTextBox.ScrollToEnd();
  }
}
