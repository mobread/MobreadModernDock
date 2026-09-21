namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>Language bundle loading, fallbacks and format arguments.</summary>
public class LocalizationServiceTest
{
    [Fact]
    public void LocalizesBuiltInItemsButKeepsProgramLabelsUntouched()
    {
        var repository = new InMemoryDockRepository();
        var dockService = new DockService(repository);
        var localizationService = new LocalizationService(dockService);

        Assert.Equal("Settings", localizationService.DockItemLabel(new DockSettingsItemModel()));
        Assert.Equal("Start Menu", localizationService.DockItemLabel(new DockWindowsModuleItemModel("Start Menu", "start")));
        Assert.Equal("Control Panel", localizationService.DockItemLabel(new DockWindowsModuleItemModel("Control Panel", "ctrlpnl")));
        Assert.Equal("Discord", localizationService.DockItemLabel(new DockProgramItemModel("Discord", @"C:\Discord.exe")));

        localizationService.SetLanguage(SupportedLanguage.PT_BR);

        Assert.Equal("Configurações", localizationService.DockItemLabel(new DockSettingsItemModel()));
        Assert.Equal("Painel de Controle", localizationService.DockItemLabel(new DockWindowsModuleItemModel("Control Panel", "ctrlpnl")));
        Assert.Equal("Discord", localizationService.DockItemLabel(new DockProgramItemModel("Discord", @"C:\Discord.exe")));
    }

    [Fact]
    public void PersistsLanguageAndNotifiesListeners()
    {
        var repository = new InMemoryDockRepository();
        var dockService = new DockService(repository);
        var localizationService = new LocalizationService(dockService);
        int notificationCount = 0;

        localizationService.AddListener(() => notificationCount++);
        localizationService.SetLanguage(SupportedLanguage.PT_BR);

        Assert.Equal(SupportedLanguage.PT_BR, repository.SavedModel!.Language);
        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public void LanguageSelectorUsesNativeLanguageNames()
    {
        var repository = new InMemoryDockRepository();
        var dockService = new DockService(repository);
        var localizationService = new LocalizationService(dockService);

        Assert.Equal("English", localizationService.LanguageDisplayName(SupportedLanguage.EN_US));
        Assert.Equal("Português (Brasil)", localizationService.LanguageDisplayName(SupportedLanguage.PT_BR));
    }

    [Fact]
    public void EverySupportedLanguageHasATranslationBundle()
    {
        foreach (SupportedLanguage language in Enum.GetValues<SupportedLanguage>())
        {
            string windowTitle = LocalizationService.BootstrapText(language, "settings.window.title");
            string moduleTitle = LocalizationService.BootstrapText(language, "windowsModule.modal.title");
            string startMenu = LocalizationService.BootstrapText(language, "windowsModule.startMenu");

            Assert.False(string.IsNullOrWhiteSpace(windowTitle), $"Missing settings.window.title for {language}");
            Assert.False(string.IsNullOrWhiteSpace(moduleTitle), $"Missing windowsModule.modal.title for {language}");
            Assert.NotEqual("settings.window.title", windowTitle);
            Assert.NotEqual("windowsModule.modal.title", moduleTitle);
            Assert.NotEqual("windowsModule.startMenu", startMenu);
        }
    }

    private sealed class InMemoryDockRepository : IDockRepository
    {
        public DockModel SavedModel { get; private set; } = new();

        public DockModel Load() => SavedModel;
        public void Save(DockModel model) => SavedModel = model;
    }
}
