using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using JangJang.Core;

namespace JangJang.ViewModels;

public partial class PetViewModel : ObservableObject
{
    private readonly ActivityMonitor _monitor;
    private readonly AppSettings _settings;
    private int _dialogueCooldown;

    [ObservableProperty] private PetState _currentState = PetState.Sleeping;
    [ObservableProperty] private double _annoyanceLevel;
    [ObservableProperty] private double _shakeAmplitude;
    [ObservableProperty] private string _statusText = "zzZ...";
    [ObservableProperty] private string _workTimeText = "";
    [ObservableProperty] private ImageSource? _petImageSource;
    [ObservableProperty] private double _angryScale = 1.0;

    public PetViewModel(ActivityMonitor monitor, AppSettings settings)
    {
        _monitor = monitor;
        _settings = settings;
        _monitor.StateUpdated += OnStateUpdated;
        LoadPetImage();
        UpdateWorkTimeText();
    }

    public void LoadPetImage()
    {
        if (!string.IsNullOrEmpty(_settings.PetImagePath) && File.Exists(_settings.PetImagePath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(_settings.PetImagePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            PetImageSource = bitmap;
        }
        else
        {
            PetImageSource = new BitmapImage(new Uri("pack://application:,,,/Resources/pet.png"));
        }
    }

    private void UpdateWorkTimeText()
    {
        var s = _monitor.SessionSeconds;
        WorkTimeText = $"{s / 3600:D2}:{s % 3600 / 60:D2}:{s % 60:D2}";
    }

    private void OnStateUpdated(PetState state, double annoyance)
    {
        var prevState = CurrentState;
        CurrentState = state;
        AnnoyanceLevel = annoyance;

        // 작업 시간 업데이트
        UpdateWorkTimeText();

        // 대사: 상태 전환 시 즉시, 같은 상태면 5~8초마다 교체
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

            case PetState.Idle:
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
        }
    }
}
