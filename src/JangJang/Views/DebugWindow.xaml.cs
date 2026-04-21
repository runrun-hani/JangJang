using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using JangJang.Core;
using JangJang.Core.Persona;
using JangJang.Core.Persona.Pipeline;
using JangJang.Core.Persona.Suggestion;

namespace JangJang.Views;

public partial class DebugWindow : Window
{
    private int _entryCount;

    public DebugWindow()
    {
        InitializeComponent();
        PersonaDialogueProvider.OnDebugEntry += OnDebugEntry;
        ApiDialogueSuggestionService.OnDebugEntry += OnSuggestionDebugEntry;
        Closed += (_, _) =>
        {
            PersonaDialogueProvider.OnDebugEntry -= OnDebugEntry;
            ApiDialogueSuggestionService.OnDebugEntry -= OnSuggestionDebugEntry;
        };
    }

    private void OnDebugEntry(DebugEntry entry)
    {
        // 이벤트는 백그라운드 스레드에서 올 수 있으므로 Dispatcher로 마샬링
        Dispatcher.BeginInvoke(() => AddEntry(entry));
    }

    private void AddEntry(DebugEntry e)
    {
        _entryCount++;
        EntryCountText.Text = $" — {_entryCount}건";

        var card = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var stack = new StackPanel();

        // 헤더: 시간 + 상태
        var header = new TextBlock
        {
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
            Text = $"{e.Timestamp:HH:mm:ss}  |  " +
                   $"상태={GetStateName(e.Context.State)}  " +
                   $"세션={FormatSec(e.Context.SessionSeconds)}  " +
                   $"유휴={FormatSec(e.Context.IdleSessionSeconds)}  " +
                   $"오늘={FormatSec(e.Context.TodaySeconds)}"
        };
        stack.Children.Add(header);

        // Narration
        stack.Children.Add(new TextBlock
        {
            Text = "Narration",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 6, 0, 2)
        });
        stack.Children.Add(new TextBlock
        {
            Text = e.Narration,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush")
        });

        // 후보 목록
        stack.Children.Add(new TextBlock
        {
            Text = $"후보 ({e.Candidates.Count}개)",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 6, 0, 2)
        });

        if (e.Candidates.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "(후보 없음 — 폴백)",
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush")
            });
        }
        else
        {
            for (int i = 0; i < e.Candidates.Count; i++)
            {
                var seed = e.Candidates[i];
                var isSelected = seed.Text == e.FinalLine;

                var linePanel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new Thickness(0, 1, 0, 1)
                };

                linePanel.Children.Add(new TextBlock
                {
                    Text = $"{i + 1}. ",
                    FontSize = 11,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isSelected
                        ? (System.Windows.Media.Brush)FindResource("PrimaryBrush")
                        : (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                    Width = 20
                });

                var textBlock = new TextBlock
                {
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = isSelected
                        ? (System.Windows.Media.Brush)FindResource("PrimaryBrush")
                        : (System.Windows.Media.Brush)FindResource("TextBrush"),
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal
                };

                textBlock.Inlines.Add(new Run(seed.Text));

                if (!string.IsNullOrEmpty(seed.SituationDescription))
                {
                    textBlock.Inlines.Add(new Run($"  [{seed.SituationDescription}]")
                    {
                        FontSize = 10,
                        Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                        FontWeight = FontWeights.Normal
                    });
                }

                if (isSelected)
                {
                    textBlock.Inlines.Add(new Run("  ← 선택")
                    {
                        FontSize = 10,
                        Foreground = (System.Windows.Media.Brush)FindResource("PrimaryBrush"),
                        FontWeight = FontWeights.SemiBold
                    });
                }

                linePanel.Children.Add(textBlock);
                stack.Children.Add(linePanel);
            }
        }

        card.Child = stack;

        // 최신이 위에 오도록 맨 앞에 삽입
        LogPanel.Children.Insert(0, card);

        // 100건 초과 시 오래된 항목 제거
        while (LogPanel.Children.Count > 100)
            LogPanel.Children.RemoveAt(LogPanel.Children.Count - 1);

        if (AutoScrollCheck.IsChecked == true)
            LogScrollViewer.ScrollToTop();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        LogPanel.Children.Clear();
        _entryCount = 0;
        EntryCountText.Text = " — 0건";
    }

    private void OnTopmostChanged(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostCheck.IsChecked == true;
    }

    private static string GetStateName(PetState state) => state switch
    {
        PetState.Happy => "작업 중",
        PetState.Alert => "경계",
        PetState.Annoyed => "화남",
        PetState.Sleeping => "수면",
        PetState.WakeUp => "대기",
        _ => state.ToString()
    };

    private static string FormatSec(int s)
    {
        if (s < 60) return $"{s}s";
        if (s < 3600) return $"{s / 60}m{s % 60}s";
        return $"{s / 3600}h{(s % 3600) / 60}m";
    }

    // ─── 편집 시 AI 추천 (SUGGEST) 카드 ────────────────────────────────────
    private void OnSuggestionDebugEntry(SuggestionDebugEntry entry)
    {
        Dispatcher.BeginInvoke(() => AddSuggestionEntry(entry));
    }

    private void AddSuggestionEntry(SuggestionDebugEntry e)
    {
        _entryCount++;
        EntryCountText.Text = $" \u2014 {_entryCount}건";

        var card = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("PrimaryBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
            Text = $"{e.Timestamp:HH:mm:ss}  |  \uD83E\uDD16 추천  |  상태={GetStateName(e.TargetState)}"
        });

        AppendSuggestionSection(stack, "System Prompt", e.SystemPrompt);
        AppendSuggestionSection(stack, "User Prompt", e.UserPrompt);
        AppendSuggestionSection(stack, "Raw Response",
            string.IsNullOrEmpty(e.RawResponse) ? "(empty)" : e.RawResponse);

        string parsedText;
        if (e.Parsed.Count == 0)
        {
            parsedText = "(파싱 결과 없음)";
        }
        else
        {
            var lines = new List<string>(e.Parsed.Count);
            for (int i = 0; i < e.Parsed.Count; i++)
            {
                var p = e.Parsed[i];
                var line = $"{i + 1}. {p.Text}";
                if (!string.IsNullOrEmpty(p.SituationDescription))
                    line += $"  |  {p.SituationDescription}";
                lines.Add(line);
            }
            parsedText = string.Join("\n", lines);
        }
        AppendSuggestionSection(stack, $"Parsed ({e.Parsed.Count}개)", parsedText);

        if (!string.IsNullOrEmpty(e.ExceptionMessage))
            AppendSuggestionSection(stack, "Exception", e.ExceptionMessage);

        card.Child = stack;
        LogPanel.Children.Insert(0, card);

        while (LogPanel.Children.Count > 100)
            LogPanel.Children.RemoveAt(LogPanel.Children.Count - 1);

        if (AutoScrollCheck.IsChecked == true)
            LogScrollViewer.ScrollToTop();
    }

    private void AppendSuggestionSection(StackPanel stack, string label, string body)
    {
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 6, 0, 2)
        });
        stack.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush")
        });
    }
}
