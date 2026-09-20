using CedroModernDock.Core.Application;
using Xunit;

namespace CedroModernDock.Tests;

public class WidgetServiceTest
{
    [Fact]
    public void ResolveText_ReplacesHostPlaceholder()
    {
        Assert.Equal(Environment.MachineName, WidgetService.ResolveText("{host}"));
    }

    [Fact]
    public void ResolveText_ReplacesUserPlaceholder_CaseInsensitive()
    {
        Assert.Equal($"Hi {Environment.UserName}", WidgetService.ResolveText("Hi {USER}"));
    }

    [Fact]
    public void ResolveText_EmptyTemplate_FallsBackToHost()
    {
        Assert.Equal(Environment.MachineName, WidgetService.ResolveText(""));
        Assert.Equal(Environment.MachineName, WidgetService.ResolveText("   "));
    }

    [Fact]
    public void ResolveText_PlainText_Unchanged()
    {
        Assert.Equal("Just text", WidgetService.ResolveText("Just text"));
    }
}
