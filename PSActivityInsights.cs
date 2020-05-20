using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Timer = System.Threading.Timer;
using Window = EnvDTE.Window;
using System.IO;
using log4net;

namespace ps_activity_insights
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(UIContextGuids.SolutionExists, flags: PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(AUTOLOAD_GUID_FOR_NON_SOLUTIONS, flags: PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class PSActivityInsights : AsyncPackage
    {
        const string AUTOLOAD_GUID_FOR_NON_SOLUTIONS = "4646B819-1AE0-4E79-97F4-8A8176FDD664";
        public const string PackageGuidString = "c5214e54-d0f1-48d2-8158-fc00b6c64519";
        private List<Event> eventList = new List<Event>();
        private Timer timer;
        private TextEditorEvents textEditorEvents;
        private DTEEvents dteEvents;
        private BuildEvents buildEvents;
        private DocumentEvents documentEvents;
        private HashSet<String> eventCache = new HashSet<string>();
        private ILog logger;

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
                logger.Error($"Register process exited with nonzero status code.\n{process.StandardError.ReadToEnd()}");
            }
        }

        public async Task StartPulseTrackingAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            timer = new Timer((t) => { SendBatch(); }, new AutoResetEvent(true), TimeSpan.Zero, TimeSpan.FromMinutes(1));
            if (await GetServiceAsync(typeof(DTE)) is DTE dte)
            {
                Events events = dte.Events;

                WindowEvents windowEvents = events.WindowEvents;
                windowEvents.WindowActivated += OnActiveDocumentChanged;

                dteEvents = events.DTEEvents;
                dteEvents.OnBeginShutdown += OnBeginShutdown;

                textEditorEvents = events.TextEditorEvents;
                textEditorEvents.LineChanged += OnLineChanged;

                buildEvents = events.BuildEvents;
                buildEvents.OnBuildDone += OnBuildDone;

                documentEvents = events.DocumentEvents;
                documentEvents.DocumentSaved += OnDocumentSaved;
            }
        }

        private void addEventToBatch(Event e)
        {
            if (e.eventType is eventType.Shutdown)
            {
                SendBatch();
            } else
            {
                eventList.Add(e);
            }
        }

        private void SendBatch()
        {
            if (eventList.Count == 0) return;

            JsonSerializerOptions options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var serialized = JsonSerializer.Serialize(eventList, options);
            eventList.Clear();
            eventCache.Clear();

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
                logger.Error($"Sending pulses exited with nonzero exit code.\n{process.StandardError.ReadToEnd()}");
            }
        }

        private void handleEvent(Event e)
        {
            if (eventCache.Add($"{e.eventType.ToString()}{e.filePath}")) addEventToBatch(e);
        }

        private void OnBuildDone(vsBuildScope Scope, vsBuildAction Action)
        {
            handleEvent(new Event(eventType.BuildDone));
        }

        private void OnDocumentSaved(Document document)
        {
            JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (document != null)
                {
                    var filePath = document.FullName;
                    handleEvent(new Event(eventType.SaveFile, filePath));
                }
            });
        }

        private void OnBeginShutdown()
        {
            handleEvent(new Event(eventType.Shutdown));
            logger.Info("Beginning IDE shutdown");
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
                        handleEvent(new Event(eventType.Typing, filePath));
                    }
                }
            });
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
                    handleEvent(new Event(eventType.ChangeTab, filePath));
                }
            });
        }

    }
}
