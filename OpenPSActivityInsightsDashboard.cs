namespace ps_activity_insights
{
    using System;
    using System.ComponentModel.Design;
    using System.Windows;
    using log4net;
    using Microsoft.VisualStudio.Shell;
    using Task = System.Threading.Tasks.Task;

    internal sealed class OpenPSActivityInsightsDashboard
    {
        public const int CommandId = 4130;
        public static readonly Guid CommandSet = new Guid("3d96b5c3-90fb-416f-aaf2-3968ba8bbab1");
        private readonly AsyncPackage package;
        private readonly ILog logger;

        private OpenPSActivityInsightsDashboard(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(this.Execute, menuCommandID);
            menuItem.BeforeQueryStatus += new EventHandler(OnBeforeQueryStatus);
            this.logger = LogManager.GetLogger(typeof(OpenPSActivityInsightsDashboard));
            commandService.AddCommand(menuItem);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            var myCommand = sender as OleMenuCommand;
            myCommand.Enabled = Utilities.HasBinary();
        }

        public static OpenPSActivityInsightsDashboard Instance
        {
            get;
            private set;
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new OpenPSActivityInsightsDashboard(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                var message = "This will take you to your Pluralsight Activity Insights dashboard.";
                var label = "Click OK to continue.";
                MessageBoxResult res = MessageBox.Show(message, label, MessageBoxButton.OKCancel, MessageBoxImage.Question);
                switch (res)
                {
                    case MessageBoxResult.OK:
                        if (this.package is PSActivityInsights ext)
                        {
                            ext.OpenDashboard();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex);
            }
        }
    }
}
