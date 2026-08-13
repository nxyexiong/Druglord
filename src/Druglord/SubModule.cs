using System;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Druglord;

public sealed class SubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();

        Module.CurrentModule.AddInitialStateOption(
            new InitialStateOption(
                "Druglord.ShowVersion",
                new TextObject("{=Druglord_MainMenuOption}Druglord"),
                9990,
                ShowVersion,
                () => (false, new TextObject(string.Empty))));
    }

    private static void ShowVersion()
    {
        Version? version = typeof(SubModule).Assembly.GetName().Version;
        string displayVersion = version is null
            ? "unknown"
            : $"v{version.Major}.{version.Minor}.{version.Build}";

        InformationManager.ShowInquiry(
            new InquiryData(
                "Druglord",
                $"Version {displayVersion}",
                true,
                false,
                "OK",
                string.Empty,
                null,
                null),
            false,
            false);
    }
}
