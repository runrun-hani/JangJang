using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using JangJang.Core;
using JangJang.Core.Persona;

namespace JangJang.ViewModels;

public partial class PetViewModel : ObservableObject
{
    private readonly ActivityMonitor _monitor;
    private readonly AppSettings _settings;
    private int _dialogueCooldown;

    // 상태별 이미지 캐시
    private ImageSource? _defaultImage;
    private ImageSource? _happyImage;
    private ImageSource? _idleImage;
    private ImageSource? _annoyedImage;
    private ImageSource? _sleepingImage;
    private ImageSource? _wakeUpImage;

    // 페르소나 모드에서 모든 상태에 공통으로 사용하는 초상화
    // null이면 페르소나 모드가 아니거나 로드 실패 → 기존 상태별 이미지 사용
    private ImageSource? _personaPortrait;

    [ObservableProperty] private PetState _currentState = PetState.Sleeping;
    [ObservableProperty] private double _annoyanceLevel;
    [ObservableProperty] private double _shakeAmplitude;
    [ObservableProperty] private string _statusText = "zzZ...";
    [ObservableProperty] private string _workTimeText = "";
    [ObservableProperty] private ImageSource? _petImageSource;
    [ObservableProperty] private double _angryScale = 1.0;
    [ObservableProperty] private bool _isTimeReversing;

    public PetViewModel(ActivityMonitor monitor, AppSettings settings)
    {
        _monitor = monitor;
        _settings = settings;
        _monitor.StateUpdated += OnStateUpdated;
        LoadAllImages();
        UpdateWorkTimeText();
    }

    public void LoadAllImages()
    {
        _defaultImage = LoadImage(_settings.PetImagePath, "pack://application:,,,/Resources/Default.png");
        _happyImage = LoadImage(_settings.HappyImagePath, null);
        _idleImage = LoadImage(_settings.IdleImagePath, null);
        _annoyedImage = LoadImage(_settings.AnnoyedImagePath, null);
        _sleepingImage = LoadImage(_settings.SleepingImagePath, "pack://application:,,,/Resources/Sleeping.png");
        _wakeUpImage = LoadImage(_settings.WakeUpImagePath, "pack://application:,,,/Resources/WakeUp.png");

        // 페르소나 모드: 활성화 + 페르소나 데이터 + 초상화 파일이 모두 존재하면 초상화 로드
        // 이 초상화는 GetImageForState에서 모든 상태에 대해 반환됨
        _personaPortrait = TryLoadPersonaPortrait();

        // 현재 상태에 맞는 이미지 적용
        PetImageSource = GetImageForState(CurrentState);
    }

    private ImageSource? TryLoadPersonaPortrait()
    {
        if (!_settings.PersonaEnabled) return null;
        var data = PersonaStore.Load();
        if (data == null || string.IsNullOrEmpty(data.PortraitFileName)) return null;
        var path = PersonaStore.GetPortraitFullPath(data);
        if (!File.Exists(path)) return null;
        return LoadImage(path, null);
    }

    // 하위 호환
    public void LoadPetImage() => LoadAllImages();

    private static ImageSource? LoadImage(string? path, string? fallbackPack)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        if (fallbackPack != null)
            return new BitmapImage(new Uri(fallbackPack));
        return null;
    }

    private ImageSource? GetImageForState(PetState state)
    {
        // 페르소나 모드에서 초상화가 로드되어 있으면 모든 상태에 공통 적용
        if (_personaPortrait != null)
            return _personaPortrait;

        var stateImage = state switch
        {
            PetState.Happy => _happyImage,
            PetState.Alert => _idleImage,
            PetState.Annoyed => _annoyedImage,
            PetState.Sleeping => _sleepingImage,
            PetState.WakeUp => _wakeUpImage,
            _ => null
        };
        return stateImage ?? _defaultImage;
    }

    private void UpdateWorkTimeText()
    {
        IsTimeReversing = _monitor.IsReversing;
        var s = _monitor.SessionSeconds;
        WorkTimeText = $"{s / 3600:D2}:{s % 3600 / 60:D2}:{s % 60:D2}";
    }

    private void OnStateUpdated(PetState state, double annoyance)
    {
        var prevState = CurrentState;
        CurrentState = state;
        AnnoyanceLevel = annoyance;

        UpdateWorkTimeText();

        // 상태 전환 시 이미지 교체
        if (state != prevState)
            PetImageSource = GetImageForState(state);

        // 대사
        _dialogueCooldown--;
        if (state != prevState || _dialogueCooldown <= 0)
        {
            StatusText = Dialogue.GetLine(state, annoyance, _monitor.WorkLog.TodaySeconds);
            _dialogueCooldown = state == PetState.Happy ? 8 : 5;
        }

        switch (state)
        {
            case PetState.Happy:
                ShakeAmplitude = 0;
                AngryScale = 1.0;
                break;
            case PetState.Alert:
                ShakeAmplitude = 0;
                AngryScale = 1.0;
                break;
            case PetState.Annoyed:
                ShakeAmplitude = annoyance * 8;
                AngryScale = _settings.GrowWhenAnnoyed
                    ? 1.0 + annoyance * (_settings.MaxGrowScale - 1.0)
                    : 1.0;
                break;
            case PetState.Sleeping:
                ShakeAmplitude = 0;
                AngryScale = 1.0;
                break;
            case PetState.WakeUp:
                ShakeAmplitude = 0;
                AngryScale = 1.0;
                break;
        }
    }
}
