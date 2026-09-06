namespace DualSenseVoice;

internal sealed class LongPressDetector
{
    internal const long ThresholdMilliseconds = 600;
    long? pressedAt;
    long? lastReport;
    bool armed;

    internal bool Update(bool pressed, long now)
    {
        // A stalled/disconnected device must not turn elapsed downtime into a hold.
        if (lastReport.HasValue && now - lastReport.Value > 250)
        {
            pressedAt = null;
            armed = false;
        }
        lastReport = now;
        if (!pressed) { pressedAt = null; armed = true; return false; }
        if (!armed) return false;
        pressedAt ??= now;
        if (now - pressedAt.Value < ThresholdMilliseconds) return false;
        armed = false;
        return true;
    }
}
