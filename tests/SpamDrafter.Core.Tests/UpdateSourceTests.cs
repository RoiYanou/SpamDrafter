using SpamDrafter.Core.Updates;

namespace SpamDrafter.Core.Tests;

public class UpdateSourceTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "spamdrafter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void NoSourceWhenNothingConfigured() =>
        Assert.Null(UpdateSource.Resolve(null, TempDir()));

    [Fact]
    public void EnvironmentVariableWins()
    {
        var d = TempDir();
        File.WriteAllText(Path.Combine(d, UpdateSource.FileName), @"C:\fichier");
        Assert.Equal(@"C:\variable", UpdateSource.Resolve(@"  C:\variable ", d));
    }

    [Fact]
    public void FileFirstNonEmptyNonCommentLine()
    {
        var d = TempDir();
        File.WriteAllText(Path.Combine(d, UpdateSource.FileName), "# commentaire\n\n  D:\\Mises à jour  \nautre\n");
        Assert.Equal(@"D:\Mises à jour", UpdateSource.Resolve("", d));
    }
}
