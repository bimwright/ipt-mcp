using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Security;

/// <summary>Code bodies are represented by length and hash in every persisted call log.</summary>
public static class CallLogPrivacy
{
    public static JToken Redact(JToken input)
    {
        var copy = input.DeepClone();
        Visit(copy);
        return copy;
    }

    private static void Visit(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (new[] { "auth_token", "access_token", "refresh_token", "id_token", "api_key", "apikey", "client_secret", "password", "passwd", "pwd", "secret", "token", "authorization" }.Contains(property.Name, System.StringComparer.OrdinalIgnoreCase))
                    property.Value = "***";
                else if ((property.Name == "code" || property.Name == "source_code") && property.Value.Type == JTokenType.String)
                {
                    var body = property.Value.Value<string>() ?? "";
                    property.Value = new JObject { ["length"] = body.Length, ["sha256"] = SendCodeSource.Hash(body) };
                }
                else Visit(property.Value);
            }
        }
        else if (token is JArray array)
            foreach (var item in array) Visit(item);
        else if (token is JValue value && value.Type == JTokenType.String)
            value.Value = BakeRedactor.RedactForBake(value.Value<string>());
    }
}
