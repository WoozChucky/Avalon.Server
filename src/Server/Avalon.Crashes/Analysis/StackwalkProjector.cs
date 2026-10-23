using System.Globalization;
using System.Text.Json;

namespace Avalon.Crashes.Analysis;

public static class StackwalkProjector
{
    public static CrashAnalysis Project(ReadOnlyMemory<byte> json, CrashInput input, ProcessorIdentity tools)
    {
        if (json.Length > 16 * 1024 * 1024)
            throw new CrashInputException(CrashFailureCode.ResourceLimit);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            return new Projection(input, tools).Read(document.RootElement);
        }
        catch (JsonException)
        {
            // Never persist parser text or its inner exception, which may echo raw report material.
            throw new CrashInputException(CrashFailureCode.InvalidAnalysis);
        }
    }

    private sealed class Projection(CrashInput input, ProcessorIdentity tools)
    {
        private CrashQualityFlags _quality = input.Quality;
        private readonly HashSet<ModuleIdentity> _seenModules = [];
        private readonly List<ModuleIdentity> _relevant = [];
        private readonly ModuleIdentity[] _byAddress = input.Modules.OrderBy(m => m.BaseAddress).ToArray();

        internal CrashAnalysis Read(JsonElement root)
        {
            JsonElement info = Property(root, "crash_info");
            JsonElement[] rawThreads = Array(Property(root, "threads"));
            int? crashIndex = Integer(Property(info, "crashing_thread"));
            string? reason = Text(Property(info, "type"), 128);
            uint? code = ExceptionCode(reason);
            if (crashIndex is null || crashIndex < 0 || crashIndex >= rawThreads.Length || (reason is null && code is null))
                throw new CrashInputException(CrashFailureCode.InvalidAnalysis);

            var threads = new List<CrashThread>();
            threads.Add(ReadThread(rawThreads[crashIndex.Value], crashIndex.Value));
            for (int i = 0; i < rawThreads.Length && threads.Count < CrashLimits.Threads; i++)
            {
                if (i != crashIndex)
                    threads.Add(ReadThread(rawThreads[i], i));
            }
            if (rawThreads.Length > threads.Count)
                _quality |= CrashQualityFlags.Truncated;

            uint? threadId = threads[0].ThreadId;
            string? faultPc = Hex(Property(Array(Property(rawThreads[crashIndex.Value], "frames")).FirstOrDefault(), "offset"));
            if ((threadId is not null && threadId != input.Metadata.ThreadId) || (code is not null && code != input.Metadata.ExceptionCode)
                || (faultPc is not null && !string.Equals(faultPc, input.Metadata.ExceptionAddress, StringComparison.OrdinalIgnoreCase)))
            {
                _quality |= CrashQualityFlags.MetadataDisagreement;
            }

            JsonElement system = Property(root, "system_info");
            string? os = Text(Property(system, "os"), 128);
            string? osVersion = Text(Property(system, "os_ver"), 128);
            var machine = new CrashSystem(os, osVersion, WindowsBuild(os, osVersion),
                Text(Property(system, "cpu_arch"), 128), Text(Property(system, "cpu_info"), 512));
            IReadOnlyList<CrashModule> modules = ReadModules(Array(Property(root, "modules")));
            var identity = new CrashIdentity(reason, code, Hex(Property(info, "address")), crashIndex, threadId);
            var analysis = new CrashAnalysis(1, identity, machine, threads, modules, input.Metadata, input.LogTail, _quality, tools);
            return Fit(analysis);
        }

        private CrashThread ReadThread(JsonElement raw, int index)
        {
            JsonElement[] frames = Array(Property(raw, "frames"));
            if (frames.Length > CrashLimits.FramesPerThread)
                _quality |= CrashQualityFlags.Truncated;
            return new(index, Unsigned(Property(raw, "thread_id")), frames.Take(CrashLimits.FramesPerThread).Select(ReadFrame).ToArray());
        }

        private CrashFrame ReadFrame(JsonElement raw)
        {
            ModuleIdentity? module = null;
            string? pc = Hex(Property(raw, "offset"));
            if (CrashSignature.TryHex(pc, out ulong address))
                module = FindModule(address);
            // The pinned frame schema carries no complete image identity. Without a PC, a basename
            // and offset do not identify which of several same-named images produced the frame.
            if (module is not null && _seenModules.Add(module))
                _relevant.Add(module);
            string? relative = module is not null ? Address(address - module.BaseAddress) : null;
            JsonElement[] rawInlines = Array(Property(raw, "inlines"));
            if (rawInlines.Length > CrashLimits.InlineFrames)
                _quality |= CrashQualityFlags.Truncated;
            CrashInlineFrame[] inlines = rawInlines.Take(CrashLimits.InlineFrames).Select(f => new CrashInlineFrame(
                Text(Property(f, "function"), CrashLimits.FunctionCharacters), Source(Property(f, "file")), PositiveInteger(Property(f, "line")))).ToArray();
            string? trust = Text(Property(raw, "trust"), 128);
            if (trust is "scan" or "cfi_scan")
                _quality |= CrashQualityFlags.ScanFrames;
            string? name = module?.Name ?? Basename(String(Property(raw, "module")));
            string? function = Text(Property(raw, "function"), CrashLimits.FunctionCharacters);
            if (CrashModuleRules.IsSystem(name) && function is null)
                _quality |= CrashQualityFlags.MissingSystemSymbols;
            return new(Text(name, CrashLimits.ModuleCharacters), relative, function, Source(Property(raw, "file")), PositiveInteger(Property(raw, "line")), inlines, trust)
            { ImageKey = module is null ? null : Text(module.ImageKey, 128) };
        }

        private IReadOnlyList<CrashModule> ReadModules(JsonElement[] raw)
        {
            // Prioritize faulting/crashing-thread images before caller and other-thread images.
            // Sorting the relevant set back to dump order could evict the faulting image itself.
            ModuleIdentity[] ordered = _relevant.Concat(input.Modules.Where(m => !_seenModules.Contains(m))).ToArray();
            if (ordered.Length > CrashLimits.Modules)
                _quality |= CrashQualityFlags.Truncated;
            var result = new List<CrashModule>();
            foreach (ModuleIdentity module in ordered.Take(CrashLimits.Modules))
            {
                // Symbol availability must belong to the same image, not merely a same-named DLL.
                JsonElement upstream = raw.FirstOrDefault(r => CrashSignature.TryHex(String(Property(r, "base_addr")), out ulong start)
                    && start == module.BaseAddress && string.Equals(String(Property(r, "code_id")), module.ImageKey, StringComparison.OrdinalIgnoreCase));
                bool symbols = Property(upstream, "loaded_symbols").ValueKind == JsonValueKind.True;
                if (CrashModuleRules.IsSystem(module.Name) && !symbols)
                    _quality |= CrashQualityFlags.MissingSystemSymbols;
                result.Add(new(Text(module.Name, CrashLimits.ModuleCharacters)!, Text(module.ImageKey, 128),
                    Text(Basename(module.PdbName), CrashLimits.ModuleCharacters), Text(module.PdbKey, 128), Text(module.Version, 128),
                    Address(module.BaseAddress), Address(module.Size), symbols));
            }
            return result;
        }

        private ModuleIdentity? FindModule(ulong address)
        {
            int low = 0;
            int high = _byAddress.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                ModuleIdentity module = _byAddress[middle];
                if (address < module.BaseAddress)
                    high = middle - 1;
                else if (address - module.BaseAddress >= module.Size)
                    low = middle + 1;
                else
                    return module;
            }
            return null;
        }

        private string? Source(JsonElement value)
        {
            string? source = String(value)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(source))
                return null;
            // Only known source-root anchors are mapped; arbitrary relative/absolute paths become basenames.
            foreach (string anchor in new[] { "engine/src/", "common/src/", "runtime/src/" })
            {
                int index = source.IndexOf(anchor, StringComparison.Ordinal);
                if (index >= 0 && (index == 0 || source[index - 1] == '/') && !source[index..].Split('/').Any(p => p is ".." or "."))
                    return Text(source[index..], CrashLimits.PathCharacters);
            }
            return Text(Basename(source), CrashLimits.PathCharacters);
        }

        private string? Text(JsonElement value, int cap) => Text(String(value), cap);

        private string? Text(string? value, int cap)
        {
            if (value is null)
                return null;
            if (value.Length <= cap)
                return value;
            _quality |= CrashQualityFlags.Truncated;
            int length = char.IsHighSurrogate(value[cap - 1]) ? cap - 1 : cap;
            return value[..length];
        }

        private static CrashAnalysis Fit(CrashAnalysis analysis)
        {
            if (Size(analysis) <= CrashLimits.StackBytes)
                return analysis;
            analysis = analysis with { Quality = analysis.Quality | CrashQualityFlags.Truncated };
            CrashThread first = analysis.Threads[0];
            // Find the largest whole-thread prefix fitting the byte budget. The crashing thread
            // always leads the prefix; if it alone is too large, retain its longest frame prefix.
            int low = 0;
            int high = analysis.Threads.Count;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (Size(analysis with { Threads = analysis.Threads.Take(middle).ToArray() }) <= CrashLimits.StackBytes)
                    low = middle;
                else
                    high = middle - 1;
            }
            if (low > 0)
                return analysis with { Threads = analysis.Threads.Take(low).ToArray() };
            low = 0;
            high = first.Frames.Count;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                CrashAnalysis candidate = analysis with { Threads = [first with { Frames = first.Frames.Take(middle).ToArray() }] };
                if (Size(candidate) <= CrashLimits.StackBytes)
                    low = middle;
                else
                    high = middle - 1;
            }
            CrashAnalysis fitted = analysis with { Threads = [first with { Frames = first.Frames.Take(low).ToArray() }] };
            if ((first.Frames.Count > 0 && low == 0) || Size(fitted) > CrashLimits.StackBytes)
                throw new CrashInputException(CrashFailureCode.InvalidAnalysis);
            return fitted;
        }

        private static int Size(CrashAnalysis analysis) => JsonSerializer.SerializeToUtf8Bytes(analysis).Length;
    }

    private static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property) ? property : default;
    private static JsonElement[] Array(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    private static string? String(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : null;
    private static int? PositiveInteger(JsonElement value) => Integer(value) is int number && number > 0 ? number : null;
    private static uint? Unsigned(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint result) ? result : null;
    private static string? Hex(JsonElement value) => CrashSignature.TryHex(String(value), out ulong result) ? Address(result) : null;
    private static string Address(ulong value) => "0x" + value.ToString("x16", CultureInfo.InvariantCulture);
    private static string? Basename(string? value) => value?.Replace('\\', '/').Split('/').LastOrDefault();

    private static string? WindowsBuild(string? os, string? version)
    {
        if (!string.Equals(os, "Windows NT", StringComparison.Ordinal) || version is null)
            return null;
        string[] parts = version.Split('.');
        if (parts.Length < 3 || !parts[0].All(char.IsAsciiDigit) || !parts[1].All(char.IsAsciiDigit))
            return null;
        string build = new(parts[2].TakeWhile(char.IsAsciiDigit).ToArray());
        return build.Length == 0 ? null : build;
    }

    private static uint? ExceptionCode(string? reason)
    {
        if (reason is null)
            return null;
        string name = reason.Split(" / ", StringSplitOptions.None)[0];
        if (name is "EXCEPTION_ACCESS_VIOLATION" or "EXCEPTION_ACCESS_VIOLATION_READ" or "EXCEPTION_ACCESS_VIOLATION_WRITE" or "EXCEPTION_ACCESS_VIOLATION_EXEC")
            return 0xc0000005;
        if (name is "EXCEPTION_IN_PAGE_ERROR" or "EXCEPTION_IN_PAGE_ERROR_READ" or "EXCEPTION_IN_PAGE_ERROR_WRITE" or "EXCEPTION_IN_PAGE_ERROR_EXEC")
            return 0xc0000006;
        // Windows exception enum names from the locked rust-minidump revision. Interpret the
        // primary exception before any refinement (which may itself contain a numeric NTSTATUS).
        uint? known = name switch
        {
            "EXCEPTION_GUARD_PAGE" => 0x80000001,
            "EXCEPTION_DATATYPE_MISALIGNMENT" => 0x80000002,
            "EXCEPTION_SINGLE_STEP" => 0x80000004,
            "EXCEPTION_INVALID_HANDLE" => 0xc0000008,
            "EXCEPTION_NONCONTINUABLE_EXCEPTION" => 0xc0000025,
            "EXCEPTION_INVALID_DISPOSITION" => 0xc0000026,
            "EXCEPTION_BOUNDS_EXCEEDED" => 0xc000008c,
            "EXCEPTION_FLT_DENORMAL_OPERAND" => 0xc000008d,
            "EXCEPTION_FLT_INEXACT_RESULT" => 0xc000008f,
            "EXCEPTION_FLT_INVALID_OPERATION" => 0xc0000090,
            "EXCEPTION_FLT_OVERFLOW" => 0xc0000091,
            "EXCEPTION_FLT_STACK_CHECK" => 0xc0000092,
            "EXCEPTION_FLT_UNDERFLOW" => 0xc0000093,
            "EXCEPTION_INT_OVERFLOW" => 0xc0000095,
            "EXCEPTION_PRIV_INSTRUCTION" => 0xc0000096,
            "EXCEPTION_STACK_OVERFLOW" => 0xc00000fd,
            "EXCEPTION_ILLEGAL_INSTRUCTION" => 0xc000001d,
            "EXCEPTION_INT_DIVIDE_BY_ZERO" => 0xc0000094,
            "EXCEPTION_FLT_DIVIDE_BY_ZERO" => 0xc000008e,
            "EXCEPTION_BREAKPOINT" => 0x80000003,
            "EXCEPTION_POSSIBLE_DEADLOCK" => 0xc0000194,
            "OUT_OF_MEMORY" => 0xe0000008,
            "UNHANDLED_CPP_EXCEPTION" => 0xe06d7363,
            "MANAGED_EXCEPTION_CODE_V4" => 0xe0434352,
            "SIMULATED" => 0x0517a7ed,
            "STATUS_HEAP_CORRUPTION" => 0xc0000374,
            "STATUS_STACK_BUFFER_OVERRUN" => 0xc0000409,
            "STATUS_FAIL_FAST_EXCEPTION" => 0xc0000602,
            _ => null
        };
        if (known is not null)
            return known;
        int hex = reason.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
        if (hex >= 0)
        {
            string digits = new(reason[(hex + 2)..].TakeWhile(char.IsAsciiHexDigit).ToArray());
            if (digits.Length is > 0 and <= 8 && uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint code))
                return code;
        }
        return null;
    }
}
