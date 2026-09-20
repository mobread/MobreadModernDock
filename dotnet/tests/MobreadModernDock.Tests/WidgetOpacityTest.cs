using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;
using Xunit;

namespace MobreadModernDock.Tests;

public class WidgetOpacityTest
{
    [Fact]
    public void DefaultMode_FollowsGlobal()
    {
        var def = new WidgetDefinition { Type = WidgetTypes.Text };
        Assert.Equal(0.55, CommonWidgetSettings.EffectiveOpacity(def, 55), 3);
        Assert.Equal(1.0, CommonWidgetSettings.EffectiveOpacity(def, 100), 3);
    }

    [Fact]
    public void CustomMode_UsesOwnValue_IgnoresGlobal()
    {
        var def = new WidgetDefinition { Type = WidgetTypes.Tray };
        def.SetSetting(CommonWidgetSettings.OpacityMode, CommonWidgetSettings.OpacityModeCustom);
        def.SetSetting(CommonWidgetSettings.Opacity, "90");
        Assert.Equal(0.9, CommonWidgetSettings.EffectiveOpacity(def, 30), 3);
    }

    [Fact]
    public void CustomMode_WithoutValue_DefaultsToOpaque()
    {
        var def = new WidgetDefinition { Type = WidgetTypes.Tray };
        def.SetSetting(CommonWidgetSettings.OpacityMode, CommonWidgetSettings.OpacityModeCustom);
        Assert.Equal(1.0, CommonWidgetSettings.EffectiveOpacity(def, 30), 3);
    }

    [Fact]
    public void Opacity_IsClampedToReadableRange()
    {
        var def = new WidgetDefinition { Type = WidgetTypes.Text };
        def.SetSetting(CommonWidgetSettings.OpacityMode, CommonWidgetSettings.OpacityModeCustom);
        def.SetSetting(CommonWidgetSettings.Opacity, "0");
        Assert.Equal(0.2, CommonWidgetSettings.EffectiveOpacity(def, 100), 3);
        Assert.Equal(0.2, CommonWidgetSettings.EffectiveOpacity(new WidgetDefinition(), 5), 3);
        Assert.Equal(1.0, CommonWidgetSettings.EffectiveOpacity(new WidgetDefinition(), 250), 3);
    }
}
