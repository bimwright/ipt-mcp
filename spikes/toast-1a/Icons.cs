// PROTOTYPE — throwaway toast compatibility spike (roadmap Phase 1a).
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ToastSpike
{
    /// Two ways to get a stdole.IPictureDisp for Inventor ribbon icons; the spike checks which works per tier.
    internal static class Icons
    {
        public static Bitmap Make(int size, Color bg, string letter)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(bg);
                using (var f = new Font("Segoe UI", size * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(letter, f, Brushes.White, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }

        /// Variant 1: WinForms AxHost helper (the classic net48 Inventor sample route).
        private sealed class AxConv : System.Windows.Forms.AxHost
        {
            private AxConv() : base("") { }
            public static object ToPic(Image img) => GetIPictureDispFromPicture(img);
        }
        public static object ViaAxHost(Image img) => AxConv.ToPic(img);

        /// Variant 2: OleCreatePictureIndirect on an HBITMAP (no WinForms dependency).
        [StructLayout(LayoutKind.Sequential)]
        private struct PICTDESC { public int cbSizeOfStruct; public int picType; public IntPtr hbitmap; public IntPtr hpal; }
        [DllImport("oleaut32.dll", PreserveSig = false)]
        private static extern void OleCreatePictureIndirect(ref PICTDESC desc, ref Guid riid, bool fOwn, [MarshalAs(UnmanagedType.IUnknown)] out object pic);
        private static Guid IID_IPictureDisp = new Guid("7BF80981-BF32-101A-8BBB-00AA00300CAB");

        public static object ViaOle(Bitmap bmp)
        {
            var d = new PICTDESC { cbSizeOfStruct = Marshal.SizeOf<PICTDESC>(), picType = 1, hbitmap = bmp.GetHbitmap(), hpal = IntPtr.Zero };
            OleCreatePictureIndirect(ref d, ref IID_IPictureDisp, true, out var pic);
            return pic;
        }
    }
}
