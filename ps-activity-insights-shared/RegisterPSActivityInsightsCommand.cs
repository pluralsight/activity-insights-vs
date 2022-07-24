namespace ps_activity_insights_shared
{
    using System;
    using System.ComponentModel.Design;
    using System.Windows;
    using log4net;
    using Microsoft.VisualStudio.Shell;
    using Task = System.Threading.Tasks.Task;

    internal sealed class RegisterPSActivityInsightsCommand
    {
        public const int CommandId = 4129;
        public static readonly Guid CommandSet = new Guid("3d96b5c3-90fb-416f-aaf2-3968ba8bbab1");
        private readonly AsyncPackage package;
        private readonly ILog logger;

        private RegisterPSActivityInsightsCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(Execute, menuCommandID);
            menuItem.BeforeQueryStatus += new EventHandler(OnBeforeQueryStatus);
            commandService.AddCommand(menuItem);
            logger = LogManager.GetLogger(typeof(PSActivityInsights));
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            var myCommand = sender as OleMenuCommand;
            myCommand.Enabled = Utilities.HasBinary();
        }

        public static RegisterPSActivityInsightsCommand Instance
        {
            get;
            private set;
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new RegisterPSActivityInsightsCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                logger.Info("Registering user from Tools menu");
                var message = "Register this device to see your Pluralsight Activity Insights metrics.";
                var label = "Register New Device";
                MessageBoxResult res = MessageBox.Show(message, label, MessageBoxButton.OKCancel, MessageBoxImage.Question);
                switch (res)
                {
                    case MessageBoxResult.OK:
                        if (package is PSActivityInsights ext)
                        {
                            _ = ext.RegisterUserAsync();
                            _ = ext.StartPulseTrackingAsync();
                        }
                        break;
                    case MessageBoxResult.Cancel:
                        _ = MessageBox.Show("You can always opt in later on by enabling from the Tools window.");
                        break;
                }
            } catch (Exception ex)
            {
                logger.Error(ex);
            }
        }
    }
}
