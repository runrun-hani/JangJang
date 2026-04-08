using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using JangJang.Core;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using ListBox = System.Windows.Controls.ListBox;
using Button = System.Windows.Controls.Button;

namespace JangJang.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private string? _selectedImagePath;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        // 프리셋 콤보 초기화
        foreach (IdlePreset preset in Enum.GetValues<IdlePreset>())
            PresetCombo.Items.Add(new ComboBoxItem { Content = preset.ToDisplayName(), Tag = preset });

        PresetCombo.SelectedIndex = (int)settings.IdlePreset;
        CustomMinutesBox.Text = settings.CustomIdleMinutes.ToString();
        SizeSlider.Value = settings.PetSize;
        TargetProcessBox.Text = settings.TargetProcessName;
        GrowCheck.IsChecked = settings.GrowWhenAnnoyed;
        GrowSlider.Value = settings.MaxGrowScale;

        // 이미지 미리보기
        _selectedImagePath = settings.PetImagePath;
        LoadPreviewImage();
    }

    private void LoadPreviewImage()
    {
        if (!string.IsNullOrEmpty(_selectedImagePath) && File.Exists(_selectedImagePath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(_selectedImagePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
            ImagePathText.Text = _selectedImagePath;
        }
        else
        {
            PreviewImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/pet.png"));
            ImagePathText.Text = "(기본 이미지)";
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is ComboBoxItem item && item.Tag is IdlePreset preset)
            CustomPanel.Visibility = preset == IdlePreset.Custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSelectImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "펫 이미지 선택",
            Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.gif;*.bmp|모든 파일|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedImagePath = dialog.FileName;
            LoadPreviewImage();
        }
    }

    private void OnResetImageClick(object sender, RoutedEventArgs e)
    {
        _selectedImagePath = null;
        LoadPreviewImage();
    }

    private void OnSelectProcessClick(object sender, RoutedEventArgs e)
    {
        var allProcs = Process.GetProcesses();
        var processes = new List<(string Name, string Title)>();
        foreach (var p in allProcs)
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                    processes.Add((p.ProcessName, p.MainWindowTitle));
            }
            catch { }
            finally { p.Dispose(); }
        }
        processes = processes.Distinct().OrderBy(x => x.Name).ToList();

        var selectWindow = new Window
        {
            Title = "실행 중인 프로그램 선택",
            Width = 450,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var stack = new StackPanel { Margin = new Thickness(8) };
        var listBox = new ListBox { Height = 260 };

        foreach (var (name, title) in processes)
            listBox.Items.Add(new ListBoxItem
            {
                Content = $"{name}  —  {title}",
                Tag = name
            });

        var okBtn = new Button
        {
            Content = "선택", Width = 80, Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Padding = new Thickness(8, 4, 8, 4)
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
        _settings.PetImagePath = _selectedImagePath;
        _settings.GrowWhenAnnoyed = GrowCheck.IsChecked == true;
        _settings.MaxGrowScale = GrowSlider.Value;

        var processName = TargetProcessBox.Text.Trim();
        if (!string.IsNullOrEmpty(processName))
            _settings.TargetProcessName = processName;

        _settings.Save();
        DialogResult = true;
    }
}
