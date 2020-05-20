namespace ps_activity_insights
{
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text.Json.Serialization;
    using System.Text.Json;
    using System.Threading;
    using System.Windows;
    using System;
    using EnvDTE;
    using Microsoft.VisualStudio.Shell.Interop;
    using Microsoft.VisualStudio.Shell;
    using Task = System.Threading.Tasks.Task;
    using Timer = System.Threading.Timer;
    using Window = EnvDTE.Window;
    using log4net;

    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(UIContextGuids.SolutionExists, flags: PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(AutoloadGuidForNonSolutions, flags: PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 1)]

    public sealed class PSActivityInsights : AsyncPackage
    {
        const string AutoloadGuidForNonSolutions = "4646B819-1AE0-4E79-97F4-8A8176FDD664";
        public const string PackageGuidString = "c5214e54-d0f1-48d2-8158-fc00b6c64519";
        private readonly int cacheBustTime = 60000;
        private readonly List<Event> eventList = new List<Event>();
        private Timer timer;
        private TextEditorEvents textEditorEvents;
        private DTEEvents dteEvents;
        private BuildEvents buildEvents;
        private DocumentEvents documentEvents;
        private ILog logger;
        private string lastFile = null;
        private long? lastFileTime = null;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.RunAsync(async () =>
            {
                Logger.Setup();
                this.logger = LogManager.GetLogger(typeof(PSActivityInsights));

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    var psActivityInsightsInstallDir = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var psActivityInsightsInstallFile = Path.Combine(Directory.GetParent(psActivityInsightsInstallDir).ToString(), "installation.yaml");

                    if (!File.Exists(psActivityInsightsInstallFile))
                    {
                        var message = "Register this device to see your Pluralsight Activity Insights metrics.";
                        var label = "Register New Device";
                        MessageBoxResult res = MessageBox.Show(message, label, MessageBoxButton.OKCancel, MessageBoxImage.Question);
                        switch (res)
                        {
                            case MessageBoxResult.OK:
                                logger.Info("Registering user from popup window");
                                this.RegisterUser();
                                await this.StartPulseTrackingAsync();
                                break;
                            case MessageBoxResult.Cancel:
                                MessageBox.Show("You can always opt in later on by enabling from the Tools window.");
                                break;
                        }
                        var response = res.ToString();
                        CreateInstallFile(response, psActivityInsightsInstallFile);
                    }
                    else
                    {
                        var yamlFile = CheckInstallStatus(psActivityInsightsInstallFile);
                        if (yamlFile.PromptResponse == MessageBoxResult.OK.ToString())
                        {
                            await this.StartPulseTrackingAsync();
                        }
                    }
                } catch (Exception e)
                {
                    logger.Error(e);
                }
            });
            await RegisterPSActivityInsightsCommand.InitializeAsync(this);
            await OpenPSActivityInsightsDashboard.InitializeAsync(this);
        }

        private InstallFile CheckInstallStatus(string filePath)
        {
            var fileContents = File.ReadAllText(filePath);
            var deserializer = new YamlDotNet.Serialization.Deserializer();
            var yamlFile = deserializer.Deserialize<InstallFile>(fileContents);
            return yamlFile;
        }

        private void CreateInstallFile(string response, string filePath)
        {
            var serializer = new YamlDotNet.Serialization.Serializer();
            var fileContents = serializer.Serialize(new InstallFile
            {
                PromptResponse = response,
                InstallDate = DateTimeOffset.UtcNow.ToString("s", System.Globalization.CultureInfo.InvariantCulture)
            });
            File.WriteAllText(filePath, fileContents);
        }

        public void RegisterUser()
        {
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            var executableDir = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var executablePath = Path.Combine(Directory.GetParent(executableDir).ToString(), "ps-activity-insights.exe");
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                CreateNoWindow = true,
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardError = true,
                Arguments = $"/C {executablePath} register"
            };
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                this.logger.Error($"Register process exited with nonzero status code.\n{process.StandardError.ReadToEnd()}");
            }
        }

        public async Task StartPulseTrackingAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            this.timer = new Timer((t) => { SendBatch(); }, new AutoResetEvent(true), TimeSpan.Zero, TimeSpan.FromMinutes(1));
            if (await GetServiceAsync(typeof(DTE)) is DTE dte)
            {
                Events events = dte.Events;

                WindowEvents windowEvents = events.WindowEvents;
                windowEvents.WindowActivated += this.OnActiveDocumentChanged;

                this.dteEvents = events.DTEEvents;
                this.dteEvents.OnBeginShutdown += this.OnBeginShutdown;

                this.textEditorEvents = events.TextEditorEvents;
                this.textEditorEvents.LineChanged += this.OnLineChanged;

                this.buildEvents = events.BuildEvents;
                this.buildEvents.OnBuildDone += this.OnBuildDone;

                this.documentEvents = events.DocumentEvents;
                this.documentEvents.DocumentSaved += this.OnDocumentSaved;
            }
        }

        private void AddEventToBatch(Event e)
        {
            if (e.EventType is EventType.Shutdown)
            {
                this.SendBatch();
            }
            else
            {
                this.eventList.Add(e);
            }
        }

        public void OpenDashboard()
        {
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            var executableDir = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var executablePath = Path.Combine(Directory.GetParent(executableDir).ToString(), "ps-activity-insights.exe");
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                CreateNoWindow = true,
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardError = true,
                Arguments = $"/C {executablePath} dashboard"
            };
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                this.logger.Error($"Dashboard opening process exited with nonzero status code.\n{process.StandardError.ReadToEnd()}");
            }
        }

        private void SendBatch()
        {
            if (this.eventList.Count == 0) return;

            JsonSerializerOptions options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var serialized = JsonSerializer.Serialize(this.eventList, options);
            this.eventList.Clear();

            System.Diagnostics.Process process = new System.Diagnostics.Process();
            var executableDir = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var executablePath = Path.Combine(Directory.GetParent(executableDir).ToString(), "ps-activity-insights.exe");
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                CreateNoWindow = true,
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                Arguments = $"/C {executablePath}"
            };
            process.StartInfo = startInfo;
            process.Start();
            process.StandardInput.WriteLine(serialized);
            process.StandardInput.Close();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                this.logger.Error($"Sending pulses exited with nonzero exit code.\n{process.StandardError.ReadToEnd()}");
            }
        }

        private void HandleEvent(Event e)
        {
            this.AddEventToBatch(e);
        }

        private void OnBuildDone(vsBuildScope scope, vsBuildAction action)
        {
            this.HandleEvent(new Event(EventType.BuildDone));
        }

        private void OnDocumentSaved(Document document)
        {
            JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (document != null)
                {
                    var filePath = document.FullName;
                    HandleEvent(new Event(EventType.SaveFile, filePath));
                }
            });
        }

        private void OnBeginShutdown()
        {
            this.HandleEvent(new Event(EventType.Shutdown));
            this.logger.Info("Beginning IDE shutdown");
        }

        private void OnLineChanged(TextPoint start, TextPoint end, int hint)
        {
            JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (await GetServiceAsync(typeof(DTE)) is DTE dte)
                {
                    if (dte.ActiveDocument is Document document)
                    {
                        var filePath = document.FullName;
                        var e = new Event(EventType.Typing, filePath);

                        if (e.FilePath != this.lastFile || this.EnoughTimePassed(e))
                        {
                            this.lastFile = filePath;
                            this.lastFileTime = e.EventDate;
                            HandleEvent(e);
                        }
                    }
                }
            });
        }

        private bool EnoughTimePassed(Event e)
        {
            return (e.EventDate - this.lastFileTime) > this.cacheBustTime;
        }

        private void OnActiveDocumentChanged(Window getFocus, Window lostFocus)
        {
            JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var document = getFocus.Document;

                if (document != null)
                {
                    var filePath = document.FullName;
                    HandleEvent(new Event(EventType.ChangeTab, filePath));
                }
            });
        }

    }
}
