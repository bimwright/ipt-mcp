#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
#nullable disable
using System;
using System.IO;

namespace Bimwright.Ipt.Shared.Views.Toast
{
    public static class ToastThumbnailLoader
    {
        public static byte[] TryLoadBytes(string path, long maxBytes)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                if (!File.Exists(path))
                    return null;

                var info = new FileInfo(path);
                if (info.Length > maxBytes)
                    return null;

                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }
    }
}
#endif
