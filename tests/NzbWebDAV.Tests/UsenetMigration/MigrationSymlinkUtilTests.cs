using NzbWebDAV.UsenetMigration.Symlinks;

namespace NzbWebDAV.Tests.UsenetMigration;

public sealed class MigrationSymlinkUtilTests
{
    [Fact]
    public void LinuxFindStartInfo_PassesHostileRootAsOneOpaqueArgument()
    {
        var hostileRoot = Path.Combine(
            Path.GetTempPath(),
            "library-\"-'-$()-; touch injected-line1\nline2");

        var startInfo = MigrationSymlinkUtil.CreateLinuxFindStartInfo(hostileRoot);

        Assert.Equal("find", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Empty(startInfo.Arguments);
        Assert.Equal(
            ["-H", Path.GetFullPath(hostileRoot), "-type", "l", "-print0"],
            startInfo.ArgumentList);
    }

    [SkippableFact]
    public void LinuxEnumeration_HandlesQuotesNewlinesAndShellMetacharactersLiterally()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux find traversal is only used on Linux.");

        var root = Path.Combine(
            Path.GetTempPath(),
            $"library-\"-'-$()-; touch injected-line1\nline2-{Guid.NewGuid():N}");
        var strmPath = Path.Combine(root, "movie.strm");
        var symlinkPath = Path.Combine(root, "episode-\nlink.mkv");
        const string targetUrl = "http://localhost:8080/content/movie.mkv?token=a&part=1";
        const string linkTarget = "missing-target.mkv";

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(strmPath, targetUrl);
            File.CreateSymbolicLink(symlinkPath, linkTarget);

            var symlinks = MigrationSymlinkUtil.GetAllSymlinks(root);

            var symlink = Assert.Single(symlinks);
            Assert.Equal(symlinkPath, symlink.SymlinkPath);
            Assert.Equal(linkTarget, symlink.TargetPath);
            Assert.DoesNotContain(symlinks, link => link.SymlinkPath == strmPath);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Enumeration_MissingRootFailsWithoutReturningPartialResults()
    {
        var missingRoot = Path.Combine(
            Path.GetTempPath(),
            $"missing-altmount-library-{Guid.NewGuid():N}");

        Assert.ThrowsAny<Exception>(() => MigrationSymlinkUtil.GetAllSymlinks(missingRoot));
    }
}
