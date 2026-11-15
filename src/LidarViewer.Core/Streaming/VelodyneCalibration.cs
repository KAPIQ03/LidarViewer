namespace LidarViewer.Core.Streaming;

public sealed record LaserCalibration(
    float RotationCorrection,
    float VerticalAngle,
    float DistanceCorrection = 0f,
    float VerticalOffset = 0f,
    float HorizontalOffset = 0f);

public sealed class VelodyneCalibration
{
    private static readonly float[] Vlp16Angles =
        [-15, 1, -13, 3, -11, 5, -9, 7, -7, 9, -5, 11, -3, 13, -1, 15];

    private static readonly float[] Hdl32EAngles =
    [
        -30.67f, -9.33f, -29.33f, -8.00f, -28.00f, -6.66f, -26.66f, -5.33f,
        -25.33f, -4.00f, -24.00f, -2.67f, -22.67f, -1.33f, -21.33f, 0.00f,
        -20.00f, 1.33f, -18.67f, 2.67f, -17.33f, 4.00f, -16.00f, 5.33f,
        -14.67f, 6.67f, -13.33f, 8.00f, -12.00f, 9.33f, -10.67f, 10.67f,
    ];

    public VelodyneCalibration(string name, IReadOnlyList<LaserCalibration> lasers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(lasers);

        if (lasers.Count is not (16 or 32 or 64))
            throw new NotSupportedException(
                $"Kalibracja ma {lasers.Count} laserów. Obsługiwane układy: 16 (VLP-16), 32 (HDL-32E/VLP-32), 64 (HDL-64E).");

        Name = name;
        Lasers = lasers.ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<LaserCalibration> Lasers { get; }

    public static VelodyneCalibration Vlp16 { get; } = FromVerticalAngles("VLP-16", Vlp16Angles);

    public static VelodyneCalibration Hdl32E { get; } = FromVerticalAngles("HDL-32E", Hdl32EAngles);

    public static VelodyneCalibration FromVerticalAngles(string name, IReadOnlyList<float> anglesDegrees) =>
        new(name, anglesDegrees.Select(angle => new LaserCalibration(0f, angle)).ToArray());
}
