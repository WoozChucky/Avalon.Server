using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Abstractions;

/// <summary>One compile of the hot-reload scripts: the assembly and the collectible context it alone is loaded into (#930).</summary>
public sealed record CompiledScripts(Assembly Assembly, ScriptAssemblyLoadContext Context);

public delegate void ScriptCompiledEventHandler(CompiledScripts scripts);

public interface IScriptCompiler
{
    event ScriptCompiledEventHandler? ScriptCompiled;
    void Start();
    void Stop();
}

public class ScriptCompiler : IScriptCompiler
{
    private const string ScriptsPath = "Scripts\\HotReload";

    //private const string ScriptsPath = "C:\\dev\\Avalon.Server\\src\\Server\\Avalon.World\\Scripts\\Creatures";
    private const string ScriptExtension = ".cs";
    private const int DebounceDelayMs = 500; // Delay in milliseconds to wait before compiling the script

    // Made at the first compile: the server's own assemblies, the ones a script may use. Only those on disk; an
    // assembly loaded from memory (an earlier compile among them) has no file to reference.
    private static readonly Lazy<MetadataReference[]> s_references = new(() =>
    [
        .. new[]
            {
                typeof(object).Assembly, typeof(Console).Assembly, typeof(AiScript).Assembly, typeof(Vector3).Assembly,
                typeof(ILoggerFactory).Assembly, typeof(IMapNavigator).Assembly, Assembly.Load("System.Runtime"),
                Assembly.Load("netstandard"), typeof(Task).Assembly, typeof(TimeSpan).Assembly, typeof(ValueType).Assembly
            }
            .Concat(AppDomain.CurrentDomain.GetAssemblies())
            .Where(a => !a.IsDynamic && !a.IsCollectible && !string.IsNullOrEmpty(a.Location))
            .Select(a => a.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => MetadataReference.CreateFromFile(location))
    ]);

    private static readonly List<string> s_defaultUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Threading.Tasks",
        "Avalon.Common.Mathematics",
        "Avalon.World.Public",
        "Avalon.World.Public.Characters",
        "Avalon.World.Public.Creatures",
        "Avalon.World.Public.Maps",
        "Microsoft.Extensions.Logging"
    ];

    private readonly ConcurrentDictionary<string, DateTime> _debounceDictionary = new(StringComparer.Ordinal);

    private readonly ILogger<ScriptCompiler> _logger;
    private readonly FileSystemWatcher _watcher;
    private bool _firstRun = true;

    public ScriptCompiler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ScriptCompiler>();

        string path = Path.Combine(Directory.GetCurrentDirectory(), ScriptsPath);
        //var path = Path.Combine("C:\\dev\\Avalon.Server\\src\\Server\\Avalon.World\\Scripts\\Creatures");
        Directory.CreateDirectory(path);

        _watcher = new FileSystemWatcher(path, $"*{ScriptExtension}")
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };

        _watcher.Changed += OnScriptChanged;
        _watcher.Created += OnScriptChanged;
        _watcher.Deleted += OnScriptChanged;
        _watcher.Renamed += OnScriptChanged;
    }

    /// <summary>
    /// Raised with every successful compile, on a pool thread. The receiver owns the context it is handed: it unloads
    /// it once it has replaced or refused it.
    /// </summary>
    public event ScriptCompiledEventHandler? ScriptCompiled;

    public void Start() => _watcher.EnableRaisingEvents = true;

    public void Stop() => _watcher.EnableRaisingEvents = false;

    private void OnScriptChanged(object sender, FileSystemEventArgs e) => Debounce(e.FullPath);

    private async void Debounce(string path)
    {
        if (_firstRun)
        {
            _firstRun = false;
            return;
        }

        _debounceDictionary[path] = DateTime.UtcNow;

        await Task.Delay(DebounceDelayMs, CancellationToken.None);

        if (_debounceDictionary.TryGetValue(path, out DateTime lastWriteTime) &&
            lastWriteTime.AddMilliseconds(DebounceDelayMs) <= DateTime.UtcNow)
        {
            _debounceDictionary.TryRemove(path, out _);

            CompiledScripts? scripts = await Task.Run(async () => await GenerateAssemblyAsync());

            if (scripts == null)
            {
                _logger.LogDebug("Failed to compile script {Path}", path);
                return;
            }

            _logger.LogInformation("Script {Path} has been compiled", path);

            if (ScriptCompiled is { } compiled)
                compiled(scripts);
            else
                scripts.Context.Unload();
        }
    }

    private async Task<CompiledScripts?> GenerateAssemblyAsync()
    {
        var sw = Stopwatch.StartNew();
        string[] files = Directory.GetFiles(ScriptsPath, $"*{ScriptExtension}", SearchOption.AllDirectories);
        List<string> sources = [];

        foreach (string file in files)
        {
            sources.Add(await File.ReadAllTextAsync(file, CancellationToken.None));
        }

        CompiledScripts? scripts = Compile(sources, _logger);

        sw.Stop();
        _logger.LogDebug("Compiled scripts in {Elapsed}ms", sw.ElapsedMilliseconds);

        return scripts;
    }

    /// <summary>
    /// Compiles <paramref name="sources" /> into one assembly, loaded into a context of its own: a new generation of the
    /// hot-reload scripts (#930). Null, with each error logged, when the sources do not compile. The caller owns the
    /// context: nothing else unloads it.
    /// </summary>
    public static CompiledScripts? Compile(IEnumerable<string> sources, ILogger logger)
    {
        List<SyntaxTree> syntaxTrees = [];

        foreach (string source in sources)
        {
            string code = source;
            foreach (string defaultUsing in s_defaultUsings)
            {
                if (!code.Contains($"using {defaultUsing};", StringComparison.Ordinal))
                {
                    code = $"using {defaultUsing};\n{code}";
                }
            }

            syntaxTrees.Add(CSharpSyntaxTree.ParseText(code, cancellationToken: CancellationToken.None));
        }

        string name = $"Scripts_{Guid.NewGuid()}";
        var compilation = CSharpCompilation.Create(
            name,
            syntaxTrees,
            s_references.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using MemoryStream ms = new();
        EmitResult result = compilation.Emit(ms, cancellationToken: CancellationToken.None);

        if (!result.Success)
        {
            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    logger.LogError("Error {Error} in script {File}", diagnostic.GetMessage(),
                        diagnostic.Location.GetLineSpan().ToString());
                }
            }

            return null;
        }

        ms.Seek(0, SeekOrigin.Begin);
        var context = new ScriptAssemblyLoadContext(name);
        try
        {
            return new CompiledScripts(context.LoadFromStream(ms), context);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }
}
