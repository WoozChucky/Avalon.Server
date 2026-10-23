using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Avalon.Crashes.Analysis;

public sealed record SignatureIdentity(int Version, string Hash, string Canonical, string Display);

public static class CrashSignature
{
    public static SignatureIdentity Create(CrashAnalysis analysis)
    {
        CrashThread? thread = analysis.Threads.FirstOrDefault(t => t.Index == analysis.Crash.CrashingThreadIndex);
        if (thread is null)
            throw new CrashInputException(CrashFailureCode.UngroupableAnalysis);
        CrashFrame? fault = thread.Frames.FirstOrDefault();
        CrashFrame[] actionable = thread.Frames.Where(f => !CrashModuleRules.IsNoise(f) && Token(f) is not null).ToArray();
        CrashFrame[] trusted = actionable.Where(f => f.Trust is "context" or "cfi" or "frame_pointer").ToArray();
        CrashFrame[] scanned = actionable.Where(f => f.Trust is "scan" or "cfi_scan").ToArray();
        CrashFrame[] chosen = trusted.Length > 0 ? trusted : scanned;
        string category = trusted.Length > 0 ? "trusted" : scanned.Length > 0 ? "scan" : "fallback";
        var tokens = new List<string> { ExceptionToken(analysis.Crash), category };

        if (fault?.ImageKey is not null && CrashModuleRules.IsGpuDriver(fault.Module))
        {
            CrashFrame? runtime = chosen.FirstOrDefault(f => CrashModuleRules.IsRuntime(f.Module));
            if (runtime is not null)
            {
                tokens.Add(fault.Module!.ToLowerInvariant());
                tokens.Add(Token(runtime)!);
            }
            else
            {
                tokens[1] = "fallback";
                tokens.Add(Unresolved(fault) ?? throw new CrashInputException(CrashFailureCode.UngroupableAnalysis));
            }
        }
        else if (chosen.Length > 0)
        {
            tokens.AddRange(chosen.Take(5).Select(f => Token(f)!));
        }
        else
        {
            tokens.Add(fault is null ? throw new CrashInputException(CrashFailureCode.UngroupableAnalysis)
                : Unresolved(fault) ?? throw new CrashInputException(CrashFailureCode.UngroupableAnalysis));
        }

        // JSON arrays preserve token boundaries even when a symbol contains separators or quotes.
        string canonical = JsonSerializer.Serialize(tokens, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        string display = string.Join(" | ", tokens);
        if (display.Length > 4096)
            display = display[..4095] + "…";
        return new(CrashModuleRules.SignatureVersion, hash, canonical, display);
    }

    private static string? Token(CrashFrame frame) => !string.IsNullOrWhiteSpace(frame.Module) && !string.IsNullOrWhiteSpace(frame.Function)
        ? frame.Module.ToLowerInvariant() + "!" + NormalizeFunction(frame.Function) : Unresolved(frame);

    private static string? Unresolved(CrashFrame frame) => frame.Module is not null && frame.ImageKey is not null && TryHex(frame.ModuleOffset, out ulong offset)
        ? frame.Module.ToLowerInvariant() + "@" + frame.ImageKey + "+0x" + offset.ToString("x16", CultureInfo.InvariantCulture) : null;

    internal static bool TryHex(string? value, out ulong number)
    {
        number = 0;
        if (value is null)
            return false;
        ReadOnlySpan<char> text = value.AsSpan();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return text.Length is > 0 and <= 16 && ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out number);
    }

    private static string ExceptionToken(CrashIdentity crash)
    {
        string? reason = crash.Reason?.Trim().ToUpperInvariant();
        return reason switch
        {
            "EXCEPTION_ACCESS_VIOLATION_READ" or "EXCEPTION_ACCESS_VIOLATION_WRITE" or "EXCEPTION_ACCESS_VIOLATION_EXEC" or "EXCEPTION_ACCESS_VIOLATION" => reason.ToLowerInvariant(),
            "EXCEPTION_STACK_OVERFLOW" or "EXCEPTION_ILLEGAL_INSTRUCTION" or "EXCEPTION_INT_DIVIDE_BY_ZERO" or "EXCEPTION_BREAKPOINT" or "EXCEPTION_FLT_DIVIDE_BY_ZERO" => reason.ToLowerInvariant(),
            _ => crash.Code is uint code ? "code:0x" + code.ToString("x8", CultureInfo.InvariantCulture) : "unknown"
        };
    }

    private static string NormalizeFunction(string original)
    {
        string function = string.Join(" ", original.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var result = new StringBuilder(function.Length);
        for (int i = 0; i < function.Length; i++)
        {
            int operatorLength = OperatorLength(function, i);
            if (operatorLength > 0)
            {
                result.Append(function, i, operatorLength);
                i += operatorLength - 1;
                continue;
            }
            if (function[i] != '<')
            {
                result.Append(function[i]);
                continue;
            }
            int depth = 1;
            int end = i + 1;
            for (; end < function.Length && depth > 0; end++)
            {
                int nestedOperator = OperatorLength(function, end);
                if (nestedOperator > 0)
                {
                    end += nestedOperator - 1;
                    continue;
                }
                if (function[end] == '<')
                    depth++;
                else if (function[end] == '>')
                    depth--;
            }
            if (depth != 0)
                return function;
            i = end - 1;
        }
        return result.ToString();
    }

    private static int OperatorLength(string value, int index)
    {
        if (!value.AsSpan(index).StartsWith("operator", StringComparison.Ordinal))
            return 0;
        int start = index + 8;
        while (start < value.Length && char.IsWhiteSpace(value[start]))
            start++;
        foreach (string token in new[] { "<=>", "<<=", ">>=", "<<", ">>", "<=", ">=", "<", ">", "()", "[]" })
        {
            if (value.AsSpan(start).StartsWith(token, StringComparison.Ordinal))
                return start - index + token.Length;
        }
        return 8;
    }
}
