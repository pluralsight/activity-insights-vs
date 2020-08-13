using Microsoft.VisualStudio.PlatformUI;
using System.Windows;
namespace ps_activity_insights
{
    public partial class TosDialog : DialogWindow
    {
        public TosDialog(string tosText)
        {
            InitializeComponent();
            this.TermsOfService_Text.Text = tosText;
        }

        private void TermsOfService_Open(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://www.pluralsight.com/terms");
        }

        private void PrivacyPolicy_Open(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://www.pluralsight.com/privacy");
        }
        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
