using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Avalon.Client.Patcher
{
    internal class Program
    {
        private static Guid ClientId;
        
        
        private static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            
            if (!Directory.Exists(Directory.GetCurrentDirectory() + "/patcher"))
                Directory.CreateDirectory(Directory.GetCurrentDirectory() + "/patcher");

            // Get hashes of all files in the client's directory, plus the remote hashes for current version
            
            // Key = file name, Value = hash
            var localHashes = GetLocalHashes();
            var remoteHashes = await GetRemoteHashes();

            // Compare the hashes of the files in the dictionary with current patch version hashes
            // If the hashes are different, transfer the file from a specific url to the client's directory
            
            var filesTransferred = new List<string>();
            
            foreach (var (remoteFilename, remoteHash) in remoteHashes)
            {
                if (localHashes.TryGetValue(remoteFilename, out var localHash))
                {
                    if (localHash != remoteHash)
                    {
                        // Download the file from the server
                        await DownloadFile(remoteFilename);
                        filesTransferred.Add(remoteFilename);
                    }
                }
                else
                {
                    // Download the file from the server
                    await DownloadFile(remoteFilename);
                    filesTransferred.Add(remoteFilename);
                }
            }

            foreach (var file in filesTransferred)
            {
                if (File.Exists(Directory.GetCurrentDirectory() + "/" + file))
                {
                    File.Delete(Directory.GetCurrentDirectory() + "/" + file);
                }
                
                var directoryName = Path.GetDirectoryName(file);
                
                if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
                    Directory.CreateDirectory(directoryName);

                // Copy file from patcher directory to client directory
                File.Copy(Directory.GetCurrentDirectory() + "/patcher/" + file, Directory.GetCurrentDirectory() + "/" + file);
                Console.WriteLine($"Updated {file}");
                
                // Delete file from patcher directory
                File.Delete(Directory.GetCurrentDirectory() + "/patcher/" + file);
            }
            
            Console.WriteLine("Finished updating client. Press any key to exit.."); 
            Console.ReadKey();

            Environment.Exit(0);

            // If the file doesn't exist in the client's directory, transfer the file from a specific url to the client's directory
            
            //var json = JsonSerializer.Serialize(localHashes, new JsonSerializerOptions
            //{
            //    WriteIndented = true,
            //});

            //Console.WriteLine(json);
        }
        
        private static async Task DownloadFile(string filename)
        {

            var baseUrl = "http://85.246.128.207:8080/patch/";
            var url = $"{baseUrl}{filename}";
            
            var savePath = Path.Combine(Directory.GetCurrentDirectory(), "patcher", filename);
            
            using var client = new HttpClient();

            try
            {
                // Send the GET request and retrieve the response
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                
                response.EnsureSuccessStatusCode();

                // Get the content length from the response headers
                long? totalBytes = response.Content.Headers.ContentLength;

                // Create a stream to save the downloaded file
                if (!Directory.Exists(Path.GetDirectoryName(savePath)))
                    Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                await using Stream fileStream = File.Create(savePath);
                
                // Get the response content stream
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                
                byte[] buffer = new byte[4096];
                
                int bytesRead;
                long bytesDownloaded = 0;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    bytesDownloaded += bytesRead;

                    // Calculate the progress percentage
                    int progressPercentage = (int)((double)bytesDownloaded / totalBytes * 100);

                    Console.WriteLine($"Downloading {filename} {bytesDownloaded}/{totalBytes} bytes ({progressPercentage}%).");
                }
                
                // Use the CopyToAsync method to copy the content stream to the file stream,
                // while tracking the download progress
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        }

        private static async Task<Dictionary<string, string>> GetRemoteHashes()
        {
            // List all files in a directory recursively to get the hash of each file
            // and store it in a dictionary with the file path as the key and the hash as the value
            using var wc = new HttpClient();
            var result = await wc.GetFromJsonAsync<Dictionary<string, string>>("http://85.246.128.207:8080/hash.json");
            if (result == null)
            {
                throw new Exception("Failed to get remote hashes");
            }
            return result;
        }

        private static Dictionary<string, string> GetLocalHashes()
        {
            var localHashes = new Dictionary<string, string>();
            // List all files in a directory recursively to get the hash of each file
            // and store it in a dictionary with the file path as the key and the hash as the value
            var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.*", SearchOption.AllDirectories);
            files = files.Where(f => !f.Contains("Patcher.dll") && !f.Contains("Patcher.exe")).ToArray();
            
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

        private static void OnProcessExit(object sender, EventArgs e)
        {
            Console.WriteLine("Process exit");
        }

        private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;

            File.WriteAllText("patcher.txt", ex + "\n\n" + ex?.StackTrace ?? "Unknown exception");
        }
    }
}
