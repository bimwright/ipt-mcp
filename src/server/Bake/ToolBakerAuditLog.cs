using System;
using System.IO;
using Newtonsoft.Json;

namespace Bimwright.Inventor.Server.Bake;

public sealed class ToolBakerAuditLog
{
    private readonly string _path;

    public ToolBakerAuditLog(string path)
    {
        _path = path;
    }

    public void Append(string eventName, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        File.AppendAllText(_path, JsonConvert.SerializeObject(new
        {
            ts_utc = DateTimeOffset.UtcNow.ToString("o"),
            event_name = eventName,
            payload
        }) + Environment.NewLine);
    }
}
