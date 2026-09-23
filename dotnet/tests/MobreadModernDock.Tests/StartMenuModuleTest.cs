namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;
using MobreadModernDock.Infrastructure.Windows.Adapters;

public class StartMenuModuleTest
{
    [Fact]
    public void LaunchStartMenu_SendsWindowsKeyPress()
    {
        var sender = new RecordingInputSender { Result = true };
        var launcher = new WindowsModuleLauncher(sender);

        launcher.Launch("start", "Start Menu");

        Assert.True(sender.WasCalled);
    }

    [Fact]
    public void LaunchStartMenu_ThrowsWhenInputSenderFails()
    {
        var launcher = new WindowsModuleLauncher(new RecordingInputSender { Result = false });

        Assert.Throws<InvalidOperationException>(
            () => launcher.Launch("start", "Start Menu"));
    }

    private sealed class RecordingInputSender : IWindowsInputSender
    {
        public bool Result { get; init; }
        public bool WasCalled { get; private set; }

        public bool SendWindowsKeyPress()
        {
            WasCalled = true;
            return Result;
        }

        public bool SendKeyChord(ushort[] modifiers, ushort key) => Result;
    }
}
