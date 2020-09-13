namespace ps_activity_insights
{
    using log4net;
    using System;
    using System.ComponentModel;
    using System.IO;
    using System.Net;

    static class Utilities
    {
        private static readonly string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static readonly string pluralsightDir = Path.Combine(homeDir, ".pluralsight");
        private static readonly string credentialsPath = Path.Combine(pluralsightDir, "credentials.yaml");
        public static readonly string binaryPath = Path.Combine(pluralsightDir, "activity-insights.exe");
        private static readonly ILog logger;
        private static readonly Uri downloadPath = new Uri("https://ps-cdn.s3-us-west-2.amazonaws.com/learner-workflow/ps-time/windows/activity-insights-latest.exe");

        public static object KnownMonikers { get; private set; }

        static Utilities()
        {
            Logger.Setup();
            logger = LogManager.GetLogger(typeof(Utilities));
        }

        public static bool IsReady()
        {
            return IsRegistered() && HasBinary();
        }

        public static bool IsRegistered()
        {
            return File.Exists(credentialsPath);
        }

        public static bool HasBinary()
        {
            return File.Exists(binaryPath);
        }

        public static void DownloadBinaryAndThen(Action<object, AsyncCompletedEventArgs> cb)
        {
            using (var client = new WebClient())
            {
                try
                {
                    logger.Info("Attempting to download extension");
                    client.DownloadFileCompleted += new AsyncCompletedEventHandler(cb);
                    client.DownloadFileAsync(downloadPath, binaryPath);
                }
                catch (Exception e)
                {
                    logger.Error($"Error downloading binary {e.Message}");
                }
            }
        }
    }
}
