using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Itsqmet.ExamAgent;

internal static class WindowsMonitor
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static string? ForegroundProcess()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return null;
            GetWindowThreadProcessId(h, out var pid);
            return Process.GetProcessById((int)pid).ProcessName + ".exe";
        }
        catch { return null; }
    }

    public static (string Base64, int Width, int Height)? CaptureJpeg()
    {
        try
        {
            var bounds = SystemInformation.VirtualScreen;
            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            Image image = bmp;
            Bitmap? scaled = null;
            if (bmp.Width > 1600)
            {
                var height = (int)Math.Round(bmp.Height * 1600d / bmp.Width);
                scaled = new Bitmap(bmp, new Size(1600, height));
                image = scaled;
            }
            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Jpeg);
            var result = (Convert.ToBase64String(ms.ToArray()), image.Width, image.Height);
            scaled?.Dispose();
            return result;
        }
        catch { return null; }
    }
}
