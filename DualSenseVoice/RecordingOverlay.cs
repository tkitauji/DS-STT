using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DualSenseVoice;

internal enum RecordingOverlayState { Ready, Recording, Processing, Attention }

internal sealed class RecordingOverlay : Window
{
    readonly TextBlock icon = new() { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 28, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock label = new() { FontSize = 14, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock slash = new() { Text = "／", Foreground = Brushes.LightGray, FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

    internal RecordingOverlay()
    {
        Title = "DS-STT 録音状態";
        Width = 180;
        Height = 54;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        IsHitTestVisible = false;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var iconPanel = new Grid();
        iconPanel.Children.Add(icon);
        iconPanel.Children.Add(slash);
        panel.Children.Add(iconPanel);
        panel.Children.Add(label);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 25, 28, 34)), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Child = panel };
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            var handle = source.Handle;
            // Layered window + TRANSPARENT passes mouse input to other processes.
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x20 | 0x80 | 0x08000000));
            source.AddHook((IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled) =>
            {
                if (message == 0x21) { handled = true; return new IntPtr(3); }
                return IntPtr.Zero;
            });
        };
        SetState(RecordingOverlayState.Attention);
        Place(false, false);
    }

    internal void Place(bool left, bool large)
    {
        Width = large ? 225 : 180;
        Height = large ? 68 : 54;
        icon.FontSize = large ? 36 : 28;
        label.FontSize = large ? 18 : 14;
        Rect area = SystemParameters.WorkArea;
        Left = left ? area.Left + 20 : area.Right - Width - 20;
        Top = area.Top + 80;
    }

    internal void SetState(RecordingOverlayState state)
    {
        var (glyph, text, color) = state switch
        {
            RecordingOverlayState.Ready => ("\uE720", "待機", Brushes.LightGray),
            RecordingOverlayState.Recording => ("\uE720", "● 録音中", Brushes.OrangeRed),
            RecordingOverlayState.Processing => ("\uE895", "文字起こし中", Brushes.LightSkyBlue),
            _ => ("\uE7BA", "要確認", Brushes.Gold)
        };
        icon.Text = glyph;
        slash.Visibility = state == RecordingOverlayState.Ready ? Visibility.Visible : Visibility.Collapsed;
        label.Text = text;
        icon.Foreground = label.Foreground = color;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
