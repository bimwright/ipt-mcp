using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server.Bake;

public sealed class SuggestionProposer
{
    public IReadOnlyList<BakeSuggestionRecord> Propose(IEnumerable<ClusterCandidate> candidates, IEnumerable<BakeSuggestionRecord>? existing = null)
    {
        var existingKeys = new HashSet<string>((existing ?? Array.Empty<BakeSuggestionRecord>()).Select(s => s.ClusterKey));
        return (candidates ?? Array.Empty<ClusterCandidate>())
            .Where(c => c.Count >= 3 && !existingKeys.Contains(c.ClusterKey))
            .Select(c => new BakeSuggestionRecord
            {
                Id = "sug_" + Guid.NewGuid().ToString("N"),
                ClusterKey = c.ClusterKey,
                Source = c.Source ?? "preset",
                Title = "Bake repeated " + c.Tool,
                Description = "Repeated Inventor workflow detected.",
                State = BakeSuggestionStates.Open,
                Score = c.Count,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
                UpdatedAt = DateTimeOffset.UtcNow.ToString("o"),
                PayloadJson = new JObject { ["tool"] = c.Tool }.ToString()
            })
            .ToArray();
    }
}
