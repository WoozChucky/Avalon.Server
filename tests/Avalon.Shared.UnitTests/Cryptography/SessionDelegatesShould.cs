using System.Text.RegularExpressions;
using Avalon.Shared.UnitTests.Schema;
using Xunit;

namespace Avalon.Shared.UnitTests.Cryptography;

/// <summary>
/// Every send hands its packet's <c>Create</c> the session's <c>Encryptor</c>, a delegate the session creates once,
/// and the connection's read loop passes a decrypt delegate it creates once. Writing <c>CryptoSession.Encrypt</c> or
/// <c>CryptoSession.Decrypt</c> as a method group instead compiles, passes every other test and sends the same bytes,
/// but creates a delegate per packet, and whether that one lands on the heap depends on how far the JIT has
/// optimised the caller (#854). This scan is the only thing that notices.
/// </summary>
public class SessionDelegatesShould
{
    // A member access not followed by an argument list: a method group. A direct call, Encrypt(...), is fine.
    private static readonly Regex s_methodGroup = new(
        @"\bCryptoSession\??\.(Encrypt|Decrypt)\b(?!\s*\()",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void BeCreatedOnceRatherThanFromAMethodGroupPerPacket()
    {
        string root = RepositoryLayout.Root();
        List<string> methodGroups = [];

        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('*'))
                    continue;

                if (s_methodGroup.IsMatch(line))
                    methodGroups.Add($"{relative}:{i + 1}: {line}");
            }
        }

        Assert.True(
            methodGroups.Count == 0,
            "Pass CryptoSession.Encryptor (or a decrypt delegate created once) instead of the method group:"
            + Environment.NewLine + string.Join(Environment.NewLine, methodGroups));
    }
}
