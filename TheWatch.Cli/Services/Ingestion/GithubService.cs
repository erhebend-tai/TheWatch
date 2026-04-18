using System.Diagnostics;
using System.Threading.Tasks;

namespace TheWatch.Cli.Services.Ingestion;

public class GithubService : IGithubService
{
    public async Task<string> CloneOrUpdateRepoAsync(string url, string localPath)
    {
        if (Directory.Exists(localPath))
        {
            await RunGitCommand($"pull", localPath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath));
            await RunGitCommand($"clone {url} \"{localPath}\"");
        }
        return localPath;
    }

    private async Task RunGitCommand(string command, string workingDirectory = "")
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"Git command failed: {error}");
        }
    }
}
