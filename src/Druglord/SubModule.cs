using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Druglord;

public sealed class SubModule : MBSubModuleBase
{
    private const string HarmonyId = "com.nxyexiong.druglord";

    private Harmony? _harmony;
    private bool _postInitializationPatchesApplied;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();

        if (IsEditorProcess())
        {
            Debug.Print(
                "Druglord: skipping runtime Harmony patches in the Bannerlord editor.");
        }
        else
        {
            _harmony = new Harmony(HarmonyId);
            HarmonyPatches.Apply(_harmony);
        }

        Module.CurrentModule.AddInitialStateOption(
            new InitialStateOption(
                "Druglord.ShowVersion",
                new TextObject("{=Druglord_MainMenuOption}Druglord"),
                9990,
                ShowVersion,
                () => (false, new TextObject(string.Empty))));

#if DEBUG
        Module.CurrentModule.AddInitialStateOption(
            new InitialStateOption(
                "Druglord.DebugBattle",
                new TextObject("{=!}Druglord Debug Battle"),
                9991,
                DebugBattleLauncher.Launch,
                () => DebugBattleLauncher.IsPending
                    ? (true, new TextObject("{=!}The debug battle is loading."))
                    : (false, new TextObject(string.Empty)),
                new TextObject("{=!}Launch a custom battle with 10 of every Druglord troop on both sides and all debug firearms dropped near the player.")));
#endif
    }

    protected override void OnSubModuleUnloaded()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        _postInitializationPatchesApplied = false;
        base.OnSubModuleUnloaded();
    }

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);
        DebugBattleLauncher.Tick();
    }

    protected override void OnGameStart(
        Game game,
        IGameStarter gameStarter)
    {
        base.OnGameStart(game, gameStarter);

        if (game.GameType is not Campaign)
        {
            return;
        }

        if (gameStarter is not CampaignGameStarter campaignGameStarter)
        {
            throw new InvalidOperationException(
                "Campaign game starter is unavailable.");
        }

        campaignGameStarter.AddBehavior(
            new OutlawPartyGrowthCampaignBehavior());
    }

    public override void OnBeforeMissionBehaviorInitialize(Mission mission)
    {
        base.OnBeforeMissionBehaviorInitialize(mission);
        mission.AddMissionBehavior(new FirearmMissionLogic());
        mission.AddMissionBehavior(new RifleControlMissionLogic());
        mission.AddMissionBehavior(
            new AutomaticRifleAiMissionLogic());

        if (DebugBattleLauncher.ConsumeLoadoutRequest())
        {
            mission.AddMissionBehavior(
                new DebugFirearmLoadoutMissionLogic());
        }
    }

    public override void OnGameInitializationFinished(Game game)
    {
        base.OnGameInitializationFinished(game);

        // Patching MissileHitCallback before game data loads freezes
        // Bannerlord's cached voice-type indices at -1.
        if (_harmony is not null &&
            !_postInitializationPatchesApplied)
        {
            _postInitializationPatchesApplied =
                HarmonyPatches.ApplyAfterGameInitialization(_harmony);
        }

        FirearmItemRegistry.EnsureLoaded(game);
        RifleSettingsRegistry.EnsureLoaded(game);

        if (game.GameType is Campaign)
        {
            TroopUpgradeRegistry.EnsureConfigured(game);
        }
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

    private static bool IsEditorProcess()
    {
        return AppDomain.CurrentDomain.BaseDirectory.IndexOf(
            "Win64_Shipping_wEditor",
            StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
