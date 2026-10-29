using System.Text;

namespace Jentl.Assets.Tests;

public class JpegTests
{
    private static byte[] File(string name) => System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Jpegs", name));

    /// <summary>A binary PPM (P6) or PGM (P5) as RGB bytes.</summary>
    private static (int Width, int Height, byte[] Rgb) Reference(string name)
    {
        var file = File(name);
        var fields = new List<string>();
        var at = 0;
        while (fields.Count < 4)
        {
            while (char.IsWhiteSpace((char)file[at]))
            {
                at++;
            }

            var start = at;
            while (!char.IsWhiteSpace((char)file[at]))
            {
                at++;
            }

            fields.Add(Encoding.ASCII.GetString(file, start, at - start));
        }

        at++;
        var (width, height) = (int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture));
        var grey = fields[0] == "P5";
        var rgb = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                rgb[(i * 3) + c] = file[at + (grey ? i : (i * 3) + c)];
            }
        }

        return (width, height, rgb);
    }

    [Theory]
    [InlineData("colour420")]
    [InlineData("colour422")]
    [InlineData("colour444")]
    [InlineData("grey")]
    [InlineData("restarts")]
    [InlineData("rgb")]
    [InlineData("separate")]
    [InlineData("progressive")]
    [InlineData("progressivegrey")]
    public void DecodesAsLibjpegDoes(string name)
    {
        var image = Jpeg.Decode(File(name + ".jpg"));
        var (width, height, expected) = Reference(name + ".pnm");

        (image.Width, image.Height).Should().Be((width, height));
        var worst = 0;
        for (var i = 0; i < width * height; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                worst = Math.Max(worst, Math.Abs(image.Pixels[(i * 4) + c] - expected[(i * 3) + c]));
            }

            image.Pixels[(i * 4) + 3].Should().Be(255);
        }

        worst.Should().BeLessThanOrEqualTo(2, "rounding in the inverse DCT and colour conversion may differ by a step or two");
    }

    [Fact]
    public void ArithmeticCoding_IsRefused()
    {
        var act = () => Jpeg.Decode(File("arithmetic.jpg"));

        act.Should().Throw<NotSupportedException>().WithMessage("*arithmetic-coded*");
    }

    [Fact]
    public void OtherFiles_AreNotJpegs()
    {
        Jpeg.IsJpeg(File("grey.jpg")).Should().BeTrue();
        Jpeg.IsJpeg(Png.Encode(new Image(1, 1))).Should().BeFalse();
        var act = () => Jpeg.Decode([1, 2, 3]);
        act.Should().Throw<InvalidDataException>();
        var cut = () => Jpeg.Decode(File("colour444.jpg")[..40]);
        cut.Should().Throw<InvalidDataException>();
    }
}
