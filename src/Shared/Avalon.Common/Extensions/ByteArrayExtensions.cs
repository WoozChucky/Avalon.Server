namespace Avalon.Common.Extensions;

public static class ByteArrayExtensions
{
    public static MemoryStream ToMemoryStream(this byte[] bytes)
    {
        return new MemoryStream(bytes);
    }
}
