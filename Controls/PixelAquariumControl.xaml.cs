using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GunlukUygulamasi.Controls;

public partial class PixelAquariumControl : UserControl
{
    private const double FishWidth = 28;
    private const double FishHeight = 16;
    private const double MinimumFishX = 5;
    private const double MinimumFishY = 18;

    private readonly DispatcherTimer _frameTimer;
    private readonly Canvas _fishVisual = new()
    {
        Width = FishWidth,
        Height = FishHeight,
        IsHitTestVisible = false,
        RenderTransformOrigin = new Point(0.5, 0.5)
    };
    private readonly ScaleTransform _fishFlip = new();
    private readonly Canvas _tailFrameA = new() { Width = 6, Height = 12, IsHitTestVisible = false };
    private readonly Canvas _tailFrameB = new() { Width = 6, Height = 12, IsHitTestVisible = false };
    private readonly TextBlock _heartText = new()
    {
        Text = "♥",
        FontFamily = new FontFamily("Segoe UI Symbol"),
        FontSize = 14,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    private readonly List<FrameworkElement> _scenery = [];
    private readonly List<FoodPellet> _food = [];
    private readonly List<Bubble> _bubbles = [];

    private double _fishX;
    private double _fishY;
    private double _elapsedSeconds;
    private double _heartRemaining;
    private double _bubbleSpawnCountdown = 1.5;
    private int _direction = 1;
    private long _previousFrameTimestamp;
    private bool _showTailFrameA;
    private bool _hasFishPosition;
    private bool _isEditingFishName;
    private string _nameBeforeEdit = string.Empty;

    public PixelAquariumControl()
    {
        InitializeComponent();

        _fishVisual.RenderTransform = _fishFlip;
        BuildFish();
        _heartText.SetResourceReference(TextBlock.ForegroundProperty, "AccentHoverBrush");
        AquariumCanvas.Children.Add(_fishVisual);
        AquariumCanvas.Children.Add(_heartText);
        Loaded += PixelAquariumControl_Loaded;
        Unloaded += PixelAquariumControl_Unloaded;
        IsVisibleChanged += PixelAquariumControl_IsVisibleChanged;

        _frameTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _frameTimer.Tick += FrameTimer_Tick;
        UpdateFishInfo();
    }

    public event EventHandler? FishStateChanged;

    public string FishName { get; private set; } = "Maviş";
    public int FeedCount { get; private set; }
    public bool IsEditingFishName => _isEditingFishName;

    public void SetSavedState(string? fishName, int feedCount)
    {
        FishName = NormalizeFishName(fishName);
        FeedCount = Math.Max(0, feedCount);
        UpdateFishInfo();
    }

    private static string NormalizeFishName(string? fishName)
    {
        var normalizedName = fishName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return "Maviş";
        }

        return normalizedName.Length > 16 ? normalizedName[..16] : normalizedName;
    }

    private void BuildFish()
    {
        _tailFrameA.Visibility = Visibility.Visible;
        _tailFrameB.Visibility = Visibility.Collapsed;
        AddFishPixel(_tailFrameA, 2, 1, 3, 3, "AccentHoverBrush");
        AddFishPixel(_tailFrameA, 0, 4, 6, 4, "AccentBrush");
        AddFishPixel(_tailFrameA, 2, 8, 3, 3, "AccentHoverBrush");
        AddFishPixel(_tailFrameB, 2, 3, 3, 3, "AccentHoverBrush");
        AddFishPixel(_tailFrameB, 0, 4, 6, 4, "AccentBrush");
        AddFishPixel(_tailFrameB, 2, 6, 3, 3, "AccentHoverBrush");
        Canvas.SetLeft(_tailFrameA, 0);
        Canvas.SetTop(_tailFrameA, 2);
        Canvas.SetLeft(_tailFrameB, 0);
        Canvas.SetTop(_tailFrameB, 2);
        _fishVisual.Children.Add(_tailFrameA);
        _fishVisual.Children.Add(_tailFrameB);

        AddFishPixel(_fishVisual, 10, 1, 8, 2, "AccentHoverBrush");
        AddFishPixel(_fishVisual, 6, 3, 16, 2, "AccentBrush");
        AddFishPixel(_fishVisual, 4, 5, 20, 6, "AccentBrush");
        AddFishPixel(_fishVisual, 6, 11, 16, 2, "AccentBrush");
        AddFishPixel(_fishVisual, 10, 13, 8, 2, "AccentHoverBrush");
        AddFishPixel(_fishVisual, 19, 6, 1, 4, "AccentHoverBrush");
        AddFishPixel(_fishVisual, 22, 6, 2, 2, "TextPrimaryBrush");
        AddFishPixel(_fishVisual, 23, 7, 1, 1, "BgPrimaryBrush");
    }

    private static void AddFishPixel(Canvas target, double x, double y, double width, double height, string brushKey)
    {
        var pixel = new Rectangle
        {
            Width = width,
            Height = height,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        pixel.SetResourceReference(Shape.FillProperty, brushKey);
        Canvas.SetLeft(pixel, x);
        Canvas.SetTop(pixel, y);
        target.Children.Add(pixel);
    }

    private void PixelAquariumControl_Loaded(object sender, RoutedEventArgs e)
    {
        RebuildScenery();
        if (!_hasFishPosition)
        {
            _fishX = Math.Max(MinimumFishX, (AquariumCanvas.ActualWidth - FishWidth) * 0.4);
            _fishY = GetBaseFishY();
            _hasFishPosition = true;
        }

        UpdateTimerState();
    }

    private void PixelAquariumControl_Unloaded(object sender, RoutedEventArgs e) =>
        _frameTimer.Stop();

    private void PixelAquariumControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        UpdateTimerState();

    private void UpdateTimerState()
    {
        if (IsLoaded && IsVisible && ActualWidth > 0 && ActualHeight > 0)
        {
            _previousFrameTimestamp = Stopwatch.GetTimestamp();
            _frameTimer.Start();
        }
        else
        {
            _frameTimer.Stop();
        }
    }

    private void AquariumCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RebuildScenery();
        _fishX = Math.Clamp(_fishX, MinimumFishX, GetMaximumFishX());
        _fishY = Math.Clamp(_fishY, MinimumFishY, GetMaximumFishY());
        UpdateTimerState();
    }

    private void RebuildScenery()
    {
        foreach (var element in _scenery)
        {
            AquariumCanvas.Children.Remove(element);
        }

        _scenery.Clear();
        var width = AquariumCanvas.ActualWidth;
        var groundY = Math.Max(0, AquariumCanvas.ActualHeight - 3);
        if (width <= 0 || groundY <= 0)
        {
            return;
        }

        AddPlant(12, groundY);
        AddPlant(Math.Max(42, width * 0.48), groundY);
        AddPlant(Math.Max(74, width - 34), groundY);
        AddSceneryPixel(width * 0.27, groundY, 5, 2, "TextMutedBrush");
        AddSceneryPixel(width * 0.29, groundY - 2, 3, 2, "BorderSubtleBrush");
        AddSceneryPixel(width * 0.64, groundY, 7, 2, "BorderSubtleBrush");
        AddSceneryPixel(width * 0.66, groundY - 2, 3, 2, "TextMutedBrush");
        AddSceneryPixel(width * 0.86, groundY, 4, 2, "TextMutedBrush");
    }

    private void AddPlant(double x, double groundY)
    {
        AddSceneryPixel(x + 4, groundY - 13, 3, 13, "SuccessBrush");
        AddSceneryPixel(x, groundY - 11, 5, 3, "SuccessBrush");
        AddSceneryPixel(x + 7, groundY - 9, 5, 3, "AccentHoverBrush");
        AddSceneryPixel(x + 2, groundY - 16, 4, 3, "AccentHoverBrush");
    }

    private void AddSceneryPixel(double x, double y, double width, double height, string brushKey)
    {
        var pixel = new Rectangle
        {
            Width = Math.Max(1, Math.Round(width)),
            Height = Math.Max(1, Math.Round(height)),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        pixel.SetResourceReference(Shape.FillProperty, brushKey);
        Canvas.SetLeft(pixel, Math.Round(x));
        Canvas.SetTop(pixel, Math.Round(y));
        AquariumCanvas.Children.Add(pixel);
        _scenery.Add(pixel);
    }

    private void AquariumCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(AquariumCanvas);
        AddFood(point.X, point.Y);
        e.Handled = true;
    }

    private void AddFood(double x, double y)
    {
        var canvasWidth = AquariumCanvas.ActualWidth;
        var canvasHeight = AquariumCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        AddFoodPellet(Math.Clamp(x - 2, 1, canvasWidth - 4), Math.Clamp(y, 1, GetFoodFloorY()), 3);
        AddFoodPellet(Math.Clamp(x + 3, 1, canvasWidth - 4), Math.Clamp(y - 4, 1, GetFoodFloorY()), 2);
    }

    private void AddFoodPellet(double x, double y, double size)
    {
        while (_food.Count >= 12)
        {
            AquariumCanvas.Children.Remove(_food[0].Visual);
            _food.RemoveAt(0);
        }

        var visual = new Rectangle
        {
            Width = size,
            Height = size,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        visual.SetResourceReference(Shape.FillProperty, "AccentHoverBrush");
        AquariumCanvas.Children.Add(visual);
        _food.Add(new FoodPellet(visual, x, y, size));
        Canvas.SetLeft(visual, x);
        Canvas.SetTop(visual, y);
    }

    private void FrameTimer_Tick(object? sender, EventArgs e)
    {
        var timestamp = Stopwatch.GetTimestamp();
        var deltaSeconds = Math.Clamp(
            (timestamp - _previousFrameTimestamp) / (double)Stopwatch.Frequency,
            0,
            0.1);
        _previousFrameTimestamp = timestamp;
        _elapsedSeconds += deltaSeconds;
        _bubbleSpawnCountdown -= deltaSeconds;

        UpdateFood(deltaSeconds);
        UpdateBubbles(deltaSeconds);
        UpdateFish(deltaSeconds);
        UpdateTailAndHeart(deltaSeconds);
    }

    private void UpdateFood(double deltaSeconds)
    {
        var foodFloorY = GetFoodFloorY();
        for (var index = _food.Count - 1; index >= 0; index--)
        {
            var pellet = _food[index];
            pellet.Y = Math.Min(foodFloorY, pellet.Y + 12 * deltaSeconds);
            Canvas.SetTop(pellet.Visual, pellet.Y);
        }
    }

    private void UpdateBubbles(double deltaSeconds)
    {
        if (_bubbleSpawnCountdown <= 0 && _bubbles.Count < 2 && AquariumCanvas.ActualWidth > 12)
        {
            var size = Random.Shared.Next(3, 6);
            var x = Random.Shared.Next(6, Math.Max(7, (int)AquariumCanvas.ActualWidth - 6));
            var y = Math.Max(0, AquariumCanvas.ActualHeight - 9);
            AddBubble(x, y, size, Random.Shared.Next(7, 12));
            _bubbleSpawnCountdown = Random.Shared.NextDouble() * 2.2 + 1.4;
        }

        for (var index = _bubbles.Count - 1; index >= 0; index--)
        {
            var bubble = _bubbles[index];
            bubble.Y -= bubble.Speed * deltaSeconds;
            Canvas.SetTop(bubble.Visual, bubble.Y);
            if (bubble.Y + bubble.Size < 0)
            {
                AquariumCanvas.Children.Remove(bubble.Visual);
                _bubbles.RemoveAt(index);
            }
        }
    }

    private void AddBubble(double x, double y, double size, double speed)
    {
        var visual = new Rectangle
        {
            Width = size,
            Height = size,
            Fill = Brushes.Transparent,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        visual.SetResourceReference(Shape.StrokeProperty, "AccentHoverBrush");
        AquariumCanvas.Children.Add(visual);
        Canvas.SetLeft(visual, x);
        Canvas.SetTop(visual, y);
        _bubbles.Add(new Bubble(visual, x, y, size, speed));
    }

    private void UpdateFish(double deltaSeconds)
    {
        var target = FindNearestFood();
        var maxX = GetMaximumFishX();
        var maxY = GetMaximumFishY();
        if (target is null)
        {
            _fishX += _direction * 16 * deltaSeconds;
            if (_fishX >= maxX)
            {
                _fishX = maxX;
                _direction = -1;
            }
            else if (_fishX <= MinimumFishX)
            {
                _fishX = MinimumFishX;
                _direction = 1;
            }

            _fishY = Math.Clamp(
                GetBaseFishY() + Math.Sin(_elapsedSeconds * 1.15) * 3,
                MinimumFishY,
                maxY);
        }
        else
        {
            var desiredX = Math.Clamp(target.X + target.Size / 2 - FishWidth / 2, MinimumFishX, maxX);
            var differenceX = desiredX - _fishX;
            if (Math.Abs(differenceX) > 1)
            {
                _direction = differenceX > 0 ? 1 : -1;
                _fishX = MoveToward(_fishX, desiredX, 26 * deltaSeconds);
            }

            var desiredY = Math.Clamp(target.Y + target.Size / 2 - FishHeight / 2, MinimumFishY, maxY);
            _fishY = MoveToward(_fishY, desiredY, 18 * deltaSeconds);

            var foodCenterX = target.X + target.Size / 2;
            var foodCenterY = target.Y + target.Size / 2;
            var horizontalDistance = Math.Max(0, Math.Max(_fishX - foodCenterX, foodCenterX - (_fishX + FishWidth)));
            var verticalDistance = Math.Max(0, Math.Max(_fishY - foodCenterY, foodCenterY - (_fishY + FishHeight)));
            if (horizontalDistance < 4 && verticalDistance < 4)
            {
                EatFood(target);
            }
        }

        _fishFlip.ScaleX = _direction;
        Canvas.SetLeft(_fishVisual, _fishX);
        Canvas.SetTop(_fishVisual, _fishY);
        Canvas.SetLeft(_heartText, _fishX + 16);
        Canvas.SetTop(_heartText, _fishY - 12);
    }

    private FoodPellet? FindNearestFood()
    {
        FoodPellet? closest = null;
        var nearestDistanceSquared = double.MaxValue;
        var fishCenterX = _fishX + FishWidth / 2;
        var fishCenterY = _fishY + FishHeight / 2;
        foreach (var pellet in _food)
        {
            var deltaX = pellet.X + pellet.Size / 2 - fishCenterX;
            var deltaY = pellet.Y + pellet.Size / 2 - fishCenterY;
            var distanceSquared = deltaX * deltaX + deltaY * deltaY;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                closest = pellet;
            }
        }

        return closest;
    }

    private void EatFood(FoodPellet pellet)
    {
        AquariumCanvas.Children.Remove(pellet.Visual);
        _food.Remove(pellet);
        if (FeedCount < int.MaxValue)
        {
            FeedCount++;
        }

        _heartRemaining = 1;
        UpdateFishInfo();
        FishStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTailAndHeart(double deltaSeconds)
    {
        var showFrameA = (int)(_elapsedSeconds / 0.18) % 2 == 0;
        if (_showTailFrameA != showFrameA)
        {
            _showTailFrameA = showFrameA;
            _tailFrameA.Visibility = showFrameA ? Visibility.Visible : Visibility.Collapsed;
            _tailFrameB.Visibility = showFrameA ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_heartRemaining > 0)
        {
            _heartRemaining = Math.Max(0, _heartRemaining - deltaSeconds);
            _heartText.Opacity = _heartRemaining;
            _heartText.Visibility = Visibility.Visible;
        }
        else
        {
            _heartText.Visibility = Visibility.Collapsed;
        }
    }

    private double GetBaseFishY() =>
        Math.Clamp(AquariumCanvas.ActualHeight * 0.52, MinimumFishY, GetMaximumFishY());

    private double GetMaximumFishX() =>
        Math.Max(MinimumFishX, AquariumCanvas.ActualWidth - FishWidth - 5);

    private double GetMaximumFishY() =>
        Math.Max(MinimumFishY, AquariumCanvas.ActualHeight - FishHeight - 18);

    private double GetFoodFloorY() =>
        Math.Max(1, AquariumCanvas.ActualHeight - FishHeight / 2 - 22);

    private static double MoveToward(double current, double target, double maximumDelta)
    {
        var difference = target - current;
        if (Math.Abs(difference) <= maximumDelta)
        {
            return target;
        }

        return current + Math.Sign(difference) * maximumDelta;
    }

    private void UpdateFishInfo() =>
        FishInfoText.Text = $"{FishName} · {FeedCount} yem";

    private void FishInfoText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isEditingFishName = true;
        _nameBeforeEdit = FishName;
        FishNameEditor.Text = FishName;
        FishInfoText.Visibility = Visibility.Collapsed;
        FishNameEditor.Visibility = Visibility.Visible;
        FishNameEditor.Focus();
        FishNameEditor.SelectAll();
        e.Handled = true;
    }

    private void FishNameEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitFishName();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FishNameEditor.Text = _nameBeforeEdit;
            FinishFishNameEditing();
            e.Handled = true;
        }
    }

    private void FishNameEditor_LostFocus(object sender, RoutedEventArgs e) =>
        CommitFishName();

    private void CommitFishName()
    {
        if (!_isEditingFishName)
        {
            return;
        }

        var updatedName = NormalizeFishName(FishNameEditor.Text);
        var changed = !string.Equals(FishName, updatedName, StringComparison.Ordinal);
        FishName = updatedName;
        FinishFishNameEditing();
        if (changed)
        {
            FishStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void FinishFishNameEditing()
    {
        _isEditingFishName = false;
        FishNameEditor.Visibility = Visibility.Collapsed;
        FishInfoText.Visibility = Visibility.Visible;
        UpdateFishInfo();
    }

    private sealed class FoodPellet(Rectangle visual, double x, double y, double size)
    {
        public Rectangle Visual { get; } = visual;
        public double X { get; set; } = x;
        public double Y { get; set; } = y;
        public double Size { get; } = size;
    }

    private sealed class Bubble(Rectangle visual, double x, double y, double size, double speed)
    {
        public Rectangle Visual { get; } = visual;
        public double X { get; } = x;
        public double Y { get; set; } = y;
        public double Size { get; } = size;
        public double Speed { get; } = speed;
    }
}
