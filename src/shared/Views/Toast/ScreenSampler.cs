#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// Average colour of a screen rectangle (every 4th pixel in both directions). The capture stays in
/// memory and is disposed immediately. It is never saved or logged (privacy note in the Status dialog).
/// </summary>
internal static class ScreenSampler
{
    public static Rgb? Average(PxRect r)
    {
        if (r.IsEmpty) return null;
        try
        {
            using var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));

            var data = bmp.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                long red = 0, green = 0, blue = 0, n = 0;
                var row = new byte[data.Stride];
                for (var y = 0; y < r.Height; y += 4)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                    for (var x = 0; x < r.Width; x += 4)
                    {
                        var i = x * 4;                 // BGRA
                        blue += row[i];
                        green += row[i + 1];
                        red += row[i + 2];
                        n++;
                    }
                }
                return n == 0 ? null : new Rgb((byte)(red / n), (byte)(green / n), (byte)(blue / n));
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
        catch
        {
            return null;   // capture can fail (secure desktop, disconnected session): fall back in the chooser
        }
    }
}
#endif
