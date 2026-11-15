namespace LidarViewer.Core.Readers;

internal static class Lzf
{
    public static byte[] Decompress(ReadOnlySpan<byte> input, int outputLength)
    {
        var output = new byte[outputLength];
        int ip = 0, op = 0;

        while (ip < input.Length)
        {
            int ctrl = input[ip++];
            if (ctrl < 32)
            {
                int run = ctrl + 1;
                if (ip + run > input.Length || op + run > outputLength)
                    throw new InvalidDataException("Uszkodzone dane LZF (bajty dosłowne).");
                input.Slice(ip, run).CopyTo(output.AsSpan(op));
                ip += run;
                op += run;
            }
            else
            {
                int len = ctrl >> 5;
                if (len == 7)
                {
                    if (ip >= input.Length) throw new InvalidDataException("Uszkodzone dane LZF (długość).");
                    len += input[ip++];
                }
                if (ip >= input.Length) throw new InvalidDataException("Uszkodzone dane LZF (przesunięcie).");
                int reference = op - ((ctrl & 0x1f) << 8) - 1 - input[ip++];
                len += 2;
                if (reference < 0 || op + len > outputLength)
                    throw new InvalidDataException("Uszkodzone dane LZF (odwołanie poza zakresem).");
                for (int k = 0; k < len; k++)
                    output[op++] = output[reference++];
            }
        }

        if (op != outputLength)
            throw new InvalidDataException($"Dane LZF mają {op} B, oczekiwano {outputLength} B.");
        return output;
    }
}
