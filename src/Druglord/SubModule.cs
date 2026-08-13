using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Druglord;

public sealed class SubModule : MBSubModuleBase
{
    private const string HarmonyId = "com.nxyexiong.druglord";

    private Harmony? _harmony;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();

        _harmony = new Harmony(HarmonyId);
        HarmonyPatches.Apply(_harmony);

        Module.CurrentModule.AddInitialStateOption(
            new InitialStateOption(
                "Druglord.ShowVersion",
                new TextObject("{=Druglord_MainMenuOption}Druglord"),
                9990,
                ShowVersion,
                () => (false, new TextObject(string.Empty))));

        Module.CurrentModule.AddInitialStateOption(
            new InitialStateOption(
                "Druglord.DebugBattle",
                new TextObject("{=!}Druglord Debug Battle"),
                9991,
                DebugBattleLauncher.Launch,
                () => DebugBattleLauncher.IsPending
                    ? (true, new TextObject("{=!}The debug battle is loading."))
                    : (false, new TextObject(string.Empty)),
                new TextObject("{=!}Launch a custom battle where every soldier has a rifle and ammunition.")));
    }

    protected override void OnSubModuleUnloaded()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        base.OnSubModuleUnloaded();
    }

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);
        DebugBattleLauncher.Tick();
    }

    public override void OnBeforeMissionBehaviorInitialize(Mission mission)
    {
        base.OnBeforeMissionBehaviorInitialize(mission);
        mission.AddMissionBehavior(new FirearmMissionLogic());
        mission.AddMissionBehavior(new RifleControlMissionLogic());

        if (DebugBattleLauncher.ConsumeLoadoutRequest())
        {
            mission.AddMissionBehavior(new DebugFirearmLoadoutMissionLogic());
        }
    }

    public override void OnGameInitializationFinished(Game game)
    {
        base.OnGameInitializationFinished(game);
        FirearmItemRegistry.EnsureLoaded(game);
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
