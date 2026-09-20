namespace MobreadModernDock.Tests;

using System.IO;
using MobreadModernDock.Core.Application;

/// <summary>Direct port of ProgramSelectionResolverTest.java</summary>
public class ProgramSelectionResolverTest
{
    private string _tempDir = null!;

    public ProgramSelectionResolverTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MobreadTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    private void CleanupTempDir()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void KeepsRegularExecutablesUntouched()
    {
        string executable = Path.Combine(_tempDir, "Editor.exe");
        File.WriteAllText(executable, "");

        var selection = ProgramSelectionResolver.Resolve(executable);

        Assert.Equal(executable, selection.ExecutablePath);
        Assert.Equal("Editor", selection.Label);
        CleanupTempDir();
    }

    [Fact]
    public void ResolvesSquirrelUpdaterToRealApplicationExecutable()
    {
        string installDir = Path.Combine(_tempDir, "Discord");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "Update.exe"), "");
        string appDirectory = Path.Combine(installDir, "app-1.0.0");
        Directory.CreateDirectory(appDirectory);
        string discordExecutable = Path.Combine(appDirectory, "Discord.exe");
        File.WriteAllText(discordExecutable, "");

        var selection = ProgramSelectionResolver.Resolve(Path.Combine(installDir, "Update.exe"));

        Assert.Equal(discordExecutable, selection.ExecutablePath);
        Assert.Equal("Discord", selection.Label);
        CleanupTempDir();
    }

    [Fact]
    public void FallsBackToFirstExecutableWhenFolderNameDoesNotMatch()
    {
        string installDir = Path.Combine(_tempDir, "SomeWrapper");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "Update.exe"), "");
        string appDirectory = Path.Combine(installDir, "app-2.0.0");
        Directory.CreateDirectory(appDirectory);
        string realExecutable = Path.Combine(appDirectory, "RealApp.exe");
        File.WriteAllText(realExecutable, "");

        var selection = ProgramSelectionResolver.Resolve(Path.Combine(installDir, "Update.exe"));

        Assert.Equal(realExecutable, selection.ExecutablePath);
        Assert.Equal("RealApp", selection.Label);
        CleanupTempDir();
    }
}
