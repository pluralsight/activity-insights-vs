namespace ps_activity_insights_shared
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
#if VS19
        public const string PackageGuidString = "c5214e54-d0f1-48d2-8158-fc00b6c64519";
#else
        public const string PackageGuidString = "265b4cf4-106d-4ae8-933b-7351bb4721c4";
#endif
        private readonly int cacheBustTime = 60000;
        private ConcurrentQueue<Event> eventList = new ConcurrentQueue<Event>();
        private readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            },
            Converters = new List<JsonConverter> { new StringEnumConverter { NamingStrategy = new CamelCaseNamingStrategy(), AllowIntegerValues = false } }
        };

        private Timer timer;
        private TextEditorEvents textEditorEvents;
        private DTEEvents dteEvents;
        private BuildEvents buildEvents;
        private DocumentEvents documentEvents;
        private ILog logger;
        private bool started = false;
        private string lastFile = null;
        private long? lastFileTime = null;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.RunAsync(async () =>
            {
                Logger.Setup();
                logger = LogManager.GetLogger(typeof(PSActivityInsights));

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    if (Utilities.IsReady())
                    {
                        await StartPulseTrackingAsync();
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

        private void StartTimer()
        {
            if (!started)
            {
                started = true;
                timer = new Timer((t) => { SendBatch(); }, new AutoResetEvent(true), TimeSpan.Zero, TimeSpan.FromMinutes(1));
            }
        }

        private void StopTimer()
        {
            if (started)
            {
                started = false;
                timer?.Dispose();
                timer = null;
            }
        }

        public async Task RegisterUserAsync()
        {
            var result = ExecuteCommand("register");

            if (result.ExitCode == 100)
            {
                var acceptedTos = ShowTos(await result.StandardOutput.ReadToEndAsync());

                if (acceptedTos) {
                    await StartPulseTrackingAsync();
                    await RegisterUserAsync();
                } else
                {
                    await StopPulseTrackingAsync();
                }
            }
            if (result.ExitCode != 0)
            {
                logger.Error($"Register process exited with nonzero status code.\n{await result.StandardError.ReadToEndAsync()}");
            }
        }

        public async Task StartPulseTrackingAsync()
        {
            if (started) return;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            StartTimer();
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

        public async Task StopPulseTrackingAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            StopTimer();
            if (await GetServiceAsync(typeof(DTE)) is DTE dte)
            {
                Events events = dte.Events;

                WindowEvents windowEvents = events.WindowEvents;
                windowEvents.WindowActivated -= OnActiveDocumentChanged;

                dteEvents = events.DTEEvents;
                dteEvents.OnBeginShutdown -= OnBeginShutdown;

                textEditorEvents = events.TextEditorEvents;
                textEditorEvents.LineChanged -= OnLineChanged;

                buildEvents = events.BuildEvents;
                buildEvents.OnBuildDone -= OnBuildDone;

                documentEvents = events.DocumentEvents;
                documentEvents.DocumentSaved -= OnDocumentSaved;
            }
        }

        private void AddEventToBatch(Event e)
        {
            if (e.EventType is EventType.Shutdown)
            {
                SendBatch();
            }
            else
            {
                eventList.Enqueue(e);
            }
        }

        public async Task OpenDashboardAsync()
        {
            var result = ExecuteCommand("dashboard");

            if (result.ExitCode == 100)
            {
                var acceptedTos = ShowTos(await result.StandardOutput.ReadToEndAsync());

                if (acceptedTos)
                {
                    await StartPulseTrackingAsync();
                    await OpenDashboardAsync();
                } else
                {
                    await StopPulseTrackingAsync();
                }
            }
            else if (result.ExitCode != 0)
            {
                logger.Error($"Dashboard opening process exited with nonzero status code.\n{await result.StandardError.ReadToEndAsync()}");
            }
        }

        private void SendBatch()
        {
            if (eventList.Count == 0) return;

            var lastQueue = Interlocked.Exchange(ref eventList, new ConcurrentQueue<Event>());
            var serialized = JsonConvert.SerializeObject(lastQueue, jsonSettings);

            var result = ExecuteCommandToStdIn(serialized);

            if (result.ExitCode == 100)
            {
                _ = JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var stdOut = await result.StandardOutput.ReadToEndAsync();

                    if (!ShowTos(stdOut))
                    {
                        await StopPulseTrackingAsync();
                    }
                });
            }
            else if (result.ExitCode != 0)
            {
                logger.Error($"Sending pulses exited with nonzero exit code.\n{result.StandardError.ReadToEnd()}");
            }
        }

        private void HandleEvent(Event e)
        {
            if (started)
            {
                AddEventToBatch(e);
            }
        }

        private void OnBuildDone(vsBuildScope scope, vsBuildAction action)
        {
            HandleEvent(new Event(EventType.BuildDone));
        }

        private void OnDocumentSaved(Document document)
        {
            _ = JoinableTaskFactory.RunAsync(async () =>
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
            HandleEvent(new Event(EventType.Shutdown));
            logger.Info("Beginning IDE shutdown");
        }

        private void OnLineChanged(TextPoint start, TextPoint end, int hint)
        {
            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (await GetServiceAsync(typeof(DTE)) is DTE dte)
                {
                    if (dte.ActiveDocument is Document document)
                    {
                        var filePath = document.FullName;
                        var e = new Event(EventType.Typing, filePath);

                        if (e.FilePath != lastFile || EnoughTimePassed(e))
                        {
                            lastFile = filePath;
                            lastFileTime = e.EventDate;
                            HandleEvent(e);
                        }
                    }
                }
            });
        }

        private bool EnoughTimePassed(Event e)
        {
            return (e.EventDate - lastFileTime) > cacheBustTime;
        }

        private void OnActiveDocumentChanged(Window getFocus, Window lostFocus)
        {
            _ = JoinableTaskFactory.RunAsync(async () =>
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

        private bool ShowTos(string tosText)
        {
#if VS19
            var tos = new ps_activity_insights.TosDialog(tosText);
#else
            var tos = new ps_activity_insights_22.TosDialog(tosText);
#endif
            var result = tos.ShowDialog();

            if ((bool)result)
            {
                return AcceptTos();
            }
            else
            {
                logger.Info("they clicked no");
                return false;
            }
        }

        private bool AcceptTos()
        {
            var result = ExecuteCommand("accept_tos");
            if (result.ExitCode > 0)
            {
                logger.Error("Failed to accept TOS");
                return false;
            }

            return true;
        }

        private System.Diagnostics.Process ExecuteCommand(string command)
        {
            var process = MakeProcess(command, false);

            _ = process.Start();
            process.WaitForExit();

            return process;
        }

        private System.Diagnostics.Process ExecuteCommandToStdIn(string stdin)
        {
            var process = MakeProcess(null, true);

            _ = process.Start();
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
                RedirectStandardOutput = true,
                RedirectStandardInput = isStdIn,
                Arguments = $"/C \"{Utilities.binaryPath}\"{safeCommand}"
            };
            process.StartInfo = startInfo;

            return process;
        }
    }
}