using System.Buffers.Binary;

namespace LidarViewer.Core.Readers;

internal enum ScalarType { Int8, UInt8, Int16, UInt16, Int32, UInt32, Int64, UInt64, Float32, Float64 }

internal static class ScalarTypes
{
    public static int SizeOf(ScalarType t) => t switch
    {
        ScalarType.Int8 or ScalarType.UInt8 => 1,
        ScalarType.Int16 or ScalarType.UInt16 => 2,
        ScalarType.Int32 or ScalarType.UInt32 or ScalarType.Float32 => 4,
        _ => 8,
    };

    public static double Read(ReadOnlySpan<byte> b, ScalarType t, bool bigEndian) => t switch
    {
        ScalarType.Int8 => (sbyte)b[0],
        ScalarType.UInt8 => b[0],
        ScalarType.Int16 => bigEndian ? BinaryPrimitives.ReadInt16BigEndian(b) : BinaryPrimitives.ReadInt16LittleEndian(b),
        ScalarType.UInt16 => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(b) : BinaryPrimitives.ReadUInt16LittleEndian(b),
        ScalarType.Int32 => bigEndian ? BinaryPrimitives.ReadInt32BigEndian(b) : BinaryPrimitives.ReadInt32LittleEndian(b),
        ScalarType.UInt32 => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(b) : BinaryPrimitives.ReadUInt32LittleEndian(b),
        ScalarType.Int64 => bigEndian ? BinaryPrimitives.ReadInt64BigEndian(b) : BinaryPrimitives.ReadInt64LittleEndian(b),
        ScalarType.UInt64 => bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(b) : BinaryPrimitives.ReadUInt64LittleEndian(b),
        ScalarType.Float32 => bigEndian ? BinaryPrimitives.ReadSingleBigEndian(b) : BinaryPrimitives.ReadSingleLittleEndian(b),
        _ => bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(b) : BinaryPrimitives.ReadDoubleLittleEndian(b),
    };

    public static byte ToByte(double v) => double.IsNaN(v) ? (byte)0 : (byte)Math.Clamp(Math.Round(v), 0, 255);
}
