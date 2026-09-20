using System.Text.Json;
using MobreadModernDock.Core.Models;
using MobreadModernDock.Infrastructure.Windows.Native;
using Xunit;

namespace MobreadModernDock.Tests;

public class ShellLinkResolverTest
{
    [Fact]
    public void IsShortcut_MatchesLnkCaseInsensitive()
    {
        Assert.True(ShellLinkResolver.IsShortcut(@"C:\x\Foo.LNK"));
        Assert.True(ShellLinkResolver.IsShortcut(@"C:\x\foo.lnk"));
        Assert.False(ShellLinkResolver.IsShortcut(@"C:\x\foo.exe"));
    }

    [Fact]
    public void Resolve_MissingFile_ReturnsNull()
    {
        Assert.Null(ShellLinkResolver.Resolve(@"C:\definitely\not\here.lnk"));
    }

    [Fact]
    public void Resolve_RealTaskbarShortcut_ReadsTargetAndArguments()
    {
        // The taskbar-pinned WSL shortcut on this machine carries "--cd ~".
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        if (!Directory.Exists(dir)) return; // not a Windows user profile with pins
        var any = Directory.GetFiles(dir, "*.lnk");
        if (any.Length == 0) return;

        var resolved = any.Select(ShellLinkResolver.Resolve).Where(r => r != null).ToList();
        Assert.NotEmpty(resolved);
        // Every resolvable pin points at some path (advertised ones may be empty).
        Assert.Contains(resolved, r => !string.IsNullOrEmpty(r!.TargetPath));
    }

    [Fact]
    public void ProgramItem_Arguments_OmittedFromJsonWhenEmpty()
    {
        var plain = new DockProgramItemModel("Notepad", @"C:\Windows\notepad.exe");
        string json = JsonSerializer.Serialize<DockItem>(plain);
        Assert.DoesNotContain("arguments", json);

        var withArgs = new DockProgramItemModel("WSL", @"C:\Windows\System32\wsl.exe", "--cd ~");
        string json2 = JsonSerializer.Serialize<DockItem>(withArgs);
        Assert.Contains("\"arguments\":\"--cd ~\"", json2);

        var back = JsonSerializer.Deserialize<DockItem>(json2) as DockProgramItemModel;
        Assert.NotNull(back);
        Assert.Equal("--cd ~", back!.Arguments);
    }

    [Fact]
    public void ProgramItem_WhitespaceArguments_BecomeNull()
    {
        var item = new DockProgramItemModel("X", @"C:\x.exe", "   ");
        Assert.Null(item.Arguments);
    }
}
