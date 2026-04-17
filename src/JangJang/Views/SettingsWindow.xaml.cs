using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using JangJang.Core;
using JangJang.Core.Persona;
using JangJang.Core.Persona.Suggestion;
using JangJang.Views.Persona;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using ListBox = System.Windows.Controls.ListBox;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;

namespace JangJang.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private string? _defaultPath, _happyPath, _idlePath, _annoyedPath, _sleepingPath, _wakeUpPath;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        foreach (IdlePreset preset in Enum.GetValues<IdlePreset>())
            PresetCombo.Items.Add(new ComboBoxItem { Content = preset.ToDisplayName(), Tag = preset });

        PresetCombo.SelectedIndex = (int)settings.IdlePreset;
        CustomMinutesBox.Text = settings.CustomIdleMinutes.ToString();
        SizeSlider.Value = settings.PetSize;
        TargetProcessBox.Text = settings.TargetProcessName;
        GrowCheck.IsChecked = settings.GrowWhenAnnoyed;
        GrowSlider.Value = settings.MaxGrowScale;

        _defaultPath = settings.PetImagePath;
        _happyPath = settings.HappyImagePath;
        _idlePath = settings.IdleImagePath;
        _annoyedPath = settings.AnnoyedImagePath;
        _sleepingPath = settings.SleepingImagePath;
        _wakeUpPath = settings.WakeUpImagePath;
        AutoStartCheck.IsChecked = AutoStartHelper.IsAutoStartEnabled();
        NoRestCheck.IsChecked = settings.NoRestMode;
        DebugModeCheck.IsChecked = settings.DebugMode;
        PersonaEnabledCheck.IsChecked = settings.PersonaEnabled;

        // API 설정 초기화
        ApiKeyMasked.Password = settings.SuggestionApiKey ?? string.Empty;
        ApiKeyVisible.Text = settings.SuggestionApiKey ?? string.Empty;
        if (!string.IsNullOrEmpty(settings.SuggestionApiModel))
            ModelCombo.Items.Add(settings.SuggestionApiModel);
        ModelCombo.Text = settings.SuggestionApiModel ?? "";

        // 페르소나 모드 → 프로그레시브 디스클로저
        PersonaEnabledCheck.Checked += (_, _) => UpdatePersonaVisibility();
        PersonaEnabledCheck.Unchecked += (_, _) => UpdatePersonaVisibility();
        UpdatePersonaVisibility();
        RefreshPersonaStatus();
        UpdateApiStatus();

        RefreshPreviews();
    }

    private void OnEditPersonaClick(object sender, RoutedEventArgs e)
    {
        var win = new PersonaWindow { Owner = this };
        win.ShowDialog();
        RefreshPersonaStatus();
    }

    private void RefreshPreviews()
    {
        SetPreview(PreviewDefault, _defaultPath);
        SetPreview(PreviewHappy, _happyPath);
        SetPreview(PreviewIdle, _idlePath);
        SetPreview(PreviewAnnoyed, _annoyedPath);
        SetPreview(PreviewSleeping, _sleepingPath, "Sleeping.png");
        SetPreviewWakeUp();
    }

    private void SetPreviewWakeUp()
    {
        if (!string.IsNullOrEmpty(_wakeUpPath) && File.Exists(_wakeUpPath))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(_wakeUpPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            PreviewWakeUp.Source = bmp;
            WakeUpPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewWakeUp.Source = null;
            WakeUpPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private static void SetPreview(Image img, string? path, string fallbackResource = "Default.png")
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;
        }
        else
        {
            img.Source = new BitmapImage(new Uri($"pack://application:,,,/Resources/{fallbackResource}"));
        }
    }

    private static string? PickImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "이미지 선택",
            Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.gif;*.bmp|모든 파일|*.*"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    // 기본 이미지
    private void OnSelectDefaultImage(object s, RoutedEventArgs e) { var p = PickImage(); if (p != null) { _defaultPath = p; RefreshPreviews(); } }
    private void OnResetDefaultImage(object s, RoutedEventArgs e) { _defaultPath = null; RefreshPreviews(); }

    // 상태별 이미지
    private void OnSelectHappyImage(object s, RoutedEventArgs e) { var p = PickImage(); if (p != null) { _happyPath = p; RefreshPreviews(); } }
    private void OnResetHappyImage(object s, RoutedEventArgs e) { _happyPath = null; RefreshPreviews(); }

    private void OnSelectIdleImage(object s, RoutedEventArgs e) { var p = PickImage(); if (p != null) { _idlePath = p; RefreshPreviews(); } }
    private void OnResetIdleImage(object s, RoutedEventArgs e) { _idlePath = null; RefreshPreviews(); }

    private void OnSelectAnnoyedImage(object s, RoutedEventArgs e) { var p = PickImage(); if (p != null) { _annoyedPath = p; RefreshPreviews(); } }
    private void OnResetAnnoyedImage(object s, RoutedEventArgs e) { _annoyedPath = null; RefreshPreviews(); }

    private void OnSelectSleepingImage(object s, RoutedEventArgs e) { var p = PickImage(); if (p != null) { _sleepingPath = p; RefreshPreviews(); } }
    private void OnResetSleepingImage(object s, RoutedEventArgs e) { _sleepingPath = null; RefreshPreviews(); }

    private void OnSelectWakeUpImage(object s, RoutedEventArgs e) { var p = PickImage(); if (p != null) { _wakeUpPath = p; RefreshPreviews(); } }
    private void OnResetWakeUpImage(object s, RoutedEventArgs e) { _wakeUpPath = null; RefreshPreviews(); }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is ComboBoxItem item && item.Tag is IdlePreset preset)
            CustomPanel.Visibility = preset == IdlePreset.Custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSelectProcessClick(object sender, RoutedEventArgs e)
    {
        var allProcs = Process.GetProcesses();
        var processes = new List<(string Name, string Title)>();
        var selfPid = Environment.ProcessId;
        foreach (var p in allProcs)
        {
            try
            {
                if (p.Id != selfPid && p.MainWindowHandle != IntPtr.Zero)
                {
                    var title = string.IsNullOrEmpty(p.MainWindowTitle) ? "" : p.MainWindowTitle;
                    processes.Add((p.ProcessName, title));
                }
            }
            catch { }
            finally { p.Dispose(); }
        }
        processes = processes.Distinct().OrderBy(x => x.Name).ToList();

        var selectWindow = new Window
        {
            Title = "실행 중인 프로그램 선택",
            Width = 480, Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush")
        };

        var stack = new StackPanel { Margin = new Thickness(16) };

        var header = new TextBlock
        {
            Text = "프로그램 선택",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var listBox = new ListBox { Height = 280 };

        foreach (var (name, title) in processes)
        {
            var display = string.IsNullOrEmpty(title) ? name : $"{name}  —  {title}";
            listBox.Items.Add(new ListBoxItem { Content = display, Tag = name });
        }

        var okBtn = new Button
        {
            Content = "선택", Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Style = (System.Windows.Style)FindResource("PrimaryButton")
        };
        okBtn.Click += (_, _) =>
        {
            if (listBox.SelectedItem is ListBoxItem selected)
            {
                TargetProcessBox.Text = (string)selected.Tag;
                selectWindow.Close();
            }
        };

        stack.Children.Add(header);
        stack.Children.Add(listBox);
        stack.Children.Add(okBtn);
        selectWindow.Content = stack;
        selectWindow.ShowDialog();
    }

    private void UpdatePersonaVisibility()
    {
        bool personaOn = PersonaEnabledCheck.IsChecked == true;

        // Progressive disclosure: show/hide persona details
        PersonaDetailPanel.Visibility = personaOn ? Visibility.Visible : Visibility.Collapsed;
        PersonaOffHint.Visibility = personaOn ? Visibility.Collapsed : Visibility.Visible;

        // Image section: hidden when persona is active
        ImageSettingsPanel.Visibility = personaOn ? Visibility.Collapsed : Visibility.Visible;
        ImageHiddenNote.Visibility = personaOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshPersonaStatus()
    {
        bool exists = PersonaStore.Exists();
        if (exists)
        {
            var data = PersonaStore.Load();
            var seedCount = data?.SeedLines?.Count ?? 0;
            var name = data?.Name ?? "설정됨";
            PersonaStatusBadge.Background = (System.Windows.Media.Brush)FindResource("SuccessLightBrush");
            PersonaStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            PersonaStatusText.Text = $"\u2713 {name} \u00B7 대사 {seedCount}개";
        }
        else
        {
            PersonaStatusBadge.Background = (System.Windows.Media.Brush)FindResource("MutedBrush");
            PersonaStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            PersonaStatusText.Text = "미설정 — 아래 편집 버튼으로 설정하세요";
        }
    }

    private void OnToggleStateImages(object sender, RoutedEventArgs e)
    {
        bool expanding = StateImagePanel.Visibility != Visibility.Visible;
        StateImagePanel.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;
        ToggleImagesBtn.Content = expanding ? "\u25BC 상태별 이미지 접기" : "\u25B6 상태별 이미지 펼치기";
    }

    private void OnToggleApiSettings(object sender, RoutedEventArgs e)
    {
        bool expanding = ApiSettingsPanel.Visibility != Visibility.Visible;
        ApiSettingsPanel.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;
        ToggleApiBtn.Content = expanding ? "\u25BC 대사 생성 API 접기" : "\u25B6 대사 생성 API 설정";
    }

    private static DebugWindow? _debugWindow;

    private void OnDebugModeChanged(object sender, RoutedEventArgs e)
    {
        if (DebugModeCheck.IsChecked == true)
        {
            if (_debugWindow == null || !_debugWindow.IsLoaded)
            {
                _debugWindow = new DebugWindow();
                _debugWindow.Show();
            }
            else
            {
                _debugWindow.Activate();
            }
        }
        else
        {
            _debugWindow?.Close();
            _debugWindow = null;
        }
    }

    private void UpdateApiStatus()
    {
        bool hasKey = !string.IsNullOrWhiteSpace(GetApiKey());
        if (hasKey)
        {
            ApiStatusIndicator.Text = "\u2713 설정됨";
            ApiStatusIndicator.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
        }
        else
        {
            ApiStatusIndicator.Text = "미설정";
            ApiStatusIndicator.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            ApiSettingsPanel.Visibility = Visibility.Visible;
            ToggleApiBtn.Content = "\u25BC 대사 생성 API 접기";
        }
    }

    private string GetApiKey()
    {
        return ApiKeyVisible.Visibility == Visibility.Visible
            ? ApiKeyVisible.Text?.Trim() ?? ""
            : ApiKeyMasked.Password?.Trim() ?? "";
    }

    private void OnToggleApiKeyClick(object sender, RoutedEventArgs e)
    {
        if (ApiKeyVisible.Visibility == Visibility.Collapsed)
        {
            ApiKeyVisible.Text = ApiKeyMasked.Password;
            ApiKeyVisible.Visibility = Visibility.Visible;
            ApiKeyMasked.Visibility = Visibility.Collapsed;
            ApiKeyToggleBtn.Content = "숨김";
        }
        else
        {
            ApiKeyMasked.Password = ApiKeyVisible.Text;
            ApiKeyMasked.Visibility = Visibility.Visible;
            ApiKeyVisible.Visibility = Visibility.Collapsed;
            ApiKeyToggleBtn.Content = "표시";
        }
    }

    private async void OnDiscoverModelsClick(object sender, RoutedEventArgs e)
    {
        var key = GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("API 키를 먼저 입력해주세요.", "모델 검색", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ModelCombo.Items.Clear();
        ModelCombo.IsEnabled = false;

        var models = await ApiDialogueSuggestionService.DiscoverAvailableModelsAsync(key,
            progress => Dispatcher.Invoke(() =>
            {
                ModelCombo.Items.Clear();
                ModelCombo.Items.Add(progress);
                ModelCombo.SelectedIndex = 0;
            }));

        ModelCombo.Items.Clear();
        ModelCombo.IsEnabled = true;

        if (models.Count == 0)
        {
            ModelCombo.Items.Add("(사용 가능한 모델 없음)");
            ModelCombo.SelectedIndex = 0;
            return;
        }

        int defaultIdx = 0;
        var current = _settings.SuggestionApiModel;
        for (int i = 0; i < models.Count; i++)
        {
            var (name, recommended) = models[i];
            var display = recommended ? $"{name} (추천)" : name;
            ModelCombo.Items.Add(new ComboBoxItem { Content = display, Tag = name });
            if (name == current) defaultIdx = i;
        }
        ModelCombo.SelectedIndex = defaultIdx;
    }

    private async void OnTestApiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("API 키를 입력해주세요.", "테스트", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var model = ModelCombo.SelectedItem is ComboBoxItem ci ? ci.Tag as string : ModelCombo.SelectedItem as string;
        var service = new ApiDialogueSuggestionService(key, model: model);
        var error = await service.TestConnectionAsync();

        if (error == null)
        {
            MessageBox.Show("연결 성공!", "테스트", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"연결 실패:\n{error}", "테스트", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnOpenAiStudioSettingsClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://aistudio.google.com/apikey") { UseShellExecute = true }); }
        catch { }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is ComboBoxItem item && item.Tag is IdlePreset preset)
        {
            _settings.IdlePreset = preset;
            if (preset == IdlePreset.Custom && int.TryParse(CustomMinutesBox.Text, out int mins) && mins > 0)
                _settings.CustomIdleMinutes = mins;
        }

        _settings.PetSize = SizeSlider.Value;
        _settings.PetImagePath = _defaultPath;
        _settings.HappyImagePath = _happyPath;
        _settings.IdleImagePath = _idlePath;
        _settings.AnnoyedImagePath = _annoyedPath;
        _settings.SleepingImagePath = _sleepingPath;
        _settings.WakeUpImagePath = _wakeUpPath;
        _settings.GrowWhenAnnoyed = GrowCheck.IsChecked == true;
        _settings.MaxGrowScale = GrowSlider.Value;

        var processName = TargetProcessBox.Text.Trim();
        if (!string.IsNullOrEmpty(processName))
            _settings.TargetProcessName = processName;

        _settings.StartWithWindows = AutoStartCheck.IsChecked == true;
        _settings.NoRestMode = NoRestCheck.IsChecked == true;
        _settings.DebugMode = DebugModeCheck.IsChecked == true;
        _settings.PersonaEnabled = PersonaEnabledCheck.IsChecked == true;

        // API 설정
        var apiKey = GetApiKey();
        _settings.SuggestionApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        string? selectedModel = ModelCombo.SelectedItem is ComboBoxItem ci ? ci.Tag as string : ModelCombo.SelectedItem as string;
        if (!string.IsNullOrEmpty(selectedModel) && !selectedModel.StartsWith("("))
            _settings.SuggestionApiModel = selectedModel;

        AutoStartHelper.SetAutoStart(_settings.StartWithWindows);

        _settings.Save();
        DialogResult = true;
    }
}
