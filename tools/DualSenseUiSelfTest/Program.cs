using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DualSenseVoice;

internal static class Program
{
    private const string ChoiceName =
        "Wireless Controller — Bluetooth（直接）";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException(
                "Usage: DualSenseUiSelfTest <output.png>");

        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        var deviceBox = (ComboBox)window.FindName("DeviceBox");
        deviceBox.ItemsSource = new[] { new SnapshotChoice(ChoiceName) };
        deviceBox.SelectedIndex = 0;

        var content = (FrameworkElement)window.Content;
        window.Content = null;
        const int width = 760;
        const int height = 570;
        var host = new Grid
        {
            Width = width,
            Height = height,
            Background = (Brush)app.Resources["Canvas"],
        };
        TextElement.SetForeground(host, (Brush)app.Resources["Ink"]);
        host.Children.Add(content);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        deviceBox.ApplyTemplate();

        var selectionText = deviceBox.Template.FindName(
            "SelectionText",
            deviceBox) as TextBlock;
        Assert(selectionText?.Text == ChoiceName,
            "The closed device selector did not render FriendlyName.");
        Assert(deviceBox.Template.FindName("PART_Popup", deviceBox) is Popup,
            "The device selector lost its required popup template part.");

        var transcript = (TextBox)window.FindName("TranscriptBox");
        Assert(
            ScrollViewer.GetVerticalScrollBarVisibility(transcript) ==
            ScrollBarVisibility.Hidden,
            "The empty transcript field should not show a bright system scrollbar.");

        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(host);
        int stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        int nonBackgroundPixels = CountNonBackgroundPixels(pixels, stride);
        Assert(nonBackgroundPixels > 100_000,
            $"The UI snapshot was blank or incomplete: {nonBackgroundPixels} non-background pixels.");

        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(output))
            encoder.Save(stream);

        Console.WriteLine(
            $"UI_SELF_TEST_PASS|selection={selectionText!.Text}|nonBackgroundPixels={nonBackgroundPixels}|png={output}");
        var overlay = new RecordingOverlay();
        IntPtr foreground = GetForegroundWindow();
        overlay.Show();
        long styles = GetWindowLongPtr(new WindowInteropHelper(overlay).Handle, -20).ToInt64();
        Assert((styles & 0x080000A0) == 0x080000A0,
            "Overlay must be non-activating, click-through, and absent from the taskbar.");
        Assert(GetForegroundWindow() == foreground, "Showing overlay stole focus.");
        foreach (RecordingOverlayState state in Enum.GetValues<RecordingOverlayState>())
        {
            overlay.SetState(state);
            overlay.UpdateLayout();
            var overlayBitmap = new RenderTargetBitmap(180, 54, 96, 96, PixelFormats.Pbgra32);
            overlayBitmap.Render(overlay);
            var overlayEncoder = new PngBitmapEncoder();
            overlayEncoder.Frames.Add(BitmapFrame.Create(overlayBitmap));
            using var overlayStream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, $"overlay-{state}.png"));
            overlayEncoder.Save(overlayStream);
        }
        overlay.Close();
        Console.WriteLine("OVERLAY_TEST_PASS|native styles and foreground preservation");
        window.Close();
        app.Shutdown();
    }

    private static int CountNonBackgroundPixels(byte[] pixels, int stride)
    {
        byte blue = pixels[0];
        byte green = pixels[1];
        byte red = pixels[2];
        byte alpha = pixels[3];
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != blue ||
                pixels[offset + 1] != green ||
                pixels[offset + 2] != red ||
                pixels[offset + 3] != alpha)
                count++;
        }
        return count;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record SnapshotChoice(string FriendlyName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
}
