using System.Collections.Generic;

namespace Bimwright.Inventor.Server.Bake;

public sealed class ClusterCandidate
{
    public string ClusterKey { get; set; } = "";
    public string Source { get; set; } = "";
    public string Tool { get; set; } = "";
    public int Count { get; set; }
}

public sealed class ClusterEngine
{
    public IReadOnlyList<ClusterCandidate> Cluster(IEnumerable<ClusterCandidate> candidates)
        => new List<ClusterCandidate>(candidates ?? new ClusterCandidate[0]);
}
