using Avalon.Balance.Core;

namespace Avalon.Balance.Data;

public static class ConfigFileStore
{
    public static T Load<T>(string path, Func<string, T> parse) =>
        File.Exists(path) ? parse(File.ReadAllText(path)) : throw new FileNotFoundException($"'{path}' not found", path);

    /// <summary>Writes scenarios.json, targets.json and rotations.json into <paramref name="dir" /> in canonical form.</summary>
    public static void Write(string dir, BalanceConfig config)
    {
        (string scenarios, string targets, string rotations) = ConfigFiles.Save(config);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "scenarios.json"), scenarios);
        File.WriteAllText(Path.Combine(dir, "targets.json"), targets);
        File.WriteAllText(Path.Combine(dir, "rotations.json"), rotations);
    }
}
