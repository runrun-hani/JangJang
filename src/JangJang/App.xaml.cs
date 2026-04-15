using System.IO;
using System.Threading;
using System.Windows;
using JangJang.Core;
using JangJang.Core.Persona;
using JangJang.TrayIcon;
using JangJang.ViewModels;
using JangJang.Views;

namespace JangJang;

public partial class App : Application
{
    private const string EmbeddingModelFolderName = "multilingual-e5-small";

    private Mutex? _mutex;
    private ActivityMonitor? _monitor;
    private TrayIconManager? _trayManager;
    private NotificationManager? _notificationManager;
    private PersonaDialogueProvider? _personaProvider;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // 단일 인스턴스 보장
        _mutex = new Mutex(true, "JangJang_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("장장이 이미 실행 중입니다!", "장장", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();
        _monitor = new ActivityMonitor(settings);

        // 자캐 페르소나 모드 활성화 시 PersonaDialogueProvider 시도. 실패 시 기본 Provider 유지.
        TryInitializePersonaProvider(settings, _monitor);

        var viewModel = new PetViewModel(_monitor, settings);
        var petWindow = new PetWindow(viewModel, settings);

        _trayManager = new TrayIconManager(_monitor, petWindow, settings);
        _notificationManager = new NotificationManager(_monitor);

        petWindow.Show();
        _monitor.Start();
    }

    private void TryInitializePersonaProvider(AppSettings settings, ActivityMonitor monitor)
    {
        if (!settings.PersonaEnabled) return;

        var persona = PersonaStore.Load();
        if (persona == null || persona.SeedLines.Count == 0) return;

        var modelFolder = ResolveModelFolder();
        if (modelFolder == null) return;

        try
        {
            _personaProvider = PersonaDialogueProvider.Create(modelFolder, persona, monitor);
            Dialogue.SetProvider(_personaProvider);
        }
        catch
        {
            // 임베딩 서비스 로드 실패 (모델 손상, 토크나이저 미스매치 등)
            // → 기본 Provider 유지. UI에는 나중 단계에서 안내 토스트 추가 가능.
            _personaProvider?.Dispose();
            _personaProvider = null;
            Dialogue.ResetToDefault();
        }
    }

    /// <summary>
    /// 임베딩 모델 폴더 탐색. 표준 위치 → 포터블 위치 순서로 시도.
    /// </summary>
    private static string? ResolveModelFolder()
    {
        // 1. 표준 위치: %AppData%/JangJang/Models/multilingual-e5-small/
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JangJang", "Models", EmbeddingModelFolderName);
        if (Directory.Exists(appDataPath)) return appDataPath;

        // 2. 포터블/개발 폴백: 실행 파일 옆의 multilingual-e5-small/
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var exeDir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(exeDir))
            {
                var localPath = Path.Combine(exeDir, EmbeddingModelFolderName);
                if (Directory.Exists(localPath)) return localPath;
            }
        }

        return null;
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _personaProvider?.Dispose();
        _notificationManager?.Dispose();
        _monitor?.Dispose();
        _trayManager?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
