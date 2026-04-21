using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JangJang.Core;
using JangJang.Core.Persona;
using JangJang.Core.Persona.Embedding;
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

    /// <summary>PersonaItemsControl에 바인딩되는 행 목록.</summary>
    private readonly ObservableCollection<PersonaListItem> _personaItems = new();

    /// <summary>창을 열 때의 페르소나 목록 스냅샷. Save 대신 Cancel/X 닫기 시 롤백에 사용.</summary>
    private readonly List<string> _originalRegisteredIds;
    private readonly string? _originalActiveId;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        _originalRegisteredIds = new List<string>(settings.RegisteredPersonaIds);
        _originalActiveId = settings.ActivePersonaId;
        InitializeComponent();
        Closing += OnWindowClosing;

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

        // 대사 교체 주기: 슬라이더 로드. Clamp된 값을 초기값으로 사용해 극단 저장 값 자동 보정.
        DialogueIntervalSlider.Value = settings.DialogueIntervalSecondsClamped;
        UpdateDialogueIntervalLabel();

        // API 설정 초기화
        ApiKeyMasked.Password = settings.SuggestionApiKeyDecrypted ?? string.Empty;
        ApiKeyVisible.Text = settings.SuggestionApiKeyDecrypted ?? string.Empty;
        if (!string.IsNullOrEmpty(settings.SuggestionApiModel))
            ModelCombo.Items.Add(settings.SuggestionApiModel);
        ModelCombo.Text = settings.SuggestionApiModel ?? "";

        // 페르소나 모드 → 프로그레시브 디스클로저
        PersonaEnabledCheck.Checked += (_, _) => UpdatePersonaVisibility();
        PersonaEnabledCheck.Unchecked += (_, _) => UpdatePersonaVisibility();
        UpdatePersonaVisibility();

        PersonaItemsControl.ItemsSource = _personaItems;
        RefreshPersonaList();
        UpdateApiStatus();

        // 임베딩 매칭: 저장된 설정을 먼저 반영한 뒤 모델 상태로 보정
        EmbeddingMatchingCheck.IsChecked = settings.EmbeddingMatchingEnabled;
        UpdateEmbeddingModelStatus();

        RefreshPreviews();
    }

    // --- 페르소나 목록 관리 ---

    /// <summary>
    /// 등록된 페르소나 Id 목록을 읽어 실제 데이터와 결합해 리스트를 갱신.
    /// 데이터가 손상/부재한 항목은 이름 없이 "(불러오기 실패)"로 표시한다 (등록만 되고 파일이 없는 경우 등).
    /// </summary>
    private void RefreshPersonaList()
    {
        _personaItems.Clear();

        var activeId = _settings.ActivePersonaId;
        foreach (var id in _settings.RegisteredPersonaIds)
        {
            var data = PersonaStore.Load(id);
            var isActive = string.Equals(id, activeId, StringComparison.Ordinal);
            _personaItems.Add(new PersonaListItem
            {
                Id = id,
                DisplayName = string.IsNullOrWhiteSpace(data?.Name) ? "(이름 없음)" : data.Name,
                SubText = data == null
                    ? "(파일을 찾을 수 없음 · 제거 가능)"
                    : $"대사 {data.SeedLines.Count}개",
                IsActive = isActive
            });
        }

        PersonaEmptyHint.Visibility = _personaItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNewPersonaClick(object sender, RoutedEventArgs e)
    {
        // PersonaWindow 내부에서 이미 persona.json이 디스크에 저장된다.
        // SettingsWindow의 목록(RegisteredPersonaIds)만 메모리에서 갱신 — 최종 persist는 OnSaveClick.
        var win = new PersonaWindow(null) { Owner = this };
        var ok = win.ShowDialog();
        if (ok == true && !string.IsNullOrEmpty(win.SavedPersonaId))
        {
            if (!_settings.RegisteredPersonaIds.Contains(win.SavedPersonaId))
                _settings.RegisteredPersonaIds.Add(win.SavedPersonaId);
            // 첫 페르소나면 자동 활성화
            if (string.IsNullOrEmpty(_settings.ActivePersonaId))
                _settings.ActivePersonaId = win.SavedPersonaId;
            RefreshPersonaList();
        }
    }

    private void OnImportPersonaClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "불러올 persona.json 선택",
            Filter = "persona.json|persona.json|모든 파일|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var folder = Path.GetDirectoryName(dlg.FileName);
        if (string.IsNullOrEmpty(folder))
        {
            MessageBox.Show("폴더를 확인할 수 없습니다.", "페르소나 불러오기",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 디스크에는 새 폴더가 복사되지만, 설정(RegisteredPersonaIds) 반영은 Save 시점에 확정.
        var newId = PersonaStore.ImportFromFolder(folder);
        if (string.IsNullOrEmpty(newId))
        {
            MessageBox.Show("persona.json을 읽지 못했습니다.", "페르소나 불러오기",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_settings.RegisteredPersonaIds.Contains(newId))
            _settings.RegisteredPersonaIds.Add(newId);
        if (string.IsNullOrEmpty(_settings.ActivePersonaId))
            _settings.ActivePersonaId = newId;
        RefreshPersonaList();
    }

    private void OnActivatePersonaClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        _settings.ActivePersonaId = id;
        RefreshPersonaList();
    }

    private void OnEditPersonaItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var data = PersonaStore.Load(id);
        if (data == null)
        {
            MessageBox.Show("페르소나 데이터를 읽을 수 없습니다. 제거 후 다시 추가하세요.",
                "페르소나 편집", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var win = new PersonaWindow(data) { Owner = this };
        if (win.ShowDialog() == true)
            RefreshPersonaList();
    }

    private void OnRemovePersonaClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var item = _personaItems.FirstOrDefault(p => p.Id == id);
        var name = item?.DisplayName ?? id;

        var result = MessageBox.Show(
            $"\"{name}\" 을(를) 목록에서 제거합니다.\n(실제 파일은 유지됩니다.)",
            "페르소나 제거",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        _settings.RegisteredPersonaIds.Remove(id);
        if (string.Equals(_settings.ActivePersonaId, id, StringComparison.Ordinal))
        {
            // 제거된 항목이 활성이면 남은 첫 항목으로 자동 전환, 없으면 비활성.
            _settings.ActivePersonaId = _settings.RegisteredPersonaIds.Count > 0
                ? _settings.RegisteredPersonaIds[0]
                : null;
        }
        RefreshPersonaList();
    }

    /// <summary>
    /// 창이 "저장"(DialogResult=true) 외 경로로 닫히면 페르소나 목록 변경을 스냅샷으로 복구.
    /// 디스크에 이미 새 폴더가 생성된 경우(신규/불러오기)에도 설정에 등록되지 않으므로 앱에서는 보이지 않는다.
    /// </summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DialogResult == true) return;

        _settings.RegisteredPersonaIds.Clear();
        _settings.RegisteredPersonaIds.AddRange(_originalRegisteredIds);
        _settings.ActivePersonaId = _originalActiveId;
    }

    private void OnDialogueIntervalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateDialogueIntervalLabel();
    }

    private void UpdateDialogueIntervalLabel()
    {
        if (DialogueIntervalLabel == null) return;
        DialogueIntervalLabel.Text = $"{(int)Math.Round(DialogueIntervalSlider.Value)}초";
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

    private void UpdateEmbeddingModelStatus()
    {
        bool installed = EmbeddingModelLocator.IsModelInstalled();
        if (installed)
        {
            EmbeddingStatusText.Text = "설치됨 ✓";
            EmbeddingStatusBadge.Background = (System.Windows.Media.Brush)FindResource("SuccessLightBrush");
            EmbeddingStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            EmbeddingMatchingCheck.IsEnabled = true;
            EmbeddingHelpInstalled.Visibility = Visibility.Visible;
            EmbeddingHelpMissing.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmbeddingStatusText.Text = "미설치 ✗";
            EmbeddingStatusBadge.Background = System.Windows.Media.Brushes.Transparent;
            EmbeddingStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            EmbeddingMatchingCheck.IsEnabled = false;
            EmbeddingMatchingCheck.IsChecked = false;  // 모델 없으면 강제 off
            EmbeddingHelpInstalled.Visibility = Visibility.Collapsed;
            EmbeddingHelpMissing.Visibility = Visibility.Visible;
        }
    }

    private void OnOpenEmbeddingFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = EmbeddingModelLocator.GetStandardPath();
            Directory.CreateDirectory(folder);  // 배치 편의를 위해 없으면 미리 생성
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch { }
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

        try
        {
            var models = await ApiDialogueSuggestionService.DiscoverAvailableModelsAsync(key,
                progress => Dispatcher.Invoke(() =>
                {
                    ModelCombo.Items.Clear();
                    ModelCombo.Items.Add(progress);
                    ModelCombo.SelectedIndex = 0;
                }));

            ModelCombo.Items.Clear();

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
        catch (Exception ex)
        {
            ModelCombo.Items.Clear();
            ModelCombo.Items.Add($"(오류: {ex.Message})");
            ModelCombo.SelectedIndex = 0;
        }
        finally
        {
            ModelCombo.IsEnabled = true;
        }
    }

    private async void OnTestApiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("API 키를 입력해주세요.", "테스트", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var model = ModelCombo.SelectedItem is ComboBoxItem ci ? ci.Tag as string : ModelCombo.SelectedItem as string;
            var service = new ApiDialogueSuggestionService(key, model: model);
            var error = await service.TestConnectionAsync();

            if (error == null)
                MessageBox.Show("연결 성공!", "테스트", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show($"연결 실패:\n{error}", "테스트", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"테스트 실패:\n{ex.Message}", "테스트", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        _settings.DialogueIntervalSeconds = (int)Math.Round(DialogueIntervalSlider.Value);
        _settings.PersonaEnabled = PersonaEnabledCheck.IsChecked == true;
        _settings.EmbeddingMatchingEnabled = EmbeddingMatchingCheck.IsChecked == true;

        // API 설정
        var apiKey = GetApiKey();
        _settings.SuggestionApiKeyDecrypted = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        string? selectedModel = ModelCombo.SelectedItem is ComboBoxItem ci ? ci.Tag as string : ModelCombo.SelectedItem as string;
        if (!string.IsNullOrEmpty(selectedModel) && !selectedModel.StartsWith("("))
            _settings.SuggestionApiModel = selectedModel;

        AutoStartHelper.SetAutoStart(_settings.StartWithWindows);

        _settings.Save();
        DialogResult = true;
    }
}

/// <summary>
/// SettingsWindow의 페르소나 목록 항목 ViewModel.
/// 불변 POCO + 계산 프로퍼티. 리스트가 통째로 갱신되므로 INotifyPropertyChanged 불필요.
/// </summary>
public sealed class PersonaListItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SubText { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public Visibility ActiveBadgeVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public bool CanActivate => !IsActive;
    public System.Windows.Media.Brush RowBackground => IsActive
        ? new SolidColorBrush(Color.FromArgb(0x1F, 0x22, 0xC5, 0x5E))
        : System.Windows.Media.Brushes.Transparent;
}
