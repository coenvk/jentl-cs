using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Jentl.Collect.Tests;

public class KeyedRandomTests
{
    private static readonly byte[] Key = [.. Enumerable.Range(1, 16).Select(i => (byte)i)];

    [Fact]
    public void TheSameKeyAndLabel_GiveTheSameStream()
    {
        var first = new KeyedRandom(Key, "deal 1");
        var second = new KeyedRandom(Key, "deal 1");

        Enumerable.Range(0, 200).Select(_ => first.Next(52)).Should().Equal(Enumerable.Range(0, 200).Select(_ => second.Next(52)));
    }

    [Fact]
    public void OtherLabelsAndKeys_GiveOtherStreams()
    {
        static ulong[] Stream(byte[] key, string label)
        {
            var random = new KeyedRandom(key, label);
            return [.. Enumerable.Range(0, 8).Select(_ => random.NextUInt64())];
        }

        var deal = Stream(Key, "deal");
        Stream(Key, "seat").Should().NotIntersectWith(deal);
        Stream(Key, "deal 2").Should().NotIntersectWith(deal);
        Stream([.. Key.AsEnumerable().Reverse()], "deal").Should().NotIntersectWith(deal);
    }

    [Fact]
    public void TheStream_IsHmacSha256_OfTheLabelAndACounter()
    {
        var random = new KeyedRandom(Key, "deal");
        byte[] block = [.. Encoding.UTF8.GetBytes("deal"), 0, 0, 0, 0, 0, 0, 0, 0, 1];
        byte[] first = [.. block[..^1], 0];
        var expected = HMACSHA256.HashData(Key, first);
        var next = HMACSHA256.HashData(Key, block);

        random.NextUInt64().Should().Be(BinaryPrimitives.ReadUInt64LittleEndian(expected));
        random.NextUInt64().Should().Be(BinaryPrimitives.ReadUInt64LittleEndian(expected.AsSpan(8)));
        random.NextUInt64();
        random.NextUInt64();
        random.NextUInt64().Should().Be(BinaryPrimitives.ReadUInt64LittleEndian(next), "the fifth number starts the second block");
    }

    [Fact]
    public void Numbers_StayInTheirRanges_AndCoverThem()
    {
        var random = new KeyedRandom(Key, "ranges");
        var draws = Enumerable.Range(0, 5000).Select(_ => random.Next(6)).ToList();
        draws.Should().OnlyContain(d => d >= 0 && d < 6);
        draws.Distinct().Should().HaveCount(6);
        draws.GroupBy(d => d).Should().OnlyContain(g => g.Count() > 700 && g.Count() < 970);

        Enumerable.Range(0, 1000).Select(_ => random.Next(-3, 4)).Should().OnlyContain(d => d >= -3 && d < 4);
        Enumerable.Range(0, 1000).Select(_ => random.Next()).Should().OnlyContain(d => d >= 0 && d < int.MaxValue);
        Enumerable.Range(0, 1000).Select(_ => random.NextInt64(long.MinValue, long.MaxValue)).Should().OnlyContain(d => d < long.MaxValue);
        Enumerable.Range(0, 1000).Select(_ => random.NextDouble()).Should().OnlyContain(d => d >= 0 && d < 1);
        Enumerable.Range(0, 1000).Select(_ => random.NextSingle()).Should().OnlyContain(d => d >= 0 && d < 1);
        random.Next(1).Should().Be(0);
        random.Next(5, 5).Should().Be(5);
    }

    [Fact]
    public void Bytes_ComeFromTheSameStream()
    {
        var bytes = new byte[12];
        new KeyedRandom(Key, "bytes").NextBytes(bytes);
        var numbers = new KeyedRandom(Key, "bytes");

        bytes[..8].Should().Equal(BitConverter.GetBytes(numbers.NextUInt64()));
        new KeyedRandom(Key, "bytes").Invoking(r => r.Next(-1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AnEmptyKey_IsNotAllowed() =>
        FluentActions.Invoking(() => new KeyedRandom([], "deal")).Should().Throw<ArgumentException>();
}
