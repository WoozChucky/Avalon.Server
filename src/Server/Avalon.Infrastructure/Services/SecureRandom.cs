using System.Security.Cryptography;

namespace Avalon.Infrastructure.Services;

public interface ISecureRandom
{
    byte[] GetBytes(int count);
}

public class SecureRandom : ISecureRandom
{
    public byte[] GetBytes(int count) => RandomNumberGenerator.GetBytes(count);
}
