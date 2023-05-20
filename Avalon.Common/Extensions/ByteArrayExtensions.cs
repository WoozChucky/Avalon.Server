namespace Avalon.Common.Extensions;

public static class ByteArrayExtensions
{
    public static MemoryStream ToMemoryStream(this byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }
}
