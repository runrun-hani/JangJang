using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JangJang.ViewModels;

namespace JangJang.Views;

public partial class PetRenderer : UserControl
{
    private readonly DispatcherTimer _animTimer;
    private readonly Random _rand = new();
    private ScaleTransform? _growTransform;

    public PetRenderer()
    {
        InitializeComponent();
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _animTimer.Tick += AnimatePet;
        _animTimer.Start();

        Unloaded += (_, _) => _animTimer.Stop();
    }

    private void AnimatePet(object? sender, EventArgs e)
    {
        if (DataContext is not PetViewModel vm) return;

        // 흔들림 애니메이션
        ShakeTransform.X = vm.ShakeAmplitude > 0.1
            ? (_rand.NextDouble() * 2 - 1) * vm.ShakeAmplitude
            : 0;

        // 분노 시 크기 증가 (이미지+오버레이 밖에서 적용)
        _growTransform ??= new ScaleTransform(1, 1);
        _growTransform.ScaleX = vm.AngryScale;
        _growTransform.ScaleY = vm.AngryScale;
        GrowWrapper.LayoutTransform = _growTransform;

        // 상태별 색조: 이미지 픽셀 영역에만 적용
        switch (vm.CurrentState)
        {
            case Core.PetState.Annoyed:
                ColorOverlay.Fill = new SolidColorBrush(System.Windows.Media.Colors.Red);
                ColorOverlay.Opacity = vm.AnnoyanceLevel * 0.5;
                break;

            case Core.PetState.Idle:
                ColorOverlay.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                ColorOverlay.Opacity = 0.2;
                break;

            case Core.PetState.Sleeping:
                ColorOverlay.Fill = new SolidColorBrush(Color.FromRgb(100, 100, 150));
                ColorOverlay.Opacity = 0.3;
                break;

            default:
                ColorOverlay.Opacity = 0;
                break;
        }
    }
}
