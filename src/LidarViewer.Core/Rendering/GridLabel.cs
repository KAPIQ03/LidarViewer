using System.Numerics;

namespace LidarViewer.Core.Rendering;

public readonly record struct GridLabel(Vector3 Position, string Text);

public readonly record struct ScreenLabel(float X, float Y, string Text);
