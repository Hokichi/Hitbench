using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using Hitbench.Core.Entities;
namespace Hitbench.Resources.CustomControls;

public sealed class OHsColorPicker : Control
{
    public static readonly DependencyProperty SelectedColorProperty=DependencyProperty.Register(nameof(SelectedColor),typeof(string),typeof(OHsColorPicker),new FrameworkPropertyMetadata("#FF0000",FrameworkPropertyMetadataOptions.BindsTwoWayByDefault|FrameworkPropertyMetadataOptions.AffectsRender,OnColorChanged),v=>RgbColor.TryParse(v as string,out _));
    public static readonly DependencyProperty HueProperty=DependencyProperty.Register(nameof(Hue),typeof(double),typeof(OHsColorPicker),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender,(d,e)=>((OHsColorPicker)d).UpdateRgb()),v=>v is double h && double.IsFinite(h) && h is >=0 and <=360);
    public string SelectedColor { get=>(string)GetValue(SelectedColorProperty); set=>SetValue(SelectedColorProperty,value); }
    public double Hue { get=>(double)GetValue(HueProperty); set=>SetValue(HueProperty,value); }
    private double saturation=1, value=1;
    private bool updating;
    public OHsColorPicker()
    {
        Focusable=true;
        IsEnabledChanged+=(_,_)=>{ if(!IsEnabled && IsMouseCaptured) ReleaseMouseCapture(); };
    }
    private static void OnColorChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        var picker=(OHsColorPicker)d;
        if(picker.updating) return;
        RgbColor.TryParse((string)e.NewValue,out var rgb); var hsv=rgb.ToHsv();
        picker.updating=true;
        try { picker.saturation=hsv.Saturation; picker.value=hsv.Value; if(hsv.Saturation>0 && hsv.Value>0) picker.SetCurrentValue(HueProperty,hsv.Hue); }
        finally { picker.updating=false; }
    }
    private void UpdateRgb()
    {
        if(updating) return;
        updating=true;
        try { SetCurrentValue(SelectedColorProperty,RgbColor.FromHsv(Hue,saturation,value).ToHex()); }
        finally { updating=false; }
        InvalidateVisual();
    }
    public void SelectAt(Point point)
    {
        if(!IsEnabled || ActualWidth<=0 || ActualHeight<=0 || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        var x=Math.Clamp(point.X/ActualWidth,0,1);
        saturation=FlowDirection==FlowDirection.RightToLeft?1-x:x;
        value=1-Math.Clamp(point.Y/ActualHeight,0,1); UpdateRgb();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); if(!IsEnabled)return; Focus(); CaptureMouse(); SelectAt(e.GetPosition(this)); e.Handled=true; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if(IsMouseCaptured) SelectAt(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if(IsMouseCaptured){ SelectAt(e.GetPosition(this)); ReleaseMouseCapture(); e.Handled=true; } base.OnMouseLeftButtonUp(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if(!IsEnabled)return;
        var step=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.1:.01;
        var direction=FlowDirection==FlowDirection.RightToLeft?-1:1;
        switch(e.Key) { case Key.Left:saturation=Math.Clamp(saturation-step*direction,0,1);break; case Key.Right:saturation=Math.Clamp(saturation+step*direction,0,1);break; case Key.Up:value=Math.Clamp(value+step,0,1);break; case Key.Down:value=Math.Clamp(value-step,0,1);break; default:return; }
        UpdateRgb(); e.Handled=true;
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new OHsColorPickerAutomationPeer(this);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if(ActualWidth<=0 || ActualHeight<=0)return;
        var rgb=RgbColor.FromHsv(Hue,1,1); var hue=Color.FromRgb(rgb.R,rgb.G,rgb.B);
        // Spectrum endpoints are color data, rather than application chrome.
        var spectrum=new LinearGradientBrush(Colors.White,hue,0);
        if(FlowDirection==FlowDirection.RightToLeft){spectrum.StartPoint=new Point(1,.5);spectrum.EndPoint=new Point(0,.5);}
        dc.DrawRectangle(spectrum,null,new Rect(RenderSize));
        dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent,Colors.Black,90),null,new Rect(RenderSize));
        var edge=SystemParameters.HighContrast?SystemColors.WindowTextBrush:BorderBrush;
        dc.DrawRectangle(null,new Pen(edge,1),new Rect(.5,.5,Math.Max(0,ActualWidth-1),Math.Max(0,ActualHeight-1)));
        var x=(FlowDirection==FlowDirection.RightToLeft?1-saturation:saturation)*ActualWidth;
        var point=new Point(Math.Clamp(x,5,Math.Max(5,ActualWidth-5)),Math.Clamp((1-value)*ActualHeight,5,Math.Max(5,ActualHeight-5)));
        var outer=SystemParameters.HighContrast?SystemColors.WindowBrush:TryFindResource("Brush.Background.Main") as Brush ?? SystemColors.WindowBrush;
        var inner=SystemParameters.HighContrast?SystemColors.WindowTextBrush:TryFindResource("Brush.Text.Primary") as Brush ?? SystemColors.WindowTextBrush;
        dc.DrawEllipse(null,new Pen(outer,4),point,4,4);
        dc.DrawEllipse(null,new Pen(inner,2),point,4,4);
    }
}
