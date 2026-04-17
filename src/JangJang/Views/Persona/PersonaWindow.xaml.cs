using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using JangJang.Core;
using JangJang.Core.Persona;
using JangJang.Core.Persona.Feedback;
using JangJang.Core.Persona.Preset;
using JangJang.Core.Persona.Suggestion;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace JangJang.Views.Persona;

public partial class PersonaWindow : Window
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly ObservableCollection<SeedLine> _allSeeds = new();
    private readonly ObservableCollection<SeedLine> _filteredSeeds = new();
    private readonly List<PersonaPreset> _presets;

    private string? _newPortraitSourcePath;
    private string? _currentPortraitFileName;
    private bool _portraitCleared;

    private string? _selectedPresetId;
    private PetState _currentState = PetState.Happy;
    private readonly ToggleButton[] _stateButtons;

    private readonly AppSettings _settings;

    public PersonaWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _presets = PresetStore.LoadAll();

        SeedListBox.ItemsSource = _filteredSeeds;

        BuildPresetButtons();
        _stateButtons = BuildStateButtons();
        BuildMoveTargetCombo();

        LoadExistingPersona();
        RefreshPortraitPreview();
        FilterSeedsByState();
        UpdateSaveButtonEnabled();
    }

    // --- 프리셋 ---

    private void BuildPresetButtons()
    {
        foreach (var preset in _presets)
        {
            var btn = new ToggleButton
            {
                Content = preset.DisplayName,
                Tag = preset.Id,
                ToolTip = preset.ToneDescription,
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12
            };
            btn.Click += OnPresetClick;
            PresetPanel.Children.Add(btn);
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;
        var presetId = clicked.Tag as string;

        // 토글 해제: 다른 버튼 해제
        foreach (var child in PresetPanel.Children)
        {
            if (child is ToggleButton tb && tb != clicked)
                tb.IsChecked = false;
        }

        if (clicked.IsChecked == true && presetId != null)
        {
            var preset = _presets.Find(p => p.Id == presetId);
            if (preset == null) return;

            // 기존 사용자 대사가 있으면 확인
            var userLines = _allSeeds.Where(s => s.Source != SeedLineSource.Preset).ToList();
            if (userLines.Count > 0 && _selectedPresetId != presetId)
            {
                var result = MessageBox.Show(
                    "프리셋 대사를 불러올까요? 직접 작성한 대사는 유지됩니다.",
                    "프리셋 변경", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    clicked.IsChecked = false;
                    return;
                }
            }

            ApplyPreset(preset);
        }
        else
        {
            _selectedPresetId = null;
        }

        UpdateSaveButtonEnabled();
    }

    private void ApplyPreset(PersonaPreset preset)
    {
        _selectedPresetId = preset.Id;

        // 기존 프리셋 대사 제거 (사용자 대사는 유지)
        var userLines = _allSeeds.Where(s => s.Source != SeedLineSource.Preset).ToList();
        _allSeeds.Clear();

        // 프리셋 대사 추가
        foreach (var ps in preset.SeedLines)
        {
            _allSeeds.Add(new SeedLine
            {
                Text = ps.Text,
                SituationDescription = ps.SituationDescription,
                State = ps.State,
                Source = SeedLineSource.Preset,
                CreatedAt = DateTime.UtcNow
            });
        }

        // 사용자 대사 복원
        foreach (var line in userLines)
            _allSeeds.Add(line);

        FilterSeedsByState();
    }

    // --- 상태 탭 ---

    private static string GetKoreanStateName(PetState state) => state switch
    {
        PetState.Happy => "작업 중",
        PetState.Alert => "경계",
        PetState.Annoyed => "화남",
        PetState.Sleeping => "수면",
        PetState.WakeUp => "대기",
        _ => state.ToString()
    };

    private ToggleButton[] BuildStateButtons()
    {
        var states = new[] { PetState.Happy, PetState.Alert, PetState.Annoyed, PetState.Sleeping, PetState.WakeUp };

        var buttons = new ToggleButton[states.Length];
        for (int i = 0; i < states.Length; i++)
        {
            var state = states[i];
            var btn = new ToggleButton
            {
                Content = GetKoreanStateName(state),
                Tag = state,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                IsChecked = state == _currentState
            };
            btn.Click += OnStateTabClick;
            StateTabPanel.Children.Add(btn);
            buttons[i] = btn;
        }
        return buttons;
    }

    private void RefreshStateButtonCounts()
    {
        foreach (var btn in _stateButtons)
        {
            if (btn.Tag is PetState state)
            {
                var count = _allSeeds.Count(s => s.State == state);
                var name = GetKoreanStateName(state);
                btn.Content = count > 0 ? $"{name} ({count})" : name;

                // Tooltip: first 3 dialogues preview
                var lines = _allSeeds.Where(s => s.State == state).Take(3).Select(s => s.Text).ToList();
                if (lines.Count > 0)
                {
                    var preview = string.Join("\n", lines.Select(l =>
                        $"\u2022 {(l.Length > 25 ? l[..25] + "..." : l)}"));
                    if (count > 3) preview += $"\n  외 {count - 3}개";
                    btn.ToolTip = preview;
                }
                else
                {
                    btn.ToolTip = "(대사 없음)";
                }
            }
        }
    }

    private void OnStateTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked || clicked.Tag is not PetState state) return;

        _currentState = state;
        foreach (var btn in _stateButtons)
            btn.IsChecked = btn.Tag is PetState s && s == state;

        FilterSeedsByState();
    }

    private void FilterSeedsByState()
    {
        _filteredSeeds.Clear();
        foreach (var seed in _allSeeds.Where(s => s.State == _currentState))
            _filteredSeeds.Add(seed);

        DialogueBox.Clear();
        SituationBox.Clear();
        RefreshStateButtonCounts();
        UpdateEmptyState();
        UpdateSelectionControls();
    }

    private void UpdateEmptyState()
    {
        if (EmptyStatePanel == null) return;
        EmptyStatePanel.Visibility = _filteredSeeds.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSelectionControls()
    {
        if (SelectionActionsPanel == null) return;
        bool hasSelection = SeedListBox.SelectedItem != null;
        SelectionActionsPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        EditAreaLabel.Text = hasSelection ? "대사 편집" : "새 대사 입력";
    }

    // --- 초상화 ---

    private void OnSelectPortraitClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "초상화 이미지 선택",
            Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.gif;*.bmp|모든 파일|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        _newPortraitSourcePath = dlg.FileName;
        _portraitCleared = false;
        RefreshPortraitPreview();
        UpdateSaveButtonEnabled();
    }

    private void OnClearPortraitClick(object sender, RoutedEventArgs e)
    {
        _newPortraitSourcePath = null;
        _portraitCleared = true;
        RefreshPortraitPreview();
        UpdateSaveButtonEnabled();
    }

    private void RefreshPortraitPreview()
    {
        string? pathToShow = null;

        if (!_portraitCleared)
        {
            if (!string.IsNullOrEmpty(_newPortraitSourcePath) && File.Exists(_newPortraitSourcePath))
                pathToShow = _newPortraitSourcePath;
            else if (!string.IsNullOrEmpty(_currentPortraitFileName))
            {
                var existing = Path.Combine(PersonaStore.CurrentDir, _currentPortraitFileName);
                if (File.Exists(existing)) pathToShow = existing;
            }
        }

        if (pathToShow != null)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(pathToShow, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                PreviewPortrait.Source = bmp;
                PortraitPlaceholder.Visibility = Visibility.Collapsed;
                return;
            }
            catch { }
        }

        PreviewPortrait.Source = null;
        PortraitPlaceholder.Visibility = Visibility.Visible;
    }

    // --- 대사 편집 ---

    private void OnSeedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeedListBox.SelectedItem is SeedLine line)
        {
            DialogueBox.Text = line.Text;
            SituationBox.Text = line.SituationDescription ?? string.Empty;
        }
        UpdateSelectionControls();
    }

    private void OnAddSeedClick(object sender, RoutedEventArgs e)
    {
        var text = DialogueBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("대사 내용을 입력해주세요.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var situation = SituationBox.Text?.Trim();
        var newLine = new SeedLine
        {
            Text = text,
            SituationDescription = string.IsNullOrEmpty(situation) ? null : situation,
            State = _currentState,
            Source = SeedLineSource.UserWritten,
            CreatedAt = DateTime.UtcNow
        };

        _allSeeds.Add(newLine);
        _filteredSeeds.Add(newLine);

        DialogueBox.Clear();
        SituationBox.Clear();
        RefreshStateButtonCounts();
        UpdateEmptyState();
        UpdateSaveButtonEnabled();
    }

    private void OnUpdateSeedClick(object sender, RoutedEventArgs e)
    {
        if (SeedListBox.SelectedItem is not SeedLine selected)
        {
            MessageBox.Show("수정할 대사를 선택해주세요.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var text = DialogueBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("대사 내용을 입력해주세요.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var situation = SituationBox.Text?.Trim();
        selected.Text = text;
        selected.SituationDescription = string.IsNullOrEmpty(situation) ? null : situation;

        // ObservableCollection 갱신 트리거
        var idx = _filteredSeeds.IndexOf(selected);
        if (idx >= 0)
        {
            _filteredSeeds.RemoveAt(idx);
            _filteredSeeds.Insert(idx, selected);
            SeedListBox.SelectedIndex = idx;
        }

        UpdateSaveButtonEnabled();
    }

    private void OnDeleteSeedClick(object sender, RoutedEventArgs e)
    {
        if (SeedListBox.SelectedItem is not SeedLine selected)
        {
            MessageBox.Show("삭제할 대사를 선택해주세요.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _allSeeds.Remove(selected);
        _filteredSeeds.Remove(selected);
        DialogueBox.Clear();
        SituationBox.Clear();
        RefreshStateButtonCounts();
        UpdateEmptyState();
        UpdateSelectionControls();
        UpdateSaveButtonEnabled();
    }

    // --- 상태 이동 ---

    private void BuildMoveTargetCombo()
    {
        foreach (var state in Enum.GetValues<PetState>())
            MoveTargetCombo.Items.Add(new ComboBoxItem { Content = GetKoreanStateName(state), Tag = state });
        MoveTargetCombo.SelectedIndex = 0;
    }

    private void OnMoveSeedClick(object sender, RoutedEventArgs e)
    {
        if (SeedListBox.SelectedItem is not SeedLine selected)
        {
            MessageBox.Show("이동할 대사를 선택해주세요.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MoveTargetCombo.SelectedItem is not ComboBoxItem item || item.Tag is not PetState targetState)
            return;

        if (targetState == _currentState)
        {
            MessageBox.Show("현재 상태와 같습니다.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        selected.State = targetState;
        _filteredSeeds.Remove(selected);
        DialogueBox.Clear();
        SituationBox.Clear();
        RefreshStateButtonCounts();
        UpdateEmptyState();
        UpdateSelectionControls();
    }

    // --- AI 추천 ---

    private async void OnAiSuggestClick(object sender, RoutedEventArgs e)
    {
        // API 키 확인
        if (string.IsNullOrWhiteSpace(_settings.SuggestionApiKeyDecrypted))
        {
            SuggestionPanel.Visibility = Visibility.Visible;
            SuggestionPanel.BringIntoView();
            SuggestionList.Items.Clear();
            ApiKeyPrompt.Visibility = Visibility.Visible;
            return;
        }

        var service = ApiDialogueSuggestionService.FromSettings(_settings);
        if (service == null) return;

        // 컨텍스트 구성
        var preset = _presets.Find(p => p.Id == _selectedPresetId);
        var context = new SuggestionContext
        {
            PresetDescription = preset?.ToneDescription ?? string.Empty,
            PersonalityKeywords = preset?.PersonalityKeywords ?? new(),
            CustomNotes = PersonalityNotesBox.Text?.Trim(),
            TargetState = _currentState,
            ExistingLines = BuildExistingLinesMap(),
            RecentFeedback = FeedbackStore.Load()
        };

        AiSuggestButton.IsEnabled = false;
        AiSuggestButton.Content = "생성 중...";
        SuggestionPanel.Visibility = Visibility.Visible;
        SuggestionPanel.BringIntoView();
        SuggestionList.Items.Clear();
        ApiKeyPrompt.Visibility = Visibility.Collapsed;

        try
        {
            var suggestions = await service.SuggestAsync(context, 3);

            if (suggestions.Count == 0)
            {
                SuggestionList.Items.Add(new TextBlock { Text = "추천 결과가 없습니다.", Foreground = System.Windows.Media.Brushes.Gray });
            }
            else
            {
                foreach (var suggestion in suggestions)
                    AddSuggestionItem(suggestion.Text);
            }
        }
        catch (Exception ex)
        {
            SuggestionList.Items.Add(new TextBlock
            {
                Text = $"오류: {ex.Message}",
                Foreground = System.Windows.Media.Brushes.Red,
                TextWrapping = TextWrapping.Wrap
            });
        }
        finally
        {
            AiSuggestButton.IsEnabled = true;
            AiSuggestButton.Content = "대사 생성";
        }
    }

    private void AddSuggestionItem(string text)
    {
        var panel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 3)
        };

        var label = new TextBlock
        {
            Text = $"\u201C{text}\u201D",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 12
        };

        var acceptBtn = new System.Windows.Controls.Button
        {
            Content = "수락",
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 4, 0),
            FontSize = 11
        };
        acceptBtn.Click += (_, _) => AcceptSuggestion(text, text, FeedbackType.Accepted);

        var editBtn = new System.Windows.Controls.Button
        {
            Content = "편집",
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 4, 0),
            FontSize = 11
        };
        editBtn.Click += (_, _) =>
        {
            DialogueBox.Text = text;
            SituationBox.Clear();
        };

        var rejectBtn = new System.Windows.Controls.Button
        {
            Content = "거절",
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 11
        };
        rejectBtn.Click += (_, _) =>
        {
            FeedbackStore.Append(new DialogueFeedback
            {
                OriginalText = text,
                Type = FeedbackType.Rejected,
                State = _currentState,
                Timestamp = DateTime.UtcNow
            });
            panel.Visibility = Visibility.Collapsed;
        };

        panel.Children.Add(label);
        panel.Children.Add(acceptBtn);
        panel.Children.Add(editBtn);
        panel.Children.Add(rejectBtn);
        SuggestionList.Items.Add(panel);
    }

    private void AcceptSuggestion(string originalText, string finalText, FeedbackType type)
    {
        var newLine = new SeedLine
        {
            Text = finalText,
            State = _currentState,
            Source = type == FeedbackType.Accepted ? SeedLineSource.AiSuggested : SeedLineSource.AiEdited,
            CreatedAt = DateTime.UtcNow
        };

        _allSeeds.Add(newLine);
        _filteredSeeds.Add(newLine);

        FeedbackStore.Append(new DialogueFeedback
        {
            OriginalText = originalText,
            EditedText = type == FeedbackType.Edited ? finalText : null,
            Type = type,
            State = _currentState,
            Timestamp = DateTime.UtcNow
        });

        RefreshStateButtonCounts();
        UpdateEmptyState();
        UpdateSaveButtonEnabled();
    }

    private void OnCloseSuggestionClick(object sender, RoutedEventArgs e)
    {
        SuggestionPanel.Visibility = Visibility.Collapsed;
    }

    private void OnOpenAiStudioClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://aistudio.google.com/apikey") { UseShellExecute = true }); }
        catch { }
    }

    private void OnSaveInlineApiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = InlineApiKeyBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key)) return;

        _settings.SuggestionApiKeyDecrypted = key;
        _settings.Save();
        ApiKeyPrompt.Visibility = Visibility.Collapsed;
        MessageBox.Show("API 키가 저장되었습니다.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private Dictionary<PetState, List<string>> BuildExistingLinesMap()
    {
        var map = new Dictionary<PetState, List<string>>();
        foreach (var state in Enum.GetValues<PetState>())
        {
            var lines = _allSeeds.Where(s => s.State == state).Select(s => s.Text).ToList();
            if (lines.Count > 0)
                map[state] = lines;
        }
        return map;
    }

    // --- 로드 ---

    private void LoadExistingPersona()
    {
        var data = PersonaStore.Load();
        if (data == null) return;

        NameBox.Text = data.Name ?? string.Empty;
        _currentPortraitFileName = data.PortraitFileName;
        PersonalityNotesBox.Text = data.CustomPersonalityNotes ?? string.Empty;

        // 설정 복원
        // 설정 복원 완료

        // 프리셋 선택 복원
        if (!string.IsNullOrEmpty(data.PresetId))
        {
            _selectedPresetId = data.PresetId;
            foreach (var child in PresetPanel.Children)
            {
                if (child is ToggleButton tb && tb.Tag as string == data.PresetId)
                    tb.IsChecked = true;
            }
        }

        // 대사 로드
        foreach (var line in data.SeedLines)
            _allSeeds.Add(line);
    }

    // --- 유효성 ---

    private void OnAnyInputChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSaveButtonEnabled();
    }

    private void UpdateSaveButtonEnabled()
    {
        if (SaveButton == null) return;

        var hasName = !string.IsNullOrWhiteSpace(NameBox.Text);
        var hasPortrait = HasUsablePortrait();
        var hasSeed = _allSeeds.Count > 0;
        SaveButton.IsEnabled = hasName && hasPortrait && hasSeed;

        // Inline validation feedback
        if (ValidationMessage != null)
        {
            if (SaveButton.IsEnabled)
            {
                ValidationMessage.Visibility = Visibility.Collapsed;
            }
            else
            {
                string msg;
                if (!hasName) msg = "이름을 입력해주세요.";
                else if (!hasPortrait) msg = "초상화를 선택해주세요.";
                else msg = "대사를 1개 이상 추가해주세요.";

                ValidationMessage.Text = msg;
                ValidationMessage.Visibility = Visibility.Visible;
            }
        }
    }

    private bool HasUsablePortrait()
    {
        if (_portraitCleared) return false;
        if (!string.IsNullOrEmpty(_newPortraitSourcePath) && File.Exists(_newPortraitSourcePath)) return true;
        if (!string.IsNullOrEmpty(_currentPortraitFileName))
        {
            var p = Path.Combine(PersonaStore.CurrentDir, _currentPortraitFileName);
            if (File.Exists(p)) return true;
        }
        return false;
    }

    // --- 저장/취소 ---

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        string portraitFileName;
        try
        {
            if (!string.IsNullOrEmpty(_newPortraitSourcePath))
                portraitFileName = PersonaStore.CopyPortrait(_newPortraitSourcePath);
            else if (!_portraitCleared && !string.IsNullOrEmpty(_currentPortraitFileName))
                portraitFileName = _currentPortraitFileName!;
            else
            {
                MessageBox.Show("초상화가 필요합니다.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"초상화 파일 복사 실패: {ex.Message}", "자캐 페르소나",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var data = new PersonaData
        {
            Name = NameBox.Text.Trim(),
            PortraitFileName = portraitFileName,
            PresetId = _selectedPresetId,
            CustomToneDescription = GetSelectedPreset()?.ToneDescription,
            CustomPersonalityNotes = PersonalityNotesBox.Text?.Trim(),
            SeedLines = new List<SeedLine>(_allSeeds)
        };

        PersonaStore.Save(data);
        DialogResult = true;
    }

    private PersonaPreset? GetSelectedPreset() => _presets.Find(p => p.Id == _selectedPresetId);

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    // --- 헬퍼 ---

    private static string? GetComboTag(System.Windows.Controls.ComboBox combo)
        => combo.SelectedItem is ComboBoxItem ci ? ci.Tag as string : null;

    private static void RestoreComboSelection(System.Windows.Controls.ComboBox combo, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem ci && ci.Tag as string == value)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    // --- 상황 자동 생성 ---

    private async void OnAutoGenerateSituation(object sender, RoutedEventArgs e)
    {
        var text = DialogueBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("대사를 먼저 입력해주세요.", "상황 생성", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.SuggestionApiKeyDecrypted))
        {
            MessageBox.Show("설정 창에서 Gemini API 키를 먼저 등록해주세요.", "상황 생성",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AutoSituationBtn.IsEnabled = false;
        var originalContent = AutoSituationBtn.Content;
        AutoSituationBtn.Content = "생성 중...";

        try
        {
            var situation = await GenerateSituationViaApi(text, _currentState);
            if (!string.IsNullOrEmpty(situation))
                SituationBox.Text = situation;
            else
                MessageBox.Show("생성 결과가 없습니다.", "상황 생성", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"생성 실패: {ex.Message}", "상황 생성", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            AutoSituationBtn.IsEnabled = true;
            AutoSituationBtn.Content = originalContent;
        }
    }

    private async Task<string?> GenerateSituationViaApi(string dialogueText, PetState state)
    {
        var key = _settings.SuggestionApiKeyDecrypted!;
        var model = _settings.SuggestionApiModel ?? "gemini-2.0-flash";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={key}";

        var stateName = GetKoreanStateName(state);
        var prompt =
            "데스크톱 펫이 사용자의 작업 상태를 보고 대사를 말한다.\n" +
            "대사가 주어지면, 그 대사가 나와야 할 \"사용자의 작업 상황\"을 써줘.\n" +
            "대사 내용을 반복하지 말고, 사용자가 뭘 하고 있는지를 묘사해.\n\n" +
            "예시:\n" +
            "대사: \"좋았어, 이 기세 그대로 가자!\" → 열심히 작업에 집중하고 있다. 이번 세션에 약 1시간 일했다.\n" +
            "대사: \"아 진짜... 답답해서 내가 대신 타이핑할까?\" → 작업을 한참 동안 안 하고 있어 답답하다. 오늘 이미 많은 작업을 했다.\n" +
            "대사: \"야야야, 딴짓 하려는 거 다 보여!\" → 잠깐 작업에서 한눈을 팔았다.\n" +
            "대사: \"드디어 왔구나? 기다렸다!\" → 막 작업을 다시 시작하려는 참이다.\n" +
            "대사: \"충분히 쉬었으면 돌아오세요.\" → 아직 작업을 시작하지 않았거나 자리를 비운 상태이다.\n\n" +
            $"대사: \"{dialogueText}\" →";

        var body = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };

        var json = JsonSerializer.Serialize(body);
        var httpContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, httpContent);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // 구조화된 API 에러 메시지 추출 시도
            try
            {
                using var errorDoc = JsonDocument.Parse(responseText);
                if (errorDoc.RootElement.TryGetProperty("error", out var apiErr) &&
                    apiErr.TryGetProperty("message", out var msg))
                    throw new InvalidOperationException(msg.GetString());
            }
            catch (JsonException) { }
            throw new HttpRequestException($"API 오류 ({(int)response.StatusCode})");
        }

        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return null;

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var contentObj))
            return null;
        if (!contentObj.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
            return null;

        return parts[0].GetProperty("text").GetString()?.Trim().Trim('"');
    }
}
