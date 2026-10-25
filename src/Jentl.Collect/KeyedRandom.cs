using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Jentl.Collect;

/// <summary>
/// A <see cref="Random"/> whose numbers come from a secret key and a label, the same on every platform and in every
/// run, and from which neither the key nor any other label's numbers can be worked out. Use it where a player must not
/// be able to guess what comes next from what they have seen (the cards still in the deck, an opponent's hand): a
/// seeded <see cref="Random"/> has only 2^31 seeds and can be found from a few of its numbers.
/// </summary>
/// <remarks>
/// The numbers are HMAC-SHA256 under the key of the label's UTF-8 bytes, a zero byte and a block counter (eight bytes,
/// big-endian, from 0), block after block. Each block gives four 64-bit numbers (little-endian); smaller numbers take
/// the low bits of one, and ranges are drawn without bias by rejecting the numbers that would favour some values.
/// </remarks>
public sealed class KeyedRandom : Random
{
    private readonly byte[] _key;
    private readonly byte[] _message;
    private readonly byte[] _block = new byte[HMACSHA256.HashSizeInBytes];
    private ulong _counter;
    private int _used = HMACSHA256.HashSizeInBytes;

    /// <param name="key">The secret: any length but empty; 16 bytes or more for a key nobody can try every value of.</param>
    /// <param name="label">What the numbers are for ("deal 12", "seat 3"): every label gives its own numbers.</param>
    public KeyedRandom(ReadOnlySpan<byte> key, string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (key.IsEmpty)
        {
            throw new ArgumentException("The key must not be empty.", nameof(key));
        }

        _key = key.ToArray();
        var text = Encoding.UTF8.GetByteCount(label);
        _message = new byte[text + 1 + sizeof(ulong)];
        Encoding.UTF8.GetBytes(label, _message);
    }

    /// <summary>The next 64 random bits.</summary>
    public ulong NextUInt64()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        Fill(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    /// <summary>The next 32 random bits: the low half of <see cref="NextUInt64"/>.</summary>
    public uint NextUInt32() => (uint)NextUInt64();

    public override int Next()
    {
        while (true)
        {
            // 0 to int.MaxValue - 1, as Random promises.
            var value = (int)(NextUInt64() >> 33);
            if (value != int.MaxValue)
            {
                return value;
            }
        }
    }

    public override int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (int)Below((ulong)maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return (int)(minValue + (long)Below((ulong)((long)maxValue - minValue)));
    }

    public override long NextInt64()
    {
        while (true)
        {
            var value = (long)(NextUInt64() >> 1);
            if (value != long.MaxValue)
            {
                return value;
            }
        }
    }

    public override long NextInt64(long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (long)Below((ulong)maxValue);
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return unchecked(minValue + (long)Below((ulong)(maxValue - minValue)));
    }

    public override double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public override float NextSingle() => (NextUInt64() >> 40) * (1.0f / (1 << 24));

    public override void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Fill(buffer);
    }

    public override void NextBytes(Span<byte> buffer) => Fill(buffer);

    protected override double Sample() => NextDouble();

    // A number from 0 to bound - 1 (0 for a bound of 0), without bias: draws in the top part that would make the low
    // values more likely are thrown away and drawn again.
    private ulong Below(ulong bound)
    {
        if (bound <= 1)
        {
            return 0;
        }

        var limit = ulong.MaxValue - (ulong.MaxValue % bound + 1) % bound;
        while (true)
        {
            var value = NextUInt64();
            if (value <= limit)
            {
                return value % bound;
            }
        }
    }

    private void Fill(Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            if (_used == _block.Length)
            {
                BinaryPrimitives.WriteUInt64BigEndian(_message.AsSpan(_message.Length - sizeof(ulong)), _counter++);
                HMACSHA256.HashData(_key, _message, _block);
                _used = 0;
            }

            var take = Math.Min(buffer.Length, _block.Length - _used);
            _block.AsSpan(_used, take).CopyTo(buffer);
            _used += take;
            buffer = buffer[take..];
        }
    }
}
