using System.Collections.Generic;
using Terraria.DataStructures;

namespace AE2ForTerraria.Systems;

/// <summary>The lock policy belongs to one server-authorized terminal read session.</summary>
internal sealed class TerminalPolicyState
{
    private readonly HashSet<int> _locked = new();
    private Point16 _position = new(-1, -1);
    private int _epoch;
    public Point16 Anchor { get; private set; } = new(-1, -1);
    public bool Matches(Point16 position, int epoch) => epoch > 0 && _epoch == epoch && _position == position;
    public bool IsLocked(int type) => _locked.Contains(type);
    public void Apply(Point16 position, int epoch, Point16 anchor, IEnumerable<int> locked)
    {
        _position = position; _epoch = epoch; Anchor = anchor;
        _locked.Clear();
        foreach (int type in locked) _locked.Add(type);
    }
    public void Reset() { _position = new(-1, -1); _epoch = 0; Anchor = new(-1, -1); _locked.Clear(); }
}
