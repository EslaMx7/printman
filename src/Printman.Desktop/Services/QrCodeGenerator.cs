using System.Text;

namespace Printman.Desktop.Services;

/// <summary>
/// Minimal dependency-free ISO 18004 QR Code encoder (Byte mode, ECC Level L, Versions 1..10).
/// </summary>
public static class QrCodeGenerator
{
    // Total data codewords and EC codewords per block for Versions 1..10 at ECC Level L.
    // Tuple: (TotalDataCodewords, EcCodewordsPerBlock, Group1Blocks, Group1DataCw, Group2Blocks, Group2DataCw)
    private static readonly (int TotalData, int EcPerBlock, int G1Blocks, int G1Data, int G2Blocks, int G2Data)[] EccLTable =
    [
        (0, 0, 0, 0, 0, 0),       // 0 unused
        (19, 7, 1, 19, 0, 0),     // V1 (21x21)
        (34, 10, 1, 34, 0, 0),    // V2 (25x25)
        (55, 15, 1, 55, 0, 0),    // V3 (29x29)
        (80, 20, 1, 80, 0, 0),    // V4 (33x33)
        (108, 26, 1, 108, 0, 0),  // V5 (37x37)
        (136, 18, 2, 68, 0, 0),   // V6 (41x41)
        (156, 20, 2, 78, 0, 0),   // V7 (45x45)
        (194, 24, 2, 97, 0, 0),   // V8 (49x49)
        (232, 30, 2, 116, 0, 0),  // V9 (53x53)
        (274, 18, 2, 68, 2, 69),  // V10 (57x57)
    ];

    private static readonly int[][] AlignmentCenters =
    [
        [],
        [],
        [6, 18],
        [6, 22],
        [6, 26],
        [6, 30],
        [6, 34],
        [6, 22, 38],
        [6, 24, 42],
        [6, 26, 46],
        [6, 28, 50],
    ];

    public static bool[,]? Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        byte[] payload = Encoding.UTF8.GetBytes(text);
        int version = -1;
        for (int v = 1; v <= 10; v++)
        {
            int ccBits = v <= 9 ? 8 : 16;
            int headerBits = 4 + ccBits;
            int capacityBytes = (EccLTable[v].TotalData * 8 - headerBits) / 8;
            if (payload.Length <= capacityBytes)
            {
                version = v;
                break;
            }
        }

        if (version < 0)
        {
            return null;
        }

        var spec = EccLTable[version];
        var dataCodewords = BuildDataCodewords(payload, version, spec.TotalData);
        var allCodewords = AddErrorCorrectionAndInterleave(dataCodewords, spec);

        int size = version * 4 + 17;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];

        DrawFunctionPatterns(modules, isFunction, version, size);
        DrawCodewords(modules, isFunction, allCodewords, size);

        int bestMask = 0;
        int bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, isFunction, size, mask);
            DrawFormatBits(modules, size, mask);
            int penalty = ComputePenalty(modules, size);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask = mask;
            }
            ApplyMask(modules, isFunction, size, mask); // undo
        }

        ApplyMask(modules, isFunction, size, bestMask);
        DrawFormatBits(modules, size, bestMask);
        return modules;
    }

    private static byte[] BuildDataCodewords(byte[] payload, int version, int totalDataCodewords)
    {
        var bits = new List<bool>(totalDataCodewords * 8);
        AppendBits(bits, 0b0100, 4); // Byte mode
        AppendBits(bits, payload.Length, version <= 9 ? 8 : 16);
        foreach (byte b in payload)
        {
            AppendBits(bits, b, 8);
        }

        int targetBits = totalDataCodewords * 8;
        int terminator = Math.Min(4, targetBits - bits.Count);
        for (int i = 0; i < terminator; i++)
        {
            bits.Add(false);
        }

        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }

        byte[] result = new byte[totalDataCodewords];
        int byteCount = bits.Count / 8;
        for (int i = 0; i < byteCount; i++)
        {
            int val = 0;
            for (int b = 0; b < 8; b++)
            {
                val = (val << 1) | (bits[i * 8 + b] ? 1 : 0);
            }
            result[i] = (byte)val;
        }

        for (int i = byteCount; i < totalDataCodewords; i++)
        {
            result[i] = ((i - byteCount) % 2 == 0) ? (byte)0xEC : (byte)0x11;
        }

        return result;
    }

    private static void AppendBits(List<bool> bits, int value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) != 0);
        }
    }

    private static byte[] AddErrorCorrectionAndInterleave(
        byte[] data,
        (int TotalData, int EcPerBlock, int G1Blocks, int G1Data, int G2Blocks, int G2Data) spec)
    {
        var gen = ComputeReedSolomonDivisor(spec.EcPerBlock);
        int numBlocks = spec.G1Blocks + spec.G2Blocks;
        var dataBlocks = new byte[numBlocks][];
        var ecBlocks = new byte[numBlocks][];

        int offset = 0;
        for (int b = 0; b < numBlocks; b++)
        {
            int blockLen = b < spec.G1Blocks ? spec.G1Data : spec.G2Data;
            var block = new byte[blockLen];
            Array.Copy(data, offset, block, 0, blockLen);
            offset += blockLen;
            dataBlocks[b] = block;
            ecBlocks[b] = ComputeReedSolomonRemainder(block, gen);
        }

        var output = new List<byte>(spec.TotalData + numBlocks * spec.EcPerBlock);
        int maxDataLen = Math.Max(spec.G1Data, spec.G2Data);
        for (int i = 0; i < maxDataLen; i++)
        {
            for (int b = 0; b < numBlocks; b++)
            {
                if (i < dataBlocks[b].Length)
                {
                    output.Add(dataBlocks[b][i]);
                }
            }
        }

        for (int i = 0; i < spec.EcPerBlock; i++)
        {
            for (int b = 0; b < numBlocks; b++)
            {
                output.Add(ecBlocks[b][i]);
            }
        }

        return [.. output];
    }

    private static byte[] ComputeReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = MultiplyGf256(result[j], root);
                if (j + 1 < degree)
                {
                    result[j] ^= result[j + 1];
                }
            }
            root = MultiplyGf256(root, 0x02);
        }
        return result;
    }

    private static byte[] ComputeReedSolomonRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (byte b in data)
        {
            byte factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < divisor.Length; i++)
            {
                result[i] ^= MultiplyGf256(divisor[i], factor);
            }
        }
        return result;
    }

    private static byte MultiplyGf256(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, int version, int size)
    {
        // Timing patterns
        for (int i = 0; i < size; i++)
        {
            SetFunctionModule(modules, isFunction, 6, i, i % 2 == 0);
            SetFunctionModule(modules, isFunction, i, 6, i % 2 == 0);
        }

        // Finder patterns
        DrawFinderPattern(modules, isFunction, 3, 3, size);
        DrawFinderPattern(modules, isFunction, size - 4, 3, size);
        DrawFinderPattern(modules, isFunction, 3, size - 4, size);

        // Alignment patterns
        var centers = AlignmentCenters[version];
        for (int i = 0; i < centers.Length; i++)
        {
            for (int j = 0; j < centers.Length; j++)
            {
                if ((i == 0 && j == 0) ||
                    (i == 0 && j == centers.Length - 1) ||
                    (i == centers.Length - 1 && j == 0))
                {
                    continue;
                }
                DrawAlignmentPattern(modules, isFunction, centers[i], centers[j]);
            }
        }

        // Reserve format info + dark module
        for (int i = 0; i <= 8; i++)
        {
            if (i != 6)
            {
                SetFunctionModule(modules, isFunction, 8, i, false);
                SetFunctionModule(modules, isFunction, i, 8, false);
            }
        }
        for (int i = size - 8; i < size; i++)
        {
            SetFunctionModule(modules, isFunction, 8, i, false);
            SetFunctionModule(modules, isFunction, i, 8, false);
        }
        SetFunctionModule(modules, isFunction, 8, size - 8, true);

        // Version >= 7 reserves 6x3 version blocks (v7..v10)
        if (version >= 7)
        {
            int rem = version;
            for (int i = 0; i < 12; i++)
            {
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            }
            int bits = (version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3;
                int b = i / 3;
                SetFunctionModule(modules, isFunction, a, b, bit);
                SetFunctionModule(modules, isFunction, b, a, bit);
            }
        }
    }

    private static void DrawFinderPattern(bool[,] modules, bool[,] isFunction, int x, int y, int size)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int xx = x + dx;
                int yy = y + dy;
                if (xx >= 0 && xx < size && yy >= 0 && yy < size)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunctionModule(modules, isFunction, xx, yy, dist != 2 && dist != 4);
                }
            }
        }
    }

    private static void DrawAlignmentPattern(bool[,] modules, bool[,] isFunction, int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                SetFunctionModule(modules, isFunction, x + dx, y + dy, dist != 1);
            }
        }
    }

    private static void SetFunctionModule(bool[,] modules, bool[,] isFunction, int x, int y, bool black)
    {
        modules[y, x] = black;
        isFunction[y, x] = true;
    }

    private static void DrawCodewords(bool[,] modules, bool[,] isFunction, byte[] data, int size)
    {
        int i = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;
                    if (!isFunction[y, x] && i < data.Length * 8)
                    {
                        modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
            }
        }
    }

    private static void ApplyMask(bool[,] modules, bool[,] isFunction, int size, int mask)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (isFunction[y, x]) continue;
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    7 => ((x + y) % 2 + x * y % 3) % 2 == 0,
                    _ => false
                };
                if (invert)
                {
                    modules[y, x] = !modules[y, x];
                }
            }
        }
    }

    private static void DrawFormatBits(bool[,] modules, int size, int mask)
    {
        // ECC Level L format bits = 01
        int data = (0b01 << 3) | mask;
        int rem = data;
        for (int i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        }
        int bits = ((data << 10) | rem) ^ 0x5412;

        for (int i = 0; i <= 5; i++) modules[i, 8] = ((bits >> i) & 1) != 0;
        modules[7, 8] = ((bits >> 6) & 1) != 0;
        modules[8, 8] = ((bits >> 7) & 1) != 0;
        modules[8, 7] = ((bits >> 8) & 1) != 0;
        for (int i = 9; i < 15; i++) modules[8, 14 - i] = ((bits >> i) & 1) != 0;

        for (int i = 0; i < 8; i++) modules[8, size - 1 - i] = ((bits >> i) & 1) != 0;
        for (int i = 8; i < 15; i++) modules[size - 15 + i, 8] = ((bits >> i) & 1) != 0;
        modules[size - 8, 8] = true;
    }

    private static int ComputePenalty(bool[,] modules, int size)
    {
        int penalty = 0;
        for (int y = 0; y < size; y++)
        {
            int runLen = 1;
            for (int x = 1; x < size; x++)
            {
                if (modules[y, x] == modules[y, x - 1])
                {
                    runLen++;
                    if (runLen == 5) penalty += 3;
                    else if (runLen > 5) penalty += 1;
                }
                else
                {
                    runLen = 1;
                }
            }
        }
        for (int x = 0; x < size; x++)
        {
            int runLen = 1;
            for (int y = 1; y < size; y++)
            {
                if (modules[y, x] == modules[y - 1, x])
                {
                    runLen++;
                    if (runLen == 5) penalty += 3;
                    else if (runLen > 5) penalty += 1;
                }
                else
                {
                    runLen = 1;
                }
            }
        }
        for (int y = 0; y < size - 1; y++)
        {
            for (int x = 0; x < size - 1; x++)
            {
                bool c = modules[y, x];
                if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1])
                {
                    penalty += 3;
                }
            }
        }
        return penalty;
    }
}
