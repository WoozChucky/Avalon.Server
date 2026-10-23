using Avalon.Crashes.Worker.Symbols;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Symbols;

public sealed class SymbolIdentityValidatorShould
{
    internal static readonly ModuleIdentity s_runtime = new("runtime.exe", "123456781000", "runtime.pdb", "00112233445566778899AABBCCDDEEFF1", null, 0x10000, 0x1000);

    [Fact]
    public async Task Should_require_matching_pe_and_pdb()
    {
        string file = Path.GetTempFileName();
        try { Assert.False(await SymbolIdentityValidator.ValidatePairAsync(s_runtime, file, file, default)); }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("MODULE windows x86_64 BAD runtime.pdb\nFUNC 1 1 0 f\nSTACK CFI INIT 1 1 .cfa: $rsp\n")]
    [InlineData("MODULE windows x86_64 00112233445566778899AABBCCDDEEFF1 runtime.pdb\nFUNC 1 1 0 f\n")]
    [InlineData("MODULE windows x86_64 00112233445566778899AABBCCDDEEFF1 runtime.pdb\nFUNC garbage\nSTACK CFI INIT garbage\n")]
    [InlineData("MODULE windows x86_64 00112233445566778899AABBCCDDEEFF1 runtime.pdb\nINFO CODE_ID BAD runtime.exe\nFUNC 1 1 0 f\nSTACK CFI INIT 1 1 .cfa: $rsp\n")]
    public async Task Should_refuse_wrong_identity_or_missing_unwind(string content)
    {
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, content);
            Assert.False(await SymbolIdentityValidator.ValidateConvertedAsync(s_runtime, file, default));
        }
        finally { File.Delete(file); }
    }
}
