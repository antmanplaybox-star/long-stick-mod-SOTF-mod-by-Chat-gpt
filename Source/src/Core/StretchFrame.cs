using System.Numerics;

namespace LongStickSpear.Core;

// The native pickup retains its original scale. A separate pivot receives the
// stretch while the loaded model retains its baseline scale and thickness.
public readonly record struct StretchFrame(Vector3 PivotPosition, Quaternion PivotRotation,
    Vector3 PivotScale, Vector3 ModelScale, int Axis)
{
    public static StretchFrame Create(Vector3 position, Quaternion rotation, Vector3 scale, Vector3 size)
    {
        var effective = size * Vector3.Abs(scale);
        var axis = Rules.LongestAxis(effective.X, effective.Y, effective.Z);
        if (Math.Max(effective.X, Math.Max(effective.Y, effective.Z)) <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        var stretch = axis switch { 0 => new Vector3(2, 1, 1), 1 => new Vector3(1, 2, 1), _ => new Vector3(1, 1, 2) };
        return new(position, rotation, stretch, scale, axis);
    }
}
