namespace ps_activity_insights
{    
    using System.Windows;
    using Microsoft.VisualStudio.PlatformUI;

    public partial class TosDialog : DialogWindow
    {
        public TosDialog(string tosText)
        {
            InitializeComponent();
            TermsOfService_Text.Text = tosText;
        }

        private void TermsOfService_Open(object sender, RoutedEventArgs e)
        {
            _ = System.Diagnostics.Process.Start("https://www.pluralsight.com/terms");
        }

        private void PrivacyPolicy_Open(object sender, RoutedEventArgs e)
        {
            _ = System.Diagnostics.Process.Start("https://www.pluralsight.com/privacy");
        }
        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
