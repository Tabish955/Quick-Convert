using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using QuickConvert.Core.Diagnostics;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Ipc;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using QuickConvert.Engines.Docs;
using QuickConvert.Engines.Images;
using QuickConvert.Engines.Media;
using QuickConvert.UI.ViewModels;
using QuickConvert.UI.Views;

namespace QuickConvert.UI;

/// <summary>
/// Interaction logic for App.xaml.
/// Orchestrates DI, Single-Instance Named Pipe IPC, global exception handling, and Explorer entry routing.
///
/// Phase 2 hardening: the library never exits the process; a failed IPC handshake produces a
/// visible explanation and a log entry; unhandled exceptions are logged and surfaced.
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private SingleInstancePipeService? _pipeService;
    private QuickPopupWindow? _activeQuickPopupWindow;
    private MainDashboardWindow? _activeDashboardWindow;

    public App()
    {
        // UI-thread exceptions (command handlers, event handlers): log, tell the user, keep running.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Non-UI-thread exceptions: the process is going down regardless; make sure the reason lands in the log.
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            QuickConvertLogger.PurgeOldLogs();
            QuickConvertLogger.Info("QuickConvert starting.");

            // 1. Initialize IPC Single-Instance Service
            _pipeService = new SingleInstancePipeService();
            bool isPrimary = _pipeService.TryAcquireSingleInstance();

            if (!isPrimary)
            {
                // Secondary instance: transmit arguments to primary via Named Pipe.
                // If that fails the user must hear about it — their selection is never silently dropped.
                var sendResult = await SingleInstancePipeService.SendFilesToPrimaryAsync(e.Args, timeoutMs: 300);

                switch (sendResult)
                {
                    case FileSendResult.Sent:
                    case FileSendResult.NoFiles:
                        break;
                    case FileSendResult.Failed:
                        QuickConvertLogger.Error("Secondary instance could not reach the primary; file selection was not delivered.");
                        bool createdNew;
                        using (var failureMutex = new Mutex(initiallyOwned: true, @"Local\QuickConvert_HandshakeFailureShown", out createdNew))
                        {
                            if (createdNew)
                            {
                                MessageBox.Show(
                            "QuickConvert could not hand your files to the running window. Open QuickConvert once from the Start menu and try again.",
                            "QuickConvert", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                        break;
                }

                Shutdown();
                return;
            }

            // 2. Setup Dependency Injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // 3. Start Named Pipe Server on primary instance
            _pipeService.FilesReceivedFromExplorer += OnFilesReceivedFromExplorer;
            _pipeService.StartPipeServer();

            // 4. Initial Routing based on launch arguments
            var incomingFiles = e.Args.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();

            if (incomingFiles.Count > 0)
            {
                // Launched with files or folders from Explorer -> Show Quick Popup
                var cursor = SingleInstancePipeService.GetCursorPosition();
                ShowQuickPopup(incomingFiles, cursor);
            }
            else
            {
                // Launched directly (e.g. Start Menu) -> Show Full Dashboard
                ShowMainDashboard();
            }
        }
        catch (Exception ex)
        {
            // Startup must never die silently: log, tell the user, shut down cleanly.
            QuickConvertLogger.Error("Unhandled exception during startup.", ex);
            MessageBox.Show(
                $"QuickConvert could not start: {ex.Message}",
                "QuickConvert", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Engines are registered as multiples; the router is the only way anything reaches them.
        services.AddSingleton<IConversionEngine, FfmpegConversionEngine>();
        services.AddSingleton<IConversionEngine, MagickImageConversionEngine>();
        services.AddSingleton<IConversionEngine, PandocConversionEngine>();
        services.AddSingleton<IConversionEngine, SpreadsheetConversionEngine>();
        services.AddSingleton<IConversionEngine, ArchiveConversionEngine>();
        services.AddSingleton<IConversionEngineRouter, ConversionEngineRouter>();

        // Core Services
        services.AddSingleton<BatchConversionCoordinator>();
        services.AddSingleton(_pipeService!);

        // ViewModels
        services.AddTransient<QuickPopupViewModel>();
        services.AddSingleton<MainDashboardViewModel>();

        // Windows
        services.AddTransient<QuickPopupWindow>();
        services.AddSingleton<MainDashboardWindow>();
    }

    private void OnFilesReceivedFromExplorer(object? sender, FilesReceivedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ShowQuickPopup(e.FilePaths, e.CursorPosition);
        });
    }

    public void ShowQuickPopup(IReadOnlyList<string> filePaths, ScreenPoint cursor)
    {
        if (_activeQuickPopupWindow == null || !_activeQuickPopupWindow.IsLoaded)
        {
            var vm = _serviceProvider!.GetRequiredService<QuickPopupViewModel>();
            _activeQuickPopupWindow = new QuickPopupWindow(vm);

            vm.RequestOpenDashboard += files =>
            {
                ShowMainDashboard(files);
            };

            _activeQuickPopupWindow.Closed += (s, ev) => _activeQuickPopupWindow = null;
        }

        _activeQuickPopupWindow.ViewModel.LoadFiles(filePaths);
        _activeQuickPopupWindow.PositionNearCursor(cursor);
        _activeQuickPopupWindow.Show();
        _activeQuickPopupWindow.Activate();
    }

    public void ShowMainDashboard(IEnumerable<string>? initialFiles = null)
    {
        if (_activeDashboardWindow == null || !_activeDashboardWindow.IsLoaded)
        {
            _activeDashboardWindow = _serviceProvider!.GetRequiredService<MainDashboardWindow>();
            _activeDashboardWindow.Closed += (s, ev) => _activeDashboardWindow = null;
        }

        if (initialFiles != null)
        {
            _activeDashboardWindow.ViewModel.AddFilePaths(initialFiles);
        }

        _activeDashboardWindow.Show();
        _activeDashboardWindow.Activate();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        QuickConvertLogger.Error("Unhandled exception on the UI thread.", e.Exception);
        MessageBox.Show(
            $"Something went wrong: {e.Exception.Message}",
            "QuickConvert", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // No UI thread left to show a dialog on; the log is the last word.
        if (e.ExceptionObject is Exception ex)
        {
            QuickConvertLogger.Error("Unhandled exception on a background thread; the application will exit.", ex);
        }
        else
        {
            QuickConvertLogger.Error("Unhandled exception on a background thread; the application will exit.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        QuickConvertLogger.Info("QuickConvert exiting.");
        _pipeService?.Dispose();
        base.OnExit(e);
    }
}
