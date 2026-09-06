using HidSharp;

namespace DualSenseVoice;

internal sealed record DualSenseUsbDevice(string DevicePath, string FriendlyName);

internal sealed class DualSenseTouchpadButtonMonitor : IDisposable
{
    private const int VendorId = 0x054C;
    private const int ProductId = 0x0CE6;
    // buttons[2], bit 1: physical touchpad click (not touch contact).
    private const int UsbButtons2Offset = 10;
    private const byte TouchpadButtonMask = 0x02;

    private readonly DualSenseBluetoothCapture.RawInputReceiver rawInput;
    private readonly LongPressDetector longPress = new();

    internal event EventHandler? TouchpadButtonPressed;

    private DualSenseTouchpadButtonMonitor(string devicePath)
    {
        rawInput = new DualSenseBluetoothCapture.RawInputReceiver(
            devicePath,
            ProcessRawReport);
    }

    internal static IReadOnlyList<DualSenseUsbDevice> EnumerateConnectedUsb()
    {
        var results = new List<DualSenseUsbDevice>();
        foreach (HidDevice device in DeviceList.Local.GetHidDevices())
        {
            if (!IsDualSense(device) ||
                device.GetMaxInputReportLength() < 64 ||
                device.GetMaxOutputReportLength() >= 100)
                continue;

            string product;
            try { product = device.GetProductName(); }
            catch { product = "DualSense Wireless Controller"; }
            results.Add(new DualSenseUsbDevice(
                device.DevicePath,
                string.IsNullOrWhiteSpace(product)
                    ? "DualSense Wireless Controller"
                    : product));
        }

        return results;
    }

    internal static DualSenseTouchpadButtonMonitor Connect(string devicePath) => new(devicePath);

    private static bool IsDualSense(HidDevice device)
    {
        string path = device.DevicePath;
        return (device.VendorID == VendorId && device.ProductID == ProductId) ||
               path.Contains("pid_0ce6", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("pid&0ce6", StringComparison.OrdinalIgnoreCase);
    }

    private void ProcessRawReport(byte[] report)
    {
        if (!IsTouchpadButtonReport(report)) return;
        bool pressed = HasTouchpadButtonPressed(report);
        if (longPress.Update(pressed, Environment.TickCount64))
            TouchpadButtonPressed?.Invoke(this, EventArgs.Empty);
    }

    internal static bool IsTouchpadButtonReport(ReadOnlySpan<byte> report) =>
        report.Length > UsbButtons2Offset && report[0] == 0x01;

    internal static bool HasTouchpadButtonPressed(ReadOnlySpan<byte> report) =>
        IsTouchpadButtonReport(report) &&
        (report[UsbButtons2Offset] & TouchpadButtonMask) != 0;

    public void Dispose() => rawInput.Dispose();
}
