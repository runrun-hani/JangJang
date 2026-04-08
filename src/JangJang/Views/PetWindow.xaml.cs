using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using JangJang.Core;
using JangJang.ViewModels;

namespace JangJang.Views;

public partial class PetWindow : Window
{
    private readonly AppSettings _settings;

    public PetWindow(PetViewModel viewModel, AppSettings settings)
    {
        _settings = settings;
        DataContext = viewModel;
        InitializeComponent();

        if (!double.IsNaN(settings.PetPositionX) && !double.IsNaN(settings.PetPositionY))
        {
            Left = settings.PetPositionX;
            Top = settings.PetPositionY;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Loaded += (_, _) =>
            {
                Left = (area.Width - ActualWidth) / 2;
                Top = area.Height - ActualHeight;
            };
        }

        ApplyScale();
    }

    public void ApplyScale()
    {
        var scale = _settings.PetSize;
        Renderer.LayoutTransform = new ScaleTransform(scale, scale);
    }

    // 트레이 아이콘 설정 메뉴에서 호출
    public void ApplySettingsFromTray()
    {
        if (DataContext is PetViewModel vm)
            vm.LoadPetImage();
        ApplyScale();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ContextMenu != null)
            ContextMenu.IsOpen = !ContextMenu.IsOpen;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        _settings.PetPositionX = Left;
        _settings.PetPositionY = Top;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
            ApplySettingsFromTray();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        _settings.Save();
        Application.Current.Shutdown();
    }
}
