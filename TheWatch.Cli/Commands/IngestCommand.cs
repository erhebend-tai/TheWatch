using System.CommandLine;
using TheWatch.Cli.Services.Ingestion;

namespace TheWatch.Cli.Commands;

public class IngestCommand : Command
{
    public IngestCommand() : base("ingest", "Ingest and process repositories into Firebase")
    {
        var reposOption = new Option<string[]>(
            name: "--repos",
            description: "A list of repository URLs to ingest, separated by spaces.")
        {
            AllowMultipleArgumentsPerToken = true,
            IsRequired = true
        };

        AddOption(reposOption);

        this.SetHandler(async (repos) =>
        {
            var github = new GithubService();
            var storage = new MockFirebaseStorage();
            var firestore = new MockFirestore();
            var ingestor = new RepositoryIngestor(github, storage, firestore);

            await ingestor.IngestRepositoriesAsync(repos, CancellationToken.None);
        }, reposOption);
    }
}
