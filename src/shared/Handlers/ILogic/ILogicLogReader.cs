using System;
using System.IO;
using System.Text;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

internal static class ILogicLogReader
{
    internal const int MaxTailChars = 1048576;
    internal sealed class Tail
    {
        internal string Text { get; }
        internal bool Truncated { get; }
        internal long Length { get; }
        internal long BytesRead { get; }
        internal Tail(string text, bool truncated, long length, long bytesRead)
        { Text = text; Truncated = truncated; Length = length; BytesRead = bytesRead; }
    }

    internal static Tail Read(string path, int maxChars)
    {
        if (maxChars < 0 || maxChars > MaxTailChars) throw new ArgumentOutOfRangeException(nameof(maxChars));
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = fs.Length;
        if (maxChars == 0)
        {
            using var reader = new StreamReader(fs, Encoding.UTF8, true);
            return new Tail(reader.ReadToEnd(), false, length, fs.Position);
        }
        var header = new byte[4];
        var headerCount = fs.Read(header, 0, (int)Math.Min(header.Length, length));
        Encoding encoding = Encoding.UTF8;
        int bom = 0, unit = 1;
        if (headerCount >= 4 && header[0] == 0xff && header[1] == 0xfe && header[2] == 0 && header[3] == 0)
        { encoding = Encoding.UTF32; bom = 4; unit = 4; }
        else if (headerCount >= 4 && header[0] == 0 && header[1] == 0 && header[2] == 0xfe && header[3] == 0xff)
        { encoding = new UTF32Encoding(true, true); bom = 4; unit = 4; }
        else if (headerCount >= 3 && header[0] == 0xef && header[1] == 0xbb && header[2] == 0xbf) bom = 3;
        else if (headerCount >= 2 && header[0] == 0xff && header[1] == 0xfe)
        { encoding = Encoding.Unicode; bom = 2; unit = 2; }
        else if (headerCount >= 2 && header[0] == 0xfe && header[1] == 0xff)
        { encoding = Encoding.BigEndianUnicode; bom = 2; unit = 2; }

        var start = Math.Max(bom, length - (4L * maxChars + 4));
        start -= (start - bom) % unit;
        fs.Seek(start, SeekOrigin.Begin);
        var bytes = new byte[(int)(length - start)];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = fs.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        var skip = 0;
        if (unit == 1 && start > bom)
            while (skip < count && (bytes[skip] & 0xc0) == 0x80) skip++;
        var text = encoding.GetString(bytes, skip, count - skip);
        var truncated = start > bom || text.Length > maxChars;
        if (text.Length > maxChars) text = text.Substring(text.Length - maxChars);
        if (text.Length > 0 && char.IsLowSurrogate(text[0])) text = text.Substring(1);
        return new Tail(text, truncated, length, headerCount + count);
    }
}
