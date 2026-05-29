using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Bimwright.Ipt.Shared.Contracts;

public readonly struct BoundedLineReadResult
{
    public BoundedLineReadResult(string? line, bool overflow)
    {
        Line = line;
        Overflow = overflow;
    }

    public string? Line { get; }
    public bool Overflow { get; }
}

public static class NdjsonLineReader
{
    public static string? ReadLineBounded(StreamReader reader, int maxBytes, out bool overflow)
    {
        overflow = false;
        var sb = new StringBuilder();
        int count = 0;
        while (true)
        {
            int ch = reader.Read();
            if (ch == -1) return sb.Length == 0 ? null : sb.ToString();
            if (ch == '\n') return sb.ToString();
            if (ch == '\r') continue;
            count++;
            if (count > maxBytes) { overflow = true; return null; }
            sb.Append((char)ch);
        }
    }

    public static async Task<BoundedLineReadResult> ReadLineBoundedAsync(StreamReader reader, int maxBytes)
    {
        var sb = new StringBuilder();
        var buffer = new char[1];
        int count = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, 1);
            if (read == 0) return new BoundedLineReadResult(sb.Length == 0 ? null : sb.ToString(), false);
            var ch = buffer[0];
            if (ch == '\n') return new BoundedLineReadResult(sb.ToString(), false);
            if (ch == '\r') continue;
            count++;
            if (count > maxBytes) return new BoundedLineReadResult(null, true);
            sb.Append(ch);
        }
    }
}
