namespace MobreadModernDock.Tests;

using System.IO;
using MobreadModernDock.Infrastructure.Windows.Adapters;

/// <summary>Direct port of DefaultProgramLauncherTest.java</summary>
public class WindowsProgramLauncherTest
{
    private string _tempDir = null!;

    public WindowsProgramLauncherTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MobreadLauncherTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void LaunchesRegularProgramsDirectly()
    {
        string executable = Path.Combine(_tempDir, "Editor.exe");
        File.WriteAllText(executable, "");

        var command = WindowsProgramLauncher.ResolveLaunchCommand(executable);

        Assert.Equal(executable, command.ExecutablePath);
        Assert.Empty(command.Arguments);
        CleanupTempDir();
    }

    [Fact]
    public void LaunchesDiscordThroughUpdateExecutable()
    {
        string installDir = Path.Combine(_tempDir, "Discord");
        Directory.CreateDirectory(installDir);
        string updateExecutable = Path.Combine(installDir, "Update.exe");
        File.WriteAllText(updateExecutable, "");
        string versionDirectory = Path.Combine(installDir, "app-1.0.9230");
        Directory.CreateDirectory(versionDirectory);
        string discordExecutable = Path.Combine(versionDirectory, "Discord.exe");
        File.WriteAllText(discordExecutable, "");

        var command = WindowsProgramLauncher.ResolveLaunchCommand(discordExecutable);

        Assert.Equal(updateExecutable, command.ExecutablePath);
        Assert.Equal(new[] { "--processStart", "Discord.exe" }, command.Arguments);
        CleanupTempDir();
    }

    [Fact]
    public void LaunchingMissingExecutableDoesNotThrow()
    {
        var launcher = new WindowsProgramLauncher();
        string missing = Path.Combine(_tempDir, "Missing.exe");

        bool launched = launcher.Launch(missing, "Missing");

        Assert.False(launched);
        CleanupTempDir();
    }

    private void CleanupTempDir()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
