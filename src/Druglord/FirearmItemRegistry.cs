using System;
using System.IO;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;

namespace Druglord;

internal static class FirearmItemRegistry
{
    internal const string HandgunId = "druglord_prototype_handgun";
    internal const string RifleId = "druglord_prototype_rifle";
    internal const string CartridgeId = "druglord_cartridge";

    private const string ModuleId = "Druglord";
    private const string ItemsFileName = "druglord_items.xml";

    private static Game? _registeredGame;
    private static ItemObject? _handgun;
    private static ItemObject? _rifle;
    private static ItemObject? _cartridges;

    internal static ItemObject Handgun =>
        _handgun ?? throw new InvalidOperationException("Druglord handgun is unavailable.");

    internal static ItemObject Rifle =>
        _rifle ?? throw new InvalidOperationException("Druglord rifle is unavailable.");

    internal static ItemObject Cartridges =>
        _cartridges ?? throw new InvalidOperationException("Druglord cartridges are unavailable.");

    internal static void EnsureLoaded(Game game)
    {
        if (TryResolveItems(game))
        {
            LogReadyForNewGame(game);
            return;
        }

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

        Debug.Print("Druglord: registering missing firearm items after game initialization.");

        XmlDocument document = new XmlDocument();
        document.Load(itemsPath);

        XmlNode? root = document.DocumentElement;
        if (root is null || root.Name != "Items")
        {
            throw new InvalidDataException(
                "Druglord firearm item definitions must have an Items root node.");
        }

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

            if (game.ObjectManager.GetObject<ItemObject>(itemId) is null &&
                game.ObjectManager.CreateObjectFromXmlNode(itemNode) is null)
            {
                throw new InvalidOperationException(
                    $"Druglord could not register firearm item '{itemId}'.");
            }
        }

        if (!TryResolveItems(game))
        {
            throw new InvalidOperationException(
                "Druglord firearm items were still unavailable after registration.");
        }

        LogReadyForNewGame(game);
    }

    private static bool TryResolveItems(Game game)
    {
        ItemObject? handgun = game.ObjectManager.GetObject<ItemObject>(HandgunId);
        ItemObject? rifle = game.ObjectManager.GetObject<ItemObject>(RifleId);
        ItemObject? cartridges = game.ObjectManager.GetObject<ItemObject>(CartridgeId);

        if (handgun is null ||
            rifle is null ||
            cartridges is null ||
            !handgun.IsReady ||
            !rifle.IsReady ||
            !cartridges.IsReady)
        {
            _handgun = null;
            _rifle = null;
            _cartridges = null;
            return false;
        }

        _handgun = handgun;
        _rifle = rifle;
        _cartridges = cartridges;
        return true;
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
