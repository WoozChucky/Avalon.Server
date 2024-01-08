using System.Security.Cryptography;
using System.Text.Json;

namespace Avalon.Client.PatchGenerator;

internal class Program
{
    private static async Task Main(string[] args)
    {
        
        // Key = file name, Value = hash
        var localHashes = GetLocalHashes();
        
        
        // If the file doesn't exist in the client's directory, transfer the file from a specific url to the client's directory
        
        var json = JsonSerializer.Serialize(localHashes, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        await File.WriteAllTextAsync(Directory.GetCurrentDirectory() + "/hash.json", json);
    }
    
    private static Dictionary<string, string> GetLocalHashes()
    {
        var localHashes = new Dictionary<string, string>();
        // List all files in a directory recursively to get the hash of each file
        // and store it in a dictionary with the file path as the key and the hash as the value
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.*", SearchOption.AllDirectories);

        files = files.Where(ExcludeFiles).ToArray();
        
        foreach (var file in files)
        {
            var filename = file.Replace(Directory.GetCurrentDirectory(), "")[1..];

            localHashes[filename] = GetHash(file);
        }
            
        return localHashes;
    }
    
    private static string GetHash(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open);
        using var bs = new BufferedStream(fs);
        using var cryptoProvider = SHA1.Create();
            
        return BitConverter.ToString(cryptoProvider.ComputeHash(bs));
    }

    private static bool ExcludeFiles(string filename)
    {
        return filename switch
        {
            "Patcher.exe" => false,
            "Patcher.dll" => false,
            "crash.txt" => false,
            "patcher.txt" => false,
            "hash.json" => false,
            _ => true
        };
    }
}
