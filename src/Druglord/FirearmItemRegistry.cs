using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ModuleManager;

namespace Druglord;

internal static class FirearmItemRegistry
{
    internal const string CartridgeId = "druglord_cartridge";

    private const string ModuleId = "Druglord";
    private const string ItemsFileName = "druglord_items.xml";

    private static Game? _registeredGame;

    internal static void EnsureLoaded(Game game)
    {
        string itemsPath = Path.Combine(
            ModuleHelper.GetModuleFullPath(ModuleId),
            "ModuleData",
            ItemsFileName);

        if (!File.Exists(itemsPath))
        {
            throw new FileNotFoundException(
                "Druglord firearm item definitions were not found.",
                itemsPath);
        }

        XmlDocument document = new XmlDocument();
        document.Load(itemsPath);

        XmlNode? root = document.DocumentElement;
        if (root is null || root.Name != "Items")
        {
            throw new InvalidDataException(
                "Druglord firearm item definitions must have an Items root node.");
        }

        List<(string Id, XmlNode Node)> definitions =
            new List<(string Id, XmlNode Node)>();
        bool allItemsReady = true;

        foreach (XmlNode itemNode in root.ChildNodes)
        {
            if (itemNode.NodeType != XmlNodeType.Element || itemNode.Name != "Item")
            {
                continue;
            }

            string? itemId = itemNode.Attributes?["id"]?.Value;
            if (string.IsNullOrEmpty(itemId))
            {
                throw new InvalidDataException(
                    "A Druglord firearm item definition is missing its id.");
            }

            definitions.Add((itemId!, itemNode));
            ItemObject? item =
                game.ObjectManager.GetObject<ItemObject>(itemId);
            allItemsReady &= item is not null && item.IsReady;
        }

        if (allItemsReady)
        {
            LogReadyForNewGame(game);
            return;
        }

        Debug.Print("Druglord: registering missing firearm items after game initialization.");

        foreach ((string itemId, XmlNode itemNode) in definitions)
        {
            if (game.ObjectManager.GetObject<ItemObject>(itemId) is null &&
                game.ObjectManager.CreateObjectFromXmlNode(itemNode) is null)
            {
                throw new InvalidOperationException(
                    $"Druglord could not register firearm item '{itemId}'.");
            }
        }

        foreach ((string itemId, _) in definitions)
        {
            ItemObject? item =
                game.ObjectManager.GetObject<ItemObject>(itemId);
            if (item is null || !item.IsReady)
            {
                throw new InvalidOperationException(
                    $"Druglord firearm item '{itemId}' was still unavailable " +
                    "after registration.");
            }
        }

        LogReadyForNewGame(game);
    }

    internal static bool IsBullet(MissionWeapon weapon)
    {
        if (weapon.IsEmpty ||
            weapon.Item.StringId != CartridgeId ||
            weapon.CurrentUsageItem is not { } usage)
        {
            return false;
        }

        return usage.WeaponFlags.HasAnyFlag(WeaponFlags.FirearmAmmo);
    }

    private static void LogReadyForNewGame(Game game)
    {
        if (ReferenceEquals(_registeredGame, game))
        {
            return;
        }

        _registeredGame = game;
        Debug.Print(
            $"Druglord: firearm items are ready for {game.GameType.GetType().Name}.");
    }
}
