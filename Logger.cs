using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System;
using System.IO;

namespace ps_activity_insights
{
    public class Logger
    {
        public static void Setup()
        {
            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();

            PatternLayout patternLayout = new PatternLayout();
            patternLayout.ConversionPattern = "%utcdate [%thread] %-5level %logger - %message%newline";
            patternLayout.ActivateOptions();

            RollingFileAppender roller = new RollingFileAppender();
            roller.AppendToFile = true;
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var pluralsightDir = Path.Combine(homeDir, ".pluralsight");
            var logFile = Path.Combine(pluralsightDir, "vs-extension.logs");
            roller.File = logFile;
            roller.Layout = patternLayout;
            roller.MaxSizeRollBackups = 2;
            roller.MaximumFileSize = "500MB";
            roller.RollingStyle = RollingFileAppender.RollingMode.Size;
            roller.StaticLogFileName = true;
            roller.ActivateOptions();
            hierarchy.Root.AddAppender(roller);

            hierarchy.Root.Level = Level.Info;
            hierarchy.Configured = true;
        }
    }
}
