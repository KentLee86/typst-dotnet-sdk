using Typst.Renderer.Uno;

var converted = TypstUnoPixelConverter.ToBgra8Premultiplied(
    [10, 20, 30, 40],
    pixelWidth: 1,
    pixelHeight: 1,
    sourceStride: 4);

if (!converted.SequenceEqual(new byte[] { 30, 20, 10, 40 }))
    throw new InvalidOperationException("The packaged Uno pixel converter returned unexpected data.");

Console.WriteLine($"Loaded {typeof(TypstView).Assembly.GetName().Name}; RGBA to BGRA conversion passed.");
