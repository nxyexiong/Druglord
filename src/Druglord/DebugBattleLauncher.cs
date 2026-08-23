using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

namespace Druglord;

internal static class DebugBattleLauncher
{
    private const int TroopsPerDruglordType = 10;

    private static readonly string[] DruglordTroopIds =
    {
        TroopUpgradeRegistry.RecruitId,
        TroopUpgradeRegistry.AssaultId,
        TroopUpgradeRegistry.SniperId,
        TroopUpgradeRegistry.BreacherId,
        TroopUpgradeRegistry.GrenadierId
    };

    private static bool _battleStartPending;
    private static bool _loadoutPending;
    private static bool _commandLineLaunchChecked;

    internal static void Launch()
    {
        if (_battleStartPending)
        {
            return;
        }

        _battleStartPending = true;
        MBGameManager.StartNewGame(new CustomGameManager());
    }

    internal static void Tick()
    {
        TryLaunchFromCommandLine();

        if (!_battleStartPending ||
            Game.Current?.GameType is not CustomGame ||
            Game.Current.GameStateManager.ActiveState is not CustomBattleState)
        {
            return;
        }

        _battleStartPending = false;

        if (!TryCreateBattleData(out CustomBattleData battleData, out string error))
        {
            ShowError(error);
            return;
        }

        _loadoutPending = true;
        CustomBattleHelper.StartGame(battleData);
    }

    private static void TryLaunchFromCommandLine()
    {
        if (_commandLineLaunchChecked ||
            GameStateManager.Current?.ActiveState is not InitialState)
        {
            return;
        }

        _commandLineLaunchChecked = true;
        if (Utilities.CommandLineArgumentExists("DruglordDebugBattle"))
        {
            Launch();
        }
    }

    internal static bool ConsumeLoadoutRequest()
    {
        if (!_loadoutPending)
        {
            return false;
        }

        _loadoutPending = false;
        return true;
    }

    private static bool TryCreateBattleData(
        out CustomBattleData battleData,
        out string error)
    {
        battleData = default;

        BasicCharacterObject? playerCharacter =
            Game.Current.ObjectManager.GetObject<BasicCharacterObject>("commander_1");
        BasicCharacterObject? enemyCharacter =
            Game.Current.ObjectManager.GetObject<BasicCharacterObject>("commander_2");
        BasicCultureObject? playerCulture =
            Game.Current.ObjectManager.GetObject<BasicCultureObject>("empire");
        BasicCultureObject? enemyCulture =
            Game.Current.ObjectManager.GetObject<BasicCultureObject>("vlandia");

        if (playerCharacter is null ||
            enemyCharacter is null ||
            playerCulture is null ||
            enemyCulture is null)
        {
            error = "Required custom-battle characters or cultures could not be loaded.";
            return false;
        }

        List<BasicCharacterObject> druglordTroops =
            new List<BasicCharacterObject>(DruglordTroopIds.Length);

        foreach (string troopId in DruglordTroopIds)
        {
            BasicCharacterObject? troop =
                Game.Current.ObjectManager.GetObject<BasicCharacterObject>(
                    troopId);
            if (troop is null)
            {
                error = $"Druglord troop '{troopId}' could not be loaded.";
                return false;
            }

            druglordTroops.Add(troop);
        }

        int druglordTroopCount =
            TroopsPerDruglordType * druglordTroops.Count;
        int[] playerTroops =
        {
            0,
            druglordTroopCount,
            0,
            0
        };
        int[] enemyTroops =
        {
            0,
            druglordTroopCount,
            0,
            0
        };
        List<BasicCharacterObject>[] playerTroopSelections =
            CreateTroopSelections(druglordTroops);
        List<BasicCharacterObject>[] enemyTroopSelections =
            CreateTroopSelections(druglordTroops);

        CustomBattleCombatant[] parties = CustomBattleHelper.GetCustomBattleParties(
            playerCharacter,
            null,
            enemyCharacter,
            playerCulture,
            playerTroops,
            playerTroopSelections,
            enemyCulture,
            enemyTroops,
            enemyTroopSelections,
            true);

        battleData = CustomBattleHelper.PrepareBattleData(
            playerCharacter,
            null,
            parties[0],
            parties[1],
            CustomBattlePlayerSide.Attacker,
            CustomBattlePlayerType.Commander,
            CustomBattleHelper.DefaultBattleGameTypeStringId,
            CustomBattleData.CoreContentDefaultSceneName,
            "summer",
            (float)CustomBattleTimeOfDay.Noon,
            null,
            null,
            null,
            1,
            false,
            string.Empty);

        error = string.Empty;
        return true;
    }

    private static List<BasicCharacterObject>[] CreateTroopSelections(
        List<BasicCharacterObject> druglordTroops)
    {
        return new[]
        {
            new List<BasicCharacterObject>(),
            new List<BasicCharacterObject>(druglordTroops),
            new List<BasicCharacterObject>(),
            new List<BasicCharacterObject>()
        };
    }

    private static void ShowError(string error)
    {
        Debug.Print($"Druglord: {error}");
        InformationManager.ShowInquiry(
            new InquiryData(
                "Druglord Debug Battle",
                error,
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
