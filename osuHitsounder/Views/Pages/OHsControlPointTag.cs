using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace osuHitsounder.Views.Pages;

/// <summary>A timestamped edit target with drag-preview state and native button invocation.</summary>
public sealed class OHsControlPointTag : Button
{
    public static readonly DependencyProperty PointProperty = DependencyProperty.Register(nameof(Point),
        typeof(Resources.DataModels.ControlPointSnapshot), typeof(OHsControlPointTag), new PropertyMetadata(null, (d, _) => ((OHsControlPointTag)d).RefreshText()));
    private static readonly DependencyPropertyKey LabelPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Label), typeof(string), typeof(OHsControlPointTag), new PropertyMetadata(""));
    public static readonly DependencyProperty LabelProperty = LabelPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.Register(nameof(IsInvalid), typeof(bool), typeof(OHsControlPointTag));
    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(OHsControlPointTag));
    public static readonly DependencyProperty IsMenuOwnerActiveProperty = DependencyProperty.Register(nameof(IsMenuOwnerActive), typeof(bool), typeof(OHsControlPointTag));
    public Resources.DataModels.ControlPointSnapshot? Point { get => (Resources.DataModels.ControlPointSnapshot?)GetValue(PointProperty); set => SetValue(PointProperty, value); }
    public string Label => (string)GetValue(LabelProperty);
    public bool IsInvalid { get => (bool)GetValue(IsInvalidProperty); set => SetValue(IsInvalidProperty, value); }
    public bool IsEditing { get => (bool)GetValue(IsEditingProperty); set => SetValue(IsEditingProperty, value); }
    public bool IsMenuOwnerActive { get => (bool)GetValue(IsMenuOwnerActiveProperty); set => SetValue(IsMenuOwnerActiveProperty, value); }
    internal void RefreshText()
    {
        var text = Point is { } point ? string.Format(CultureInfo.CurrentCulture, "{0} - {1}%", point.Sampleset, point.Volume) : "";
        SetValue(LabelPropertyKey, text);
        AutomationProperties.SetName(this, Point is { } current ? string.Format(CultureInfo.CurrentCulture, "{0}, control point at {1} milliseconds", text, current.Timestamp) : "Control point");
        ToolTip = text;
    }
}
