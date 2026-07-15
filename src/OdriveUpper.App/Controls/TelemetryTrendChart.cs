using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OdriveUpper.App.ViewModels;

namespace OdriveUpper.App.Controls;

public sealed class TelemetryTrendChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<TelemetrySample>?> SamplesProperty =
        AvaloniaProperty.Register<TelemetryTrendChart, IReadOnlyList<TelemetrySample>?>(nameof(Samples));

    public static readonly StyledProperty<IReadOnlyList<TelemetrySample>?> SecondarySamplesProperty =
        AvaloniaProperty.Register<TelemetryTrendChart, IReadOnlyList<TelemetrySample>?>(nameof(SecondarySamples));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<TelemetryTrendChart, IBrush>(nameof(LineBrush), Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush> SecondaryLineBrushProperty =
        AvaloniaProperty.Register<TelemetryTrendChart, IBrush>(nameof(SecondaryLineBrush), Brushes.Orange);

    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<TelemetryTrendChart, IBrush>(nameof(GridBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush> HoverBrushProperty =
        AvaloniaProperty.Register<TelemetryTrendChart, IBrush>(nameof(HoverBrush), Brushes.White);

    public static readonly StyledProperty<string> PrimaryLabelProperty =
        AvaloniaProperty.Register<TelemetryTrendChart, string>(nameof(PrimaryLabel), "主序列");

    public static readonly StyledProperty<string> SecondaryLabelProperty =
        AvaloniaProperty.Register<TelemetryTrendChart, string>(nameof(SecondaryLabel), "次序列");

    private INotifyCollectionChanged? _observedSamples;
    private INotifyCollectionChanged? _observedSecondarySamples;
    private IReadOnlyList<TelemetrySample>? _frozenSamples;
    private IReadOnlyList<TelemetrySample>? _frozenSecondarySamples;
    private int? _hoveredSampleIndex;

    static TelemetryTrendChart()
    {
        AffectsRender<TelemetryTrendChart>(
            SamplesProperty,
            SecondarySamplesProperty,
            LineBrushProperty,
            SecondaryLineBrushProperty,
            GridBrushProperty,
            HoverBrushProperty);
    }

    public IReadOnlyList<TelemetrySample>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public IReadOnlyList<TelemetrySample>? SecondarySamples
    {
        get => GetValue(SecondarySamplesProperty);
        set => SetValue(SecondarySamplesProperty, value);
    }

    public IBrush LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush SecondaryLineBrush
    {
        get => GetValue(SecondaryLineBrushProperty);
        set => SetValue(SecondaryLineBrushProperty, value);
    }

    public IBrush GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush HoverBrush
    {
        get => GetValue(HoverBrushProperty);
        set => SetValue(HoverBrushProperty, value);
    }

    public string PrimaryLabel
    {
        get => GetValue(PrimaryLabelProperty);
        set => SetValue(PrimaryLabelProperty, value);
    }

    public string SecondaryLabel
    {
        get => GetValue(SecondaryLabelProperty);
        set => SetValue(SecondaryLabelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SamplesProperty)
        {
            ReplaceObservedSamples(ref _observedSamples, Samples as INotifyCollectionChanged);
        }
        else if (change.Property == SecondarySamplesProperty)
        {
            ReplaceObservedSamples(ref _observedSecondarySamples, SecondarySamples as INotifyCollectionChanged);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var primarySamples = _frozenSamples ?? Samples;
        var secondarySamples = _frozenSecondarySamples ?? SecondarySamples;
        if (((primarySamples is null || primarySamples.Count == 0) &&
             (secondarySamples is null || secondarySamples.Count == 0)) ||
            Bounds.Width <= 16 || Bounds.Height <= 16)
        {
            return;
        }

        var chartBounds = new Rect(8, 8, Bounds.Width - 16, Bounds.Height - 16);
        var allValues = (primarySamples ?? []).Select(sample => sample.Value)
            .Concat((secondarySamples ?? []).Select(sample => sample.Value))
            .ToArray();
        var minimum = allValues.Min();
        var maximum = allValues.Max();
        var range = maximum - minimum;
        var padding = range > double.Epsilon ? range * 0.08 : Math.Max(Math.Abs(maximum) * 0.08, 1.0);
        minimum -= padding;
        maximum += padding;
        range = maximum - minimum;

        var gridPen = new Pen(GridBrush, 1);
        for (var line = 0; line < 3; line++)
        {
            var y = chartBounds.Top + chartBounds.Height * line / 2.0;
            context.DrawLine(gridPen, new Point(chartBounds.Left, y), new Point(chartBounds.Right, y));
        }

        DrawSeries(context, primarySamples, LineBrush, chartBounds, minimum, range);
        DrawSeries(context, secondarySamples, SecondaryLineBrush, chartBounds, minimum, range);
        DrawHoveredSamples(context, primarySamples, secondarySamples, chartBounds, minimum, range);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_hoveredSampleIndex is null)
        {
            FreezeSamples();
        }

        var primarySamples = _frozenSamples ?? Samples;
        var secondarySamples = _frozenSecondarySamples ?? SecondarySamples;
        var referenceSamples = primarySamples is { Count: > 0 } ? primarySamples : secondarySamples;
        if (referenceSamples is null || referenceSamples.Count == 0 || Bounds.Width <= 16)
        {
            return;
        }

        var position = e.GetPosition(this);
        var relativeX = Math.Clamp((position.X - 8) / (Bounds.Width - 16), 0, 1);
        var index = (int)Math.Round(relativeX * (referenceSamples.Count - 1));
        if (_hoveredSampleIndex != index)
        {
            _hoveredSampleIndex = index;
            ToolTip.SetTip(this, BuildTooltip(primarySamples, secondarySamples, index, referenceSamples == primarySamples));
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredSampleIndex is null)
        {
            return;
        }

        _hoveredSampleIndex = null;
        _frozenSamples = null;
        _frozenSecondarySamples = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    private static void DrawSeries(
        DrawingContext context,
        IReadOnlyList<TelemetrySample>? samples,
        IBrush lineBrush,
        Rect chartBounds,
        double minimum,
        double range)
    {
        if (samples is null || samples.Count == 0)
        {
            return;
        }

        var linePen = new Pen(lineBrush, 2);
        Point? previous = null;
        for (var index = 0; index < samples.Count; index++)
        {
            var point = ToPoint(samples[index].Value, index, samples.Count, chartBounds, minimum, range);
            if (previous is not null)
            {
                context.DrawLine(linePen, previous.Value, point);
            }

            previous = point;
        }
    }

    private void DrawHoveredSamples(
        DrawingContext context,
        IReadOnlyList<TelemetrySample>? primarySamples,
        IReadOnlyList<TelemetrySample>? secondarySamples,
        Rect chartBounds,
        double minimum,
        double range)
    {
        if (_hoveredSampleIndex is not int hoveredIndex)
        {
            return;
        }

        var referenceSamples = primarySamples is { Count: > 0 } ? primarySamples : secondarySamples;
        if (referenceSamples is null || hoveredIndex >= referenceSamples.Count)
        {
            return;
        }

        var relativePosition = referenceSamples.Count == 1 ? 0 : hoveredIndex / (referenceSamples.Count - 1.0);
        var x = chartBounds.Left + chartBounds.Width * relativePosition;
        context.DrawLine(new Pen(HoverBrush, 1), new Point(x, chartBounds.Top), new Point(x, chartBounds.Bottom));
        DrawHoveredPoint(context, primarySamples, relativePosition, chartBounds, minimum, range, LineBrush);
        DrawHoveredPoint(context, secondarySamples, relativePosition, chartBounds, minimum, range, SecondaryLineBrush);
    }

    private static void DrawHoveredPoint(
        DrawingContext context,
        IReadOnlyList<TelemetrySample>? samples,
        double relativePosition,
        Rect chartBounds,
        double minimum,
        double range,
        IBrush brush)
    {
        if (samples is null || samples.Count == 0)
        {
            return;
        }

        var index = (int)Math.Round(relativePosition * (samples.Count - 1));
        var point = ToPoint(samples[index].Value, index, samples.Count, chartBounds, minimum, range);
        context.DrawEllipse(brush, null, point, 4, 4);
    }

    private string BuildTooltip(
        IReadOnlyList<TelemetrySample>? primarySamples,
        IReadOnlyList<TelemetrySample>? secondarySamples,
        int hoveredIndex,
        bool primaryIsReference)
    {
        var referenceSamples = primaryIsReference ? primarySamples : secondarySamples;
        if (referenceSamples is null || referenceSamples.Count == 0)
        {
            return string.Empty;
        }

        var timestamp = referenceSamples[hoveredIndex].Timestamp.LocalDateTime.ToString("HH:mm:ss.fff");
        var relativePosition = referenceSamples.Count == 1 ? 0 : hoveredIndex / (referenceSamples.Count - 1.0);
        var lines = new List<string> { timestamp };
        AddTooltipLine(lines, PrimaryLabel, primarySamples, relativePosition);
        AddTooltipLine(lines, SecondaryLabel, secondarySamples, relativePosition);
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddTooltipLine(List<string> lines, string label, IReadOnlyList<TelemetrySample>? samples, double relativePosition)
    {
        if (samples is null || samples.Count == 0)
        {
            return;
        }

        var index = (int)Math.Round(relativePosition * (samples.Count - 1));
        lines.Add($"{label}: {samples[index].Value:0.###} A");
    }

    private void FreezeSamples()
    {
        _frozenSamples = Samples?.ToArray();
        _frozenSecondarySamples = SecondarySamples?.ToArray();
    }

    private void ReplaceObservedSamples(ref INotifyCollectionChanged? observedSamples, INotifyCollectionChanged? replacement)
    {
        if (observedSamples is not null)
        {
            observedSamples.CollectionChanged -= OnSamplesCollectionChanged;
        }

        observedSamples = replacement;
        if (observedSamples is not null)
        {
            observedSamples.CollectionChanged += OnSamplesCollectionChanged;
        }
    }

    private void OnSamplesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_hoveredSampleIndex is null)
        {
            InvalidateVisual();
        }
    }

    private static Point ToPoint(double value, int index, int count, Rect bounds, double minimum, double range)
    {
        var x = count == 1
            ? bounds.Center.X
            : bounds.Left + bounds.Width * index / (count - 1.0);
        var y = bounds.Bottom - (value - minimum) / range * bounds.Height;
        return new Point(x, y);
    }
}
