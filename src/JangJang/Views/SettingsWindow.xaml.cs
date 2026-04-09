using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using JangJang.Core;
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

        RefreshPreviews();
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
            Width = 450, Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize
        };

        var stack = new StackPanel { Margin = new Thickness(8) };
        var listBox = new ListBox { Height = 260 };

        foreach (var (name, title) in processes)
        {
            var display = string.IsNullOrEmpty(title) ? name : $"{name}  —  {title}";
            listBox.Items.Add(new ListBoxItem { Content = display, Tag = name });
        }

        var okBtn = new Button
        {
            Content = "선택", Width = 80, Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Padding = new Thickness(8, 4, 8, 4)
        };
        okBtn.Click += (_, _) =>
        {
            if (listBox.SelectedItem is ListBoxItem selected)
            {
                TargetProcessBox.Text = (string)selected.Tag;
                selectWindow.Close();
            }
        };

        stack.Children.Add(listBox);
        stack.Children.Add(okBtn);
        selectWindow.Content = stack;
        selectWindow.ShowDialog();
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
        AutoStartHelper.SetAutoStart(_settings.StartWithWindows);

        _settings.Save();
        DialogResult = true;
    }
}
