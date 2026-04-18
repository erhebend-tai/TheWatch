using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace TheWatch.Cli.Services.Ingestion;

public class RepositoryIngestor : IRepositoryIngestor
{
    private readonly IGithubService _github;
    private readonly IFirebaseStorageService _storage;
    private readonly IFirestoreService _firestore;
    private readonly string _localCachePath = Path.Combine(Path.GetTempPath(), "TheWatchRepoCache");

    private readonly string[] _excludedDirs = { ".git", "bin", "obj", "node_modules", "dist", "build", ".vs", ".vscode", "packages" };
    private readonly string[] _excludedExtensions = { 
        ".dll", ".exe", ".zip", ".nupkg", ".snk", ".pdb", ".bin", ".iso", ".img", 
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".svg", ".ico", ".woff", ".woff2", ".ttf", ".eot",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".suo", ".user", ".lock.json", "package-lock.json"
    };

    public RepositoryIngestor(IGithubService github, IFirebaseStorageService storage, IFirestoreService firestore)
    {
        _github = github;
        _storage = storage;
        _firestore = firestore;
    }

    public async Task IngestRepositoriesAsync(string[] repositoryUrls, CancellationToken cancellationToken)
    {
        foreach (var url in repositoryUrls)
        {
            var repoName = GetRepoNameFromUrl(url);
            var localPath = Path.Combine(_localCachePath, repoName);
            
            Console.WriteLine($"Processing repository: {repoName}...");
            await _github.CloneOrUpdateRepoAsync(url, localPath);

            var files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) 
                {
                    Console.WriteLine("Ingestion cancelled.");
                    return;
                }

                var fileInfo = new FileInfo(file);
                var relativePath = Path.GetRelativePath(localPath, file);

                // --- Exclusion Logic ---
                if (_excludedExtensions.Contains(fileInfo.Extension.ToLowerInvariant())) continue;
                var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (pathParts.Any(part => _excludedDirs.Contains(part, StringComparer.OrdinalIgnoreCase))) continue;
                if (fileInfo.Length > 5 * 1024 * 1024) continue; // 5MB limit for text files
                if (fileInfo.Length == 0) continue; // Skip empty files

                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var sanitizedContent = SanitizeContent(content);
                var hash = ComputeHash(sanitizedContent);

                var existingFile = await _firestore.GetFileByHashAsync(repoName, hash);
                if (existingFile != null) continue;

                var storagePath = $"{repoName}/{relativePath.Replace('\\', '/')}";
                
                await _storage.UploadFileAsync(storagePath, sanitizedContent);
                
                var newFile = new RepositoryFile(
                    RepoName: repoName,
                    RelativePath: relativePath,
                    FileName: Path.GetFileName(file),
                    Extension: Path.GetExtension(file),
                    ContentHash: hash,
                    Size: fileInfo.Length,
                    Content: sanitizedContent.Length > 100000 ? sanitizedContent.Substring(0, 100000) : sanitizedContent, // Truncate content for Firestore
                    LastIndexed: DateTime.UtcNow,
                    StoragePath: storagePath
                );

                await _firestore.SaveFileAsync(newFile);
                Console.WriteLine($"  + Ingested: {relativePath}");
            }
            Console.WriteLine($"✅ Finished repository: {repoName}.");
        }
    }

    private string GetRepoNameFromUrl(string url) => new Uri(url).Segments.Last().Replace(".git", "");
    private string ComputeHash(string content) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    
    private string SanitizeContent(string content)
    {
        content = content.Replace("\r\n", "\n").Trim();

        var secretPatterns = new[]
        {
            @"([""']?(?:key|secret|token|password|auth|apiKey|secretKey|authToken)[""']?\s*[:=]\s*[""']?)[A-Za-z0-9\-_+/=]{20,}([""']?)",
            @"Endpoint=sb://[^;]+;SharedAccessKeyName=[^;]+;SharedAccessKey=[^;]+",
            @"Server=tcp:[^;]+;Initial Catalog=[^;]+;User ID=[^;]+;Password=[^;]+;"
        };

        foreach (var pattern in secretPatterns)
        {
            content = Regex.Replace(content, pattern, m => $"{m.Groups[1]}[REDACTED]{m.Groups[2]}");
        }

        return content;
    }
}

// Mock Firebase services for demonstration
public interface IFirebaseStorageService { Task UploadFileAsync(string path, string content); }
public interface IFirestoreService { Task<RepositoryFile> GetFileByHashAsync(string repo, string hash); Task SaveFileAsync(RepositoryFile file); }

public class MockFirebaseStorage : IFirebaseStorageService { 
    public Task UploadFileAsync(string path, string content) {
        Console.WriteLine($"  -> Uploading to Storage: {path}");
        return Task.CompletedTask;
    } 
}
public class MockFirestore : IFirestoreService {
    private readonly Dictionary<string, RepositoryFile> _store = new();
    public Task<RepositoryFile> GetFileByHashAsync(string repo, string hash) {
        _store.TryGetValue($"{repo}-{hash}", out var file);
        return Task.FromResult(file);
    }
    public Task SaveFileAsync(RepositoryFile file) {
        _store[$"{file.RepoName}-{file.ContentHash}"] = file;
        Console.WriteLine($"  -> Saving metadata to Firestore for: {file.RelativePath}");
        return Task.CompletedTask;
    }
}
