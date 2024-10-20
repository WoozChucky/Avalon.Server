using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Avalon.Common.Mathematics;
using Avalon.Hosting;
using Avalon.World.Public;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Abstractions;

public delegate void ScriptCompiledEventHandler(Assembly assembly);

public interface IScriptCompiler
{
    event ScriptCompiledEventHandler? ScriptCompiled;
    void Start();
    void Stop();
}

public class ScriptCompiler : IScriptCompiler
{
    public event ScriptCompiledEventHandler? ScriptCompiled;

    private ILogger<ScriptCompiler> _logger;
    private FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, DateTime> _debounceDictionary = new();
    
    private const string ScriptsPath = "Scripts\\HotReload";
    //private const string ScriptsPath = "C:\\dev\\Avalon.Server\\src\\Server\\Avalon.World\\Scripts\\Creatures";
    private const string ScriptExtension = ".cs";
    private const int DebounceDelayMs = 500; // Delay in milliseconds to wait before compiling the script
    private bool _firstRun = true;
    
    public ScriptCompiler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ScriptCompiler>();
        
        var path = Path.Combine(Directory.GetCurrentDirectory(), ScriptsPath);
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
        
        References.AddRange(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic) // Only include non-dynamic assemblies
            .Select(a => MetadataReference.CreateFromFile(a.Location)));
    }
    
    private static readonly List<MetadataReference> References =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(AiScript).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Vector3).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ILoggerFactory).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ChunkMetadata).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
        MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(TimeSpan).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ValueType).Assembly.Location)
    ];
    
    private static readonly List<string> DefaultUsings =
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
    
    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
    }

    private void OnScriptChanged(object sender, FileSystemEventArgs e)
    {
        Debounce(e.FullPath);
    }
    
    private async void Debounce(string path)
    {
        if (_firstRun)
        {
            _firstRun = false;
            return;
        }
        
        _debounceDictionary[path] = DateTime.UtcNow;

        await Task.Delay(DebounceDelayMs);

        if (_debounceDictionary.TryGetValue(path, out var lastWriteTime) && lastWriteTime.AddMilliseconds(DebounceDelayMs) <= DateTime.UtcNow)
        {
            _debounceDictionary.TryRemove(path, out _);

            var assembly = await Task.Run(async () => await GenerateAssemblyAsync());

            if (assembly == null)
            {
                _logger.LogDebug("Failed to compile script {Path}", path);
                return;
            }

            _logger.LogInformation("Script {Path} has been compiled", path);

            ScriptCompiled?.Invoke(assembly);
        }
    }

    private async Task<Assembly?> GenerateAssemblyAsync()
    {
        var sw = Stopwatch.StartNew();
        var files = Directory.GetFiles(ScriptsPath, $"*{ScriptExtension}", SearchOption.AllDirectories);
        var syntaxTrees = new List<SyntaxTree>();
        
        foreach (var file in files)
        {
            var code = await File.ReadAllTextAsync(file);

            foreach (var defaultUsing in DefaultUsings)
            {
                if (!code.Contains($"using {defaultUsing};"))
                {
                    code = $"using {defaultUsing};\n{code}";
                }
            }
            
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            syntaxTrees.Add(syntaxTree);
        }

        var compilation = CSharpCompilation.Create(
            $"Scripts_{Guid.NewGuid()}",
            syntaxTrees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    
                    _logger.LogError("Error {Error} in script {File}", diagnostic.GetMessage(), diagnostic.Location.GetLineSpan().ToString());
                }
            }

            return null;
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());
        
        sw.Stop();
        _logger.LogDebug("Compiled scripts in {Elapsed}ms", sw.ElapsedMilliseconds);
        
        return assembly;
    }
}
