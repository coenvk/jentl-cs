namespace Jentl.Assets;

/// <summary>
/// Reads JPEG pictures, baseline and progressive (Huffman-coded, 8-bit), in grey or colour (YCbCr, as JFIF, or RGB),
/// with any chroma subsampling and restart markers: what cameras, photo tools and 3D model files write. Lossless and
/// arithmetic-coded files, 12-bit samples and CMYK are refused with a <see cref="NotSupportedException"/>.
/// </summary>
public static class Jpeg
{
    private static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // cos((2x + 1) u π / 16), scaled by C(u) / 2, for the inverse DCT.
    private static readonly float[] Cosines = MakeCosines();

    /// <summary>Whether <paramref name="file"/> starts like a JPEG.</summary>
    public static bool IsJpeg(ReadOnlySpan<byte> file) => file.Length >= 3 && file[0] == 0xFF && file[1] == 0xD8 && file[2] == 0xFF;

    public static Image Decode(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!IsJpeg(file))
        {
            throw new InvalidDataException("Not a JPEG file.");
        }

        return new Decoder(file).Decode();
    }

    private static float[] MakeCosines()
    {
        var table = new float[64];
        for (var x = 0; x < 8; x++)
        {
            for (var u = 0; u < 8; u++)
            {
                var c = u == 0 ? 1 / MathF.Sqrt(2) : 1;
                table[(x * 8) + u] = c / 2 * MathF.Cos(((2 * x) + 1) * u * MathF.PI / 16);
            }
        }

        return table;
    }

    private sealed class Huffman
    {
        // Canonical codes: for each length, the first code and where its symbols start.
        private readonly int[] _maxCode = new int[18];
        private readonly int[] _offset = new int[17];
        private readonly byte[] _symbols;

        public Huffman(ReadOnlySpan<byte> counts, byte[] symbols)
        {
            _symbols = symbols;
            var code = 0;
            var k = 0;
            for (var length = 1; length <= 16; length++)
            {
                _offset[length] = k - code;
                code += counts[length - 1];
                k += counts[length - 1];
                _maxCode[length] = counts[length - 1] > 0 ? code - 1 : -1;
                code <<= 1;
            }

            _maxCode[17] = int.MaxValue;
        }

        public int Decode(BitReader bits)
        {
            var code = 0;
            for (var length = 1; length <= 16; length++)
            {
                code = (code << 1) | bits.Bit();
                if (code <= _maxCode[length])
                {
                    return _symbols[code + _offset[length]];
                }
            }

            throw new InvalidDataException("A JPEG Huffman code is broken.");
        }
    }

    private sealed class BitReader(byte[] data, int position)
    {
        private int _buffer;
        private int _count;

        public int Position { get; private set; } = position;

        public int Bit()
        {
            if (_count == 0)
            {
                var b = Position < data.Length ? data[Position] : 0;
                if (b == 0xFF)
                {
                    var next = Position + 1 < data.Length ? data[Position + 1] : 0;
                    if (next == 0)
                    {
                        Position += 2;
                    }
                    else
                    {
                        // A marker: the data has run out; feed zeros without passing it.
                        b = 0;
                    }
                }
                else
                {
                    Position++;
                }

                _buffer = b;
                _count = 8;
            }

            _count--;
            return (_buffer >> _count) & 1;
        }

        public int Bits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | Bit();
            }

            return value;
        }

        /// <summary>Skips to the next byte and past a restart marker.</summary>
        public void Restart()
        {
            _count = 0;
            while (Position + 1 < data.Length && !(data[Position] == 0xFF && data[Position + 1] is >= 0xD0 and <= 0xD7))
            {
                Position++;
            }

            Position += 2;
        }
    }


    private sealed class Component(int id, int horizontal, int vertical, int table)
    {
        public int Id { get; } = id;

        public int Horizontal { get; } = horizontal;

        public int Vertical { get; } = vertical;

        public int Table { get; } = table;

        public Huffman? Dc { get; set; }

        public Huffman? Ac { get; set; }

        public int Predictor { get; set; }

        /// <summary>Its blocks' coefficients, 64 each, row by row over whole MCUs.</summary>
        public int[] Coefficients { get; set; } = [];

        /// <summary>Blocks per row, over whole MCUs.</summary>
        public int BlocksAcross { get; set; }

        /// <summary>Blocks it really has across and down (what a scan of it alone goes through).</summary>
        public (int Across, int Down) Used { get; set; }

        public byte[] Plane { get; set; } = [];

        public int Stride => BlocksAcross * 8;
    }

    private sealed class Decoder(byte[] data)
    {
        private readonly int[][] _quantization = new int[4][];
        private readonly Huffman?[] _dcTables = new Huffman?[4];
        private readonly Huffman?[] _acTables = new Huffman?[4];
        private readonly List<Component> _components = [];
        private int _width;
        private int _height;
        private int _mcusAcross;
        private int _mcusDown;
        private int _restartInterval;
        private bool _adobeRgb;
        private bool _scanned;

        // The scan being read: which coefficients, which bit of them, and blocks left that end early.
        private int _start;
        private int _end;
        private int _high;
        private int _low;
        private int _endOfBands;

        public Image Decode()
        {
            var at = 2;
            while (at + 4 <= data.Length)
            {
                if (data[at] != 0xFF)
                {
                    at++;
                    continue;
                }

                var marker = data[at + 1];
                if (marker is 0xFF or 0x00 or 0x01 or (>= 0xD0 and <= 0xD7))
                {
                    at++;
                    continue;
                }

                if (marker == 0xD9)
                {
                    break;
                }

                var length = (data[at + 2] << 8) | data[at + 3];
                var segment = at + 4;
                if (length < 2 || segment + length - 2 > data.Length)
                {
                    throw new InvalidDataException("The JPEG file is cut short.");
                }

                var body = data.AsSpan(segment, length - 2);
                switch (marker)
                {
                    case 0xC0 or 0xC1 or 0xC2:
                        ReadFrame(body);
                        break;
                    case 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF:
                        throw new NotSupportedException("Lossless, hierarchical and arithmetic-coded JPEGs aren't supported.");
                    case 0xC4:
                        ReadHuffman(body);
                        break;
                    case 0xDB:
                        ReadQuantization(body);
                        break;
                    case 0xDD:
                        _restartInterval = (body[0] << 8) | body[1];
                        break;
                    case 0xEE when body.Length >= 12 && body[..5].SequenceEqual("Adobe"u8):
                        _adobeRgb = body[11] == 0;
                        break;
                    case 0xDA:
                        at = ReadScan(body, segment + length - 2);
                        _scanned = true;
                        continue;
                }

                at = segment + length - 2;
            }

            if (!_scanned)
            {
                throw new InvalidDataException("The JPEG file has no picture data.");
            }

            // Components named R, G and B are stored as they are (libjpeg writes RGB that way).
            _adobeRgb |= _components.Select(c => c.Id).SequenceEqual([82, 71, 66]);
            foreach (var component in _components)
            {
                Reconstruct(component);
            }

            return Colour();
        }

        private void ReadFrame(ReadOnlySpan<byte> body)
        {
            if (body[0] != 8)
            {
                throw new NotSupportedException($"{body[0]}-bit JPEGs aren't supported, only 8-bit.");
            }

            _height = (body[1] << 8) | body[2];
            _width = (body[3] << 8) | body[4];
            var count = body[5];
            if (_width == 0 || _height == 0)
            {
                throw new InvalidDataException("The JPEG has no size.");
            }

            if (count is not (1 or 3))
            {
                throw new NotSupportedException($"JPEGs with {count} colour components (CMYK) aren't supported.");
            }

            for (var i = 0; i < count; i++)
            {
                var c = body.Slice(6 + (i * 3), 3);
                if (c[1] >> 4 is < 1 or > 4 || (c[1] & 15) is < 1 or > 4)
                {
                    throw new InvalidDataException("A JPEG component has a sampling factor outside 1 to 4.");
                }

                _components.Add(new Component(c[0], c[1] >> 4, c[1] & 15, c[2]));
            }

            var maxH = _components.Max(c => c.Horizontal);
            var maxV = _components.Max(c => c.Vertical);
            _mcusAcross = (_width + (8 * maxH) - 1) / (8 * maxH);
            _mcusDown = (_height + (8 * maxV) - 1) / (8 * maxV);
            foreach (var component in _components)
            {
                component.BlocksAcross = _mcusAcross * component.Horizontal;
                component.Coefficients = new int[component.BlocksAcross * _mcusDown * component.Vertical * 64];
                component.Used = (
                    ((((_width * component.Horizontal) + maxH - 1) / maxH) + 7) / 8,
                    ((((_height * component.Vertical) + maxV - 1) / maxV) + 7) / 8);
            }
        }

        private void ReadHuffman(ReadOnlySpan<byte> body)
        {
            while (body.Length > 17)
            {
                var (kind, index) = (body[0] >> 4, body[0] & 15);
                var counts = body.Slice(1, 16);
                var total = 0;
                foreach (var count in counts)
                {
                    total += count;
                }

                var table = new Huffman(counts, body.Slice(17, total).ToArray());
                (kind == 0 ? _dcTables : _acTables)[index & 3] = table;
                body = body[(17 + total)..];
            }
        }

        private void ReadQuantization(ReadOnlySpan<byte> body)
        {
            while (body.Length > 0)
            {
                var (wide, index) = (body[0] >> 4 != 0, body[0] & 3);
                var table = new int[64];
                for (var i = 0; i < 64; i++)
                {
                    table[ZigZag[i]] = wide ? (body[1 + (i * 2)] << 8) | body[2 + (i * 2)] : body[1 + i];
                }

                _quantization[index] = table;
                body = body[(1 + (wide ? 128 : 64))..];
            }
        }

        private int ReadScan(ReadOnlySpan<byte> body, int start)
        {
            if (_components.Count == 0)
            {
                throw new InvalidDataException("The JPEG scan comes before its frame.");
            }

            var count = body[0];
            var scan = new List<Component>();
            for (var i = 0; i < count; i++)
            {
                var id = body[1 + (i * 2)];
                var component = _components.FirstOrDefault(c => c.Id == id)
                    ?? throw new InvalidDataException("A JPEG scan names a component the frame doesn't have.");
                component.Dc = _dcTables[body[2 + (i * 2)] >> 4 & 3];
                component.Ac = _acTables[body[2 + (i * 2)] & 3];
                component.Predictor = 0;
                scan.Add(component);
            }

            var tail = body[(1 + (count * 2))..];
            (_start, _end, _high, _low, _endOfBands) = (tail[0], Math.Min((int)tail[1], 63), tail[2] >> 4, tail[2] & 15, 0);
            var bits = new BitReader(data, start);
            var single = scan.Count == 1;
            var only = scan[0];

            // A scan of one component goes block by block over just its blocks; one of several, MCU by MCU.
            var (across, down) = single ? only.Used : (_mcusAcross, _mcusDown);
            var done = 0;
            for (var row = 0; row < down; row++)
            {
                for (var column = 0; column < across; column++)
                {
                    if (_restartInterval > 0 && done > 0 && done % _restartInterval == 0)
                    {
                        bits.Restart();
                        _endOfBands = 0;
                        foreach (var component in scan)
                        {
                            component.Predictor = 0;
                        }
                    }

                    if (single)
                    {
                        DecodeBlock(bits, only, (row * only.BlocksAcross) + column);
                    }
                    else
                    {
                        foreach (var component in scan)
                        {
                            for (var v = 0; v < component.Vertical; v++)
                            {
                                for (var h = 0; h < component.Horizontal; h++)
                                {
                                    var blockRow = (row * component.Vertical) + v;
                                    DecodeBlock(bits, component, (blockRow * component.BlocksAcross) + (column * component.Horizontal) + h);
                                }
                            }
                        }
                    }

                    done++;
                }
            }

            // On to the marker after the entropy-coded data.
            var at = bits.Position;
            while (at + 1 < data.Length && !(data[at] == 0xFF && data[at + 1] != 0 && data[at + 1] is not (>= 0xD0 and <= 0xD7)))
            {
                at++;
            }

            return at;
        }

        private static Huffman Table(Huffman? table) => table ?? throw new InvalidDataException("A JPEG scan uses a Huffman table that isn't there.");

        /// <summary>Reads one block's part of the scan: its DC and AC coefficients (baseline), or a band or a bit of them (progressive).</summary>
        private void DecodeBlock(BitReader bits, Component component, int block)
        {
            var coefficients = component.Coefficients.AsSpan(block * 64, 64);
            if (_start == 0)
            {
                if (_high == 0)
                {
                    var size = Table(component.Dc).Decode(bits);
                    component.Predictor += size == 0 ? 0 : Extend(bits.Bits(size), size);
                    coefficients[0] = component.Predictor * (1 << _low);
                }
                else if (bits.Bit() == 1)
                {
                    coefficients[0] |= 1 << _low;
                }

                if (_end == 0)
                {
                    return;
                }
            }

            var first = Math.Max(_start, 1);
            if (_high == 0)
            {
                FirstBand(bits, Table(component.Ac), coefficients, first);
            }
            else
            {
                RefineBand(bits, Table(component.Ac), coefficients, first);
            }
        }

        private void FirstBand(BitReader bits, Huffman table, Span<int> coefficients, int first)
        {
            if (_endOfBands > 0)
            {
                _endOfBands--;
                return;
            }

            for (var k = first; k <= _end;)
            {
                var symbol = table.Decode(bits);
                var (run, size) = (symbol >> 4, symbol & 15);
                if (size == 0)
                {
                    if (run < 15)
                    {
                        // The end of this block, and of the next ones the run says.
                        _endOfBands = (1 << run) - 1 + (run > 0 ? bits.Bits(run) : 0);
                        return;
                    }

                    k += 16;
                    continue;
                }

                k += run;
                if (k > 63)
                {
                    return;
                }

                coefficients[ZigZag[k]] = Extend(bits.Bits(size), size) * (1 << _low);
                k++;
            }
        }

        /// <summary>A later pass of a band: one more bit of every coefficient already there, and new ones of that size (as stb_image does it).</summary>
        private void RefineBand(BitReader bits, Huffman table, Span<int> coefficients, int first)
        {
            var bit = 1 << _low;
            void Sharpen(Span<int> c, int at)
            {
                if (c[at] != 0 && bits.Bit() == 1 && (c[at] & bit) == 0)
                {
                    c[at] += c[at] > 0 ? bit : -bit;
                }
            }

            if (_endOfBands > 0)
            {
                _endOfBands--;
                for (var k = first; k <= _end; k++)
                {
                    Sharpen(coefficients, ZigZag[k]);
                }

                return;
            }

            for (var k = first; k <= _end;)
            {
                var symbol = table.Decode(bits);
                var (run, size) = (symbol >> 4, symbol & 15);
                var value = 0;
                if (size == 0)
                {
                    if (run < 15)
                    {
                        _endOfBands = (1 << run) - 1 + (run > 0 ? bits.Bits(run) : 0);
                        run = 64;
                    }
                }
                else
                {
                    value = bits.Bit() == 1 ? bit : -bit;
                }

                while (k <= _end)
                {
                    var at = ZigZag[k++];
                    if (coefficients[at] != 0)
                    {
                        Sharpen(coefficients, at);
                    }
                    else
                    {
                        if (run == 0)
                        {
                            coefficients[at] = value;
                            break;
                        }

                        run--;
                    }
                }
            }
        }

        private static int Extend(int value, int size) => value < 1 << (size - 1) ? value - (1 << size) + 1 : value;

        /// <summary>Turns the component's coefficients into samples: dequantised, then the inverse DCT.</summary>
        private void Reconstruct(Component component)
        {
            var quantization = _quantization[component.Table & 3] ?? throw new InvalidDataException("A JPEG component uses a quantization table that isn't there.");
            var down = component.Coefficients.Length / 64 / component.BlocksAcross;
            component.Plane = new byte[component.Stride * down * 8];
            var block = new float[64];
            var samples = new float[64];
            for (var b = 0; b < component.Coefficients.Length / 64; b++)
            {
                for (var i = 0; i < 64; i++)
                {
                    block[i] = component.Coefficients[(b * 64) + i] * quantization[i];
                }

                InverseDct(block, samples);
                var (blockX, blockY) = (b % component.BlocksAcross, b / component.BlocksAcross);
                for (var y = 0; y < 8; y++)
                {
                    var line = (((blockY * 8) + y) * component.Stride) + (blockX * 8);
                    for (var x = 0; x < 8; x++)
                    {
                        component.Plane[line + x] = (byte)Math.Clamp((int)MathF.Round(samples[(y * 8) + x] + 128), 0, 255);
                    }
                }
            }
        }

        private static void InverseDct(float[] block, float[] samples)
        {
            // Separable: rows then columns, each output a sum over the eight frequencies.
            Span<float> rows = stackalloc float[64];
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var sum = 0f;
                    for (var u = 0; u < 8; u++)
                    {
                        sum += Cosines[(x * 8) + u] * block[(y * 8) + u];
                    }

                    rows[(y * 8) + x] = sum;
                }
            }

            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    var sum = 0f;
                    for (var v = 0; v < 8; v++)
                    {
                        sum += Cosines[(y * 8) + v] * rows[(v * 8) + x];
                    }

                    samples[(y * 8) + x] = sum;
                }
            }
        }

        private Image Colour()
        {
            var image = new Image(_width, _height);
            var pixels = image.Pixels;
            var maxH = _components.Max(c => c.Horizontal);
            var maxV = _components.Max(c => c.Vertical);
            byte Sample(Component c, int x, int y) => c.Plane[(y * c.Vertical / maxV * c.Stride) + (x * c.Horizontal / maxH)];
            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width; x++)
                {
                    var i = ((y * _width) + x) * 4;
                    if (_components.Count == 1)
                    {
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = Sample(_components[0], x, y);
                    }
                    else if (_adobeRgb)
                    {
                        (pixels[i], pixels[i + 1], pixels[i + 2]) = (Sample(_components[0], x, y), Sample(_components[1], x, y), Sample(_components[2], x, y));
                    }
                    else
                    {
                        float luma = Sample(_components[0], x, y), cb = Sample(_components[1], x, y) - 128f, cr = Sample(_components[2], x, y) - 128f;
                        pixels[i] = Byte(luma + (1.402f * cr));
                        pixels[i + 1] = Byte(luma - (0.344136f * cb) - (0.714136f * cr));
                        pixels[i + 2] = Byte(luma + (1.772f * cb));
                    }

                    pixels[i + 3] = 255;
                }
            }

            return image;

            static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
        }
    }
}
