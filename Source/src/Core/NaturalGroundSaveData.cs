namespace LongStickSpear.Core;

public sealed class NaturalGroundSaveData
{
    // Compatibility shape for v0.3.2 saves. New saves always leave Collected empty.
    public int Version { get; set; } = 1;
    public List<string> Collected { get; set; } = new();
}
