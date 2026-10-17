using Terraria.DataStructures;

namespace AE2ForTerraria.Systems;

internal sealed class TerminalReadState
{
    private static int _nextEpoch;
    public Point16 Position { get; private set; } = new(-1, -1);
    public int Epoch { get; private set; }
    public bool Waiting { get; private set; }
    public bool Denied { get; private set; }
    public int Level { get; private set; }
    public bool Readable => !Waiting && !Denied && Level >= NetworkRegistry.LevelReadOnly;
    public bool ShouldRequest => Position.X >= 0 && !Denied;
    public void Begin(Point16 position)
    { Position = position; _nextEpoch = _nextEpoch == int.MaxValue ? 1 : _nextEpoch + 1; Epoch = _nextEpoch; Waiting = true; Denied = false; Level = 0; }
    public bool Apply(Point16 position, int epoch, int level)
    {
        if (position != Position || epoch != Epoch || Denied) return false;
        Level = level; Waiting = false; Denied = level < NetworkRegistry.LevelReadOnly;
        return true;
    }
    public void Reset() { Position = new(-1, -1); Level = 0; Waiting = false; Denied = false; }
}
