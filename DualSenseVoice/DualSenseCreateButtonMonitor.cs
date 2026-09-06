using HidSharp;

namespace DualSenseVoice;

internal sealed record DualSenseUsbDevice(string DevicePath, string FriendlyName);

internal sealed class DualSenseCreateButtonMonitor : IDisposable
{
    private const int VendorId = 0x054C;
    private const int ProductId = 0x0CE6;
    // buttons[1], bit 4 (Create); USB payload starts after report ID.
    private const int UsbButtons1Offset = 9;
    private const byte CreateButtonMask = 0x10;

    private readonly DualSenseBluetoothCapture.RawInputReceiver rawInput;
    private bool previousPressed;

    internal event EventHandler? CreateButtonPressed;

    private DualSenseCreateButtonMonitor(string devicePath)
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

    internal static DualSenseCreateButtonMonitor Connect(string devicePath) => new(devicePath);

    private static bool IsDualSense(HidDevice device)
    {
        string path = device.DevicePath;
        return (device.VendorID == VendorId && device.ProductID == ProductId) ||
               path.Contains("pid_0ce6", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("pid&0ce6", StringComparison.OrdinalIgnoreCase);
    }

    private void ProcessRawReport(byte[] report)
    {
        if (!IsCreateButtonReport(report)) return;
        bool pressed = HasCreateButtonPressed(report);
        if (pressed && !previousPressed)
            CreateButtonPressed?.Invoke(this, EventArgs.Empty);
        previousPressed = pressed;
    }

    internal static bool IsCreateButtonReport(ReadOnlySpan<byte> report) =>
        report.Length > UsbButtons1Offset && report[0] == 0x01;

    internal static bool HasCreateButtonPressed(ReadOnlySpan<byte> report) =>
        IsCreateButtonReport(report) &&
        (report[UsbButtons1Offset] & CreateButtonMask) != 0;

    public void Dispose() => rawInput.Dispose();
}
