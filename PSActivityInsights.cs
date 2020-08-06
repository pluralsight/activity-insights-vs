namespace ps_activity_insights
{
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using Newtonsoft.Json.Converters;
    using System.Threading;
    using System;
    using EnvDTE;
    using Microsoft.VisualStudio.Shell.Interop;
    using Microsoft.VisualStudio.Shell;
    using Task = System.Threading.Tasks.Task;
    using Timer = System.Threading.Timer;
    using Window = EnvDTE.Window;
    using log4net;
    using System.Collections.Concurrent;
    using System.ComponentModel;

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
        private ConcurrentQueue<Event> eventList = new ConcurrentQueue<Event>();
        private readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            },
            Converters = new List<JsonConverter> { new StringEnumConverter { CamelCaseText = true, AllowIntegerValues = false } }
        };

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
                    if (Utilities.IsReady())
                    {
                        await this.StartPulseTrackingAsync();
                    }

                    if (!Utilities.HasBinary())
                    {
                        async void cb(object o, AsyncCompletedEventArgs a)
                        {
                            if (Utilities.IsRegistered())
                            {
                                await StartPulseTrackingAsync();
                            }
                        }
                        Utilities.DownloadBinaryAndThen(cb);
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e);
                }
            });
            await RegisterPSActivityInsightsCommand.InitializeAsync(this);
            await OpenPSActivityInsightsDashboard.InitializeAsync(this);
        }

        public void RegisterUser()
        {
            var result = this.ExecuteCommand("register");

            if (result.ExitCode != 0)
            {
                this.logger.Error($"Register process exited with nonzero status code.\n{result.StandardError.ReadToEnd()}");
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
                this.eventList.Enqueue(e);
            }
        }

        public void OpenDashboard()
        {
            var result = this.ExecuteCommand("dashboard");

            if (result.ExitCode != 0)
            {
                this.logger.Error($"Dashboard opening process exited with nonzero status code.\n{result.StandardError.ReadToEnd()}");
            }
        }

        private void SendBatch()
        {
            if (this.eventList.Count == 0) return;

            var lastQueue = Interlocked.Exchange(ref this.eventList, new ConcurrentQueue<Event>());
            var serialized = JsonConvert.SerializeObject(lastQueue, this.jsonSettings);

            var result = this.ExecuteCommandToStdIn(serialized);

            if (result.ExitCode != 0)
            {
                this.logger.Error($"Sending pulses exited with nonzero exit code.\n{result.StandardError.ReadToEnd()}");
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

        private System.Diagnostics.Process ExecuteCommand(string command)
        {
            var process = this.MakeProcess(command, false);

            process.Start();
            process.WaitForExit();

            return process;
        }

        private System.Diagnostics.Process ExecuteCommandToStdIn(string stdin)
        {
            var process = this.MakeProcess(null, true);

            process.Start();
            process.StandardInput.WriteLine(stdin);
            process.StandardInput.Close();
            process.WaitForExit();

            return process;
        }

        private System.Diagnostics.Process MakeProcess(string command, bool isStdIn)
        {
            var safeCommand = command == null ? "" : $" {command}";
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                CreateNoWindow = true,
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = isStdIn,
                Arguments = $"/C \"{Utilities.binaryPath}\"{safeCommand}"
            };
            process.StartInfo = startInfo;

            return process;
        }
    }
}