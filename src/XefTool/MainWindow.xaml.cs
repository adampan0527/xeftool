using Wpf.Ui.Controls;

namespace XefTool;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void LogTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
            textBox.ScrollToEnd();
    }
}
