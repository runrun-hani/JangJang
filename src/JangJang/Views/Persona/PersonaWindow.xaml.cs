using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using JangJang.Core.Persona;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace JangJang.Views.Persona;

/// <summary>
/// 자캐 페르소나 편집 창. PersonaStore에서 현재 페르소나를 로드하거나,
/// 없으면 빈 상태에서 새로 작성한다. 저장 시 PersonaStore로 쓴다.
///
/// 편집 중인 초상화 파일 경로는 임시로 메모리에 들고 있다가, 저장 버튼을 누를 때
/// PersonaStore.CopyPortrait로 current/ 폴더에 복사된다.
/// 저장 완료 시 DialogResult=true로 닫힘 → 호출 측(SettingsWindow)이 반영.
/// </summary>
public partial class PersonaWindow : Window
{
    /// <summary>
    /// 편집 중인 씨앗 대사 임시 목록. SeedListBox의 ItemsSource로 바인딩된다.
    /// </summary>
    private readonly System.Collections.ObjectModel.ObservableCollection<SeedLine> _seeds = new();

    /// <summary>
    /// 사용자가 방금 선택한 새 초상화 파일의 절대 경로. 저장 시 PersonaStore로 복사됨.
    /// null이면 기존 초상화 유지 또는 제거.
    /// </summary>
    private string? _newPortraitSourcePath;

    /// <summary>현재 저장되어 있는 초상화 파일명(상대). 편집 시작 시 로드된 값 유지.</summary>
    private string? _currentPortraitFileName;

    /// <summary>사용자가 "제거"를 눌렀는지. true면 저장 시 PortraitFileName을 비움.</summary>
    private bool _portraitCleared;

    public PersonaWindow()
    {
        InitializeComponent();

        SeedListBox.ItemsSource = _seeds;

        LoadExistingPersona();
        RefreshPortraitPreview();
        UpdateSeedCount();
        UpdateSaveButtonEnabled();
    }

    private void LoadExistingPersona()
    {
        var data = PersonaStore.Load();
        if (data == null) return;

        NameBox.Text = data.Name ?? string.Empty;
        _currentPortraitFileName = data.PortraitFileName;
        foreach (var line in data.SeedLines)
        {
            _seeds.Add(new SeedLine
            {
                Text = line.Text,
                SituationDescription = line.SituationDescription,
                CreatedAt = line.CreatedAt == default ? DateTime.UtcNow : line.CreatedAt
            });
        }
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
            {
                pathToShow = _newPortraitSourcePath;
            }
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
            catch
            {
                // 이미지 로드 실패 시 placeholder로 폴백
            }
        }

        PreviewPortrait.Source = null;
        PortraitPlaceholder.Visibility = Visibility.Visible;
    }

    // --- 씨앗 대사 편집 ---

    private void OnSeedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeedListBox.SelectedItem is SeedLine line)
        {
            DialogueBox.Text = line.Text ?? string.Empty;
            SituationBox.Text = line.SituationDescription ?? string.Empty;
        }
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
        _seeds.Add(new SeedLine
        {
            Text = text,
            SituationDescription = string.IsNullOrEmpty(situation) ? null : situation,
            CreatedAt = DateTime.UtcNow
        });

        DialogueBox.Clear();
        SituationBox.Clear();
        UpdateSeedCount();
        UpdateSaveButtonEnabled();
    }

    private void OnUpdateSeedClick(object sender, RoutedEventArgs e)
    {
        if (SeedListBox.SelectedIndex < 0)
        {
            MessageBox.Show("수정할 대사를 목록에서 선택해주세요.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var text = DialogueBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("대사 내용을 입력해주세요.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var situation = SituationBox.Text?.Trim();
        var index = SeedListBox.SelectedIndex;
        // ObservableCollection 교체로 ListBox 갱신 트리거
        _seeds[index] = new SeedLine
        {
            Text = text,
            SituationDescription = string.IsNullOrEmpty(situation) ? null : situation,
            CreatedAt = _seeds[index].CreatedAt
        };
        SeedListBox.SelectedIndex = index;
        UpdateSaveButtonEnabled();
    }

    private void OnDeleteSeedClick(object sender, RoutedEventArgs e)
    {
        if (SeedListBox.SelectedIndex < 0)
        {
            MessageBox.Show("삭제할 대사를 목록에서 선택해주세요.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _seeds.RemoveAt(SeedListBox.SelectedIndex);
        DialogueBox.Clear();
        SituationBox.Clear();
        UpdateSeedCount();
        UpdateSaveButtonEnabled();
    }

    private void UpdateSeedCount()
    {
        SeedCountText.Text = $"({_seeds.Count}개)";
    }

    // --- 유효성 ---

    private void OnAnyInputChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSaveButtonEnabled();
    }

    /// <summary>
    /// 저장 버튼은 최소 요건 충족 시에만 활성화:
    ///   - 페르소나 이름 비어있지 않음
    ///   - 초상화가 새로 선택됐거나 기존 파일이 유효 (제거 상태 아님)
    ///   - 씨앗 대사 1개 이상
    /// </summary>
    private void UpdateSaveButtonEnabled()
    {
        if (SaveButton == null) return; // InitializeComponent 전 호출 방어

        var hasName = !string.IsNullOrWhiteSpace(NameBox.Text);
        var hasPortrait = HasUsablePortrait();
        var hasSeed = _seeds.Count > 0;
        SaveButton.IsEnabled = hasName && hasPortrait && hasSeed;
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
            {
                portraitFileName = PersonaStore.CopyPortrait(_newPortraitSourcePath);
            }
            else if (!_portraitCleared && !string.IsNullOrEmpty(_currentPortraitFileName))
            {
                portraitFileName = _currentPortraitFileName!;
            }
            else
            {
                MessageBox.Show("초상화가 필요합니다.", "자캐 페르소나", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"초상화 파일 복사에 실패했습니다: {ex.Message}", "자캐 페르소나",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var data = new PersonaData
        {
            Name = NameBox.Text.Trim(),
            PortraitFileName = portraitFileName,
            ToneHint = null, // (가) MVP: 말투 프리셋 UI 미노출. 필드는 유지
            SeedLines = new List<SeedLine>(_seeds)
        };

        PersonaStore.Save(data);

        // 씨앗 대사가 바뀌었을 가능성 → 기존 임베딩 캐시를 명시적으로 무효화하지는 않는다.
        // EmbeddingCache는 텍스트 해시 키라 자동으로 새 대사는 미스 → 재계산.
        // 삭제된 대사의 캐시 엔트리는 그대로 남지만, 다음 앱 시작 시 EmbeddingCache.Save가 현재 풀만 기록하므로 자연 정리.

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
