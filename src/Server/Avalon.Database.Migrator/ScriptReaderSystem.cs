using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalon.Database.Migrator.Model;

namespace Avalon.Database.Migrator;

internal class ScriptReaderSystem
{
    private const string ScriptsPath = "Scripts";
    private const string SqlExtension = ".sql";
    private const string SqlExtensionPattern = "*" + SqlExtension;
    private const string CreateFolder = "Create";
    
    public static async Task<ICollection<MigrationScript>> ListCreateScriptsAsync()
    {
        var folderPath = $"{Directory.GetCurrentDirectory()}/{ScriptsPath}/{CreateFolder}";
        var createDirectoryInfo = new DirectoryInfo(folderPath);
        
        if (!createDirectoryInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Directory not found: {folderPath}");
        }
        
        var files = createDirectoryInfo.GetFiles(SqlExtensionPattern);
        
        var scripts = new List<MigrationScript>();
        
        foreach (var file in files)
        {
            scripts.Add(new MigrationScript
            {
                Name = file.Name,
                Database = null,
                Path = $"{folderPath}/{file.Name}",
                Content = await File.ReadAllTextAsync($"{folderPath}/{file.Name}")
            });
        }
        
        return scripts.OrderBy(s => s.Name).ToList();
    }
    
    public static async Task<ICollection<MigrationScript>> ListMigrationScriptsAsync(string path)
    {
        var folderPath = $"{Directory.GetCurrentDirectory()}/{ScriptsPath}/{path}";
        var directoryInfo = new DirectoryInfo(folderPath);
        
        if (!directoryInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Directory not found: {folderPath}");
        }
        
        var subDirectories = directoryInfo.GetDirectories();
        
        var scripts = new List<MigrationScript>();
        
        foreach (var subDirectory in subDirectories)
        {
            var files = subDirectory.GetFiles(SqlExtensionPattern);
            
            foreach (var file in files)
            {
                scripts.Add(new MigrationScript
                {
                    Name = file.Name,
                    Migration = path,
                    Database = subDirectory.Name,
                    Path = $"{folderPath}/{subDirectory.Name}/{file.Name}",
                    Content = await File.ReadAllTextAsync($"{folderPath}/{subDirectory.Name}/{file.Name}")
                });
            }
        }
        
        return scripts.OrderBy(s => s.Name).ToList();
    }
    
    public static async Task<ICollection<string>> ListMigrationDirectoriesAsync(string path = ScriptsPath)
    {
        var directoryPath = $"{Directory.GetCurrentDirectory()}/{path}";
        var directoryInfo = new DirectoryInfo(directoryPath);
        
        if (!directoryInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
        
        var directories = directoryInfo.GetDirectories();

        return directories
            .Select(directory => directory.Name)
            .Where(f => !f.Equals(CreateFolder, StringComparison.InvariantCultureIgnoreCase))
            .OrderBy(d => d)
            .ToList();
    }
}
