using System.Diagnostics;
using System.Text;
using Xunit;

namespace VersionManager.Tests;

public sealed class VersionManagerBehaviorTests
{
    [Fact]
    public void Bump_Removes_Unreleased_When_Section_Is_Empty()
    {
        var repoRoot = FindRepoRoot("HomotechsualBot.sln");
        BuildVersionManager(repoRoot);

        var tempDir = Path.Combine(Path.GetTempPath(), "halo-vm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var csprojPath = Path.Combine(tempDir, "temp.csproj");
            var changelogPath = Path.Combine(tempDir, "CHANGELOG.md");

            File.WriteAllText(csprojPath, "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>");
            File.WriteAllText(changelogPath, BuildChangelog("*"));

            RunDotnet(
                repoRoot,
                $"\"artifacts/bin/VersionManager/release/VersionManager.dll\" bump --version 1.0.1 --type patch --message \"Test change\" --csproj \"{csprojPath}\" --changelog \"{changelogPath}\"");

            var updated = File.ReadAllText(changelogPath);
            Assert.DoesNotContain("## [Unreleased]", updated, StringComparison.Ordinal);
            Assert.Contains("## [1.0.1]", updated, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Bump_Keeps_Unreleased_When_Section_Has_Real_Items()
    {
        var repoRoot = FindRepoRoot("HomotechsualBot.sln");
        BuildVersionManager(repoRoot);

        var tempDir = Path.Combine(Path.GetTempPath(), "halo-vm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var csprojPath = Path.Combine(tempDir, "temp.csproj");
            var changelogPath = Path.Combine(tempDir, "CHANGELOG.md");

            File.WriteAllText(csprojPath, "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>");
            File.WriteAllText(changelogPath, BuildChangelog("* Keep me"));

            RunDotnet(
                repoRoot,
                $"\"artifacts/bin/VersionManager/release/VersionManager.dll\" bump --version 1.0.1 --type patch --message \"Test change\" --csproj \"{csprojPath}\" --changelog \"{changelogPath}\"");

            var updated = File.ReadAllText(changelogPath);
            Assert.Contains("## [Unreleased]", updated, StringComparison.Ordinal);
            Assert.Contains("* Keep me", updated, StringComparison.Ordinal);
            Assert.Contains("## [1.0.1]", updated, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string BuildChangelog(string unreleasedBullet)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Changelog");
        sb.AppendLine();
        sb.AppendLine("All notable changes to HomotechsualBot will be documented in this file.");
        sb.AppendLine();
        sb.AppendLine("## [Unreleased]");
        sb.AppendLine();
        sb.AppendLine("### Changed");
        sb.AppendLine();
        sb.AppendLine(unreleasedBullet);
        sb.AppendLine();
        sb.AppendLine("## [1.0.0] - 2026-01-01");
        sb.AppendLine();
        sb.AppendLine("### Added");
        sb.AppendLine();
        sb.AppendLine("* Initial release");
        return sb.ToString();
    }

    private static string FindRepoRoot(string solutionFileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, solutionFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root containing {solutionFileName}");
    }

    private static void BuildVersionManager(string repoRoot)
    {
        RunDotnet(repoRoot, "build tools/VersionManager/VersionManager.csproj -c Release");
    }

    private static void RunDotnet(string workingDirectory, string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet process");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"dotnet {arguments} failed with exit code {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
    }
}

