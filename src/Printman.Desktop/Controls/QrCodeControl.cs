using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Printman.Desktop.Controls;

public sealed class QrCodeControl : Avalonia.Controls.Control
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<QrCodeControl, string?>(nameof(Value));

    private bool[,]? _matrix;

    static QrCodeControl()
    {
        AffectsRender<QrCodeControl>(ValueProperty);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            _matrix = string.IsNullOrWhiteSpace(Value) ? null : Services.QrCodeGenerator.Encode(Value);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        context.FillRectangle(Brushes.White, new Rect(0, 0, bounds.Width, bounds.Height));

        if (_matrix == null)
        {
            return;
        }

        int modules = _matrix.GetLength(0);
        const int quietZone = 2;
        int total = modules + quietZone * 2;
        double side = Math.Min(bounds.Width, bounds.Height);
        double cell = side / total;
        double offsetX = (bounds.Width - side) / 2.0;
        double offsetY = (bounds.Height - side) / 2.0;

        for (int y = 0; y < modules; y++)
        {
            for (int x = 0; x < modules; x++)
            {
                if (_matrix[y, x])
                {
                    var rect = new Rect(
                        offsetX + (x + quietZone) * cell,
                        offsetY + (y + quietZone) * cell,
                        Math.Ceiling(cell),
                        Math.Ceiling(cell));
                    context.FillRectangle(Brushes.Black, rect);
                }
            }
        }
    }
}
