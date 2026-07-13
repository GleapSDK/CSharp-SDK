using System.Collections.Generic;
using System.Linq;

namespace GleapSDK.Data;

/// <summary>The set of tags applied to the next report (replace-on-set).</summary>
public sealed class TagStore
{
    private IReadOnlyList<string> _tags = new List<string>();

    public void Set(IEnumerable<string> tags) => _tags = tags.ToList();

    public IReadOnlyList<string> Snapshot() => _tags;
}
