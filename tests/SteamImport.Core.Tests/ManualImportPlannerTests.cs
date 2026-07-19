using SteamImport.Core;

namespace SteamImport.Core.Tests;

public sealed class ManualImportPlannerTests
{
    [Fact]
    public void SelectedGameFolderProducesAReviewWithARecommendedExecutable()
    {
        using var game = TemporaryGame.Create("Neon Horizon");
        var expectedExecutable = game.AddFile("NeonHorizon.exe");
        game.AddFile("unins000.exe");

        var review = ManualImportPlanner.CreateReview(game.Path);

        Assert.Equal("Neon Horizon", review.DisplayName);
        Assert.Equal(expectedExecutable, review.RecommendedExecutable);
        Assert.Equal([expectedExecutable], review.ExecutableCandidates);
    }

    [Fact]
    public void SupportExecutablesAreExcludedFromTheReview()
    {
        using var game = TemporaryGame.Create("Moonlight");
        var expectedExecutable = game.AddFile(System.IO.Path.Combine("bin", "Moonlight.exe"));
        game.AddFile("setup.exe");
        game.AddFile("CrashReporter.exe");
        game.AddFile(System.IO.Path.Combine("redist", "vcredist_x64.exe"));

        var review = ManualImportPlanner.CreateReview(game.Path);

        Assert.Equal(expectedExecutable, review.RecommendedExecutable);
        Assert.Equal([expectedExecutable], review.ExecutableCandidates);
    }

    private sealed class TemporaryGame : IDisposable
    {
        private TemporaryGame(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryGame Create(string name)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SteamImport-{Guid.NewGuid():N}",
                name);
            Directory.CreateDirectory(path);
            return new TemporaryGame(path);
        }

        public string AddFile(string relativePath)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true);
        }
    }
}
