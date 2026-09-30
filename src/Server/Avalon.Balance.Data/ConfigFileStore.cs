namespace Avalon.Balance.Data;

public static class ConfigFileStore
{
    public static T Load<T>(string path, Func<string, T> parse) =>
        File.Exists(path) ? parse(File.ReadAllText(path)) : throw new FileNotFoundException($"'{path}' not found", path);
}
