using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;

namespace Druglord;

internal enum RifleFireMode
{
    Automatic,
    SemiAutomatic
}

internal enum RifleMuzzleFace
{
    MinX,
    MaxX,
    MinY,
    MaxY,
    MinZ,
    MaxZ
}

internal sealed class RifleSettings
{
    internal RifleSettings(
        string itemId,
        string ammunitionItemId,
        string soundEvent,
        bool isDebugLoadout,
        RifleFireMode fireMode,
        short magazineSize,
        int projectileCountPerShot,
        bool isExplosive,
        float shotInterval,
        float raiseDuration,
        float recoilDuration,
        float reloadDuration,
        int peakRecoilShotCount,
        float recoilResetDelay,
        float minimumVerticalKickDegrees,
        float maximumVerticalKickDegrees,
        float maximumHorizontalKickDegrees,
        float minimumSpreadDegrees,
        float maximumSpreadDegrees,
        string muzzleMeshMaterial,
        RifleMuzzleFace muzzleFace,
        Vec3 muzzleOffset)
    {
        ItemId = itemId;
        AmmunitionItemId = ammunitionItemId;
        SoundEvent = soundEvent;
        IsDebugLoadout = isDebugLoadout;
        FireMode = fireMode;
        MagazineSize = magazineSize;
        ProjectileCountPerShot = projectileCountPerShot;
        IsExplosive = isExplosive;
        ShotInterval = shotInterval;
        RaiseDuration = raiseDuration;
        RecoilDuration = recoilDuration;
        ReloadDuration = reloadDuration;
        PeakRecoilShotCount = peakRecoilShotCount;
        RecoilResetDelay = recoilResetDelay;
        MinimumVerticalKickDegrees = minimumVerticalKickDegrees;
        MaximumVerticalKickDegrees = maximumVerticalKickDegrees;
        MaximumHorizontalKickDegrees = maximumHorizontalKickDegrees;
        MinimumSpreadDegrees = minimumSpreadDegrees;
        MaximumSpreadDegrees = maximumSpreadDegrees;
        MuzzleMeshMaterial = muzzleMeshMaterial;
        MuzzleFace = muzzleFace;
        MuzzleOffset = muzzleOffset;
    }

    internal string ItemId { get; }
    internal string AmmunitionItemId { get; }
    internal string SoundEvent { get; }
    internal bool IsDebugLoadout { get; }
    internal RifleFireMode FireMode { get; }
    internal short MagazineSize { get; }
    internal int ProjectileCountPerShot { get; }
    internal bool IsExplosive { get; }
    internal float ShotInterval { get; }
    internal float RaiseDuration { get; }
    internal float RecoilDuration { get; }
    internal float ReloadDuration { get; }
    internal int PeakRecoilShotCount { get; }
    internal float RecoilResetDelay { get; }
    internal float MinimumVerticalKickDegrees { get; }
    internal float MaximumVerticalKickDegrees { get; }
    internal float MaximumHorizontalKickDegrees { get; }
    internal float MinimumSpreadDegrees { get; }
    internal float MaximumSpreadDegrees { get; }
    internal string MuzzleMeshMaterial { get; }
    internal RifleMuzzleFace MuzzleFace { get; }
    internal Vec3 MuzzleOffset { get; }
}

internal static class RifleSettingsRegistry
{
    private const string ModuleId = "Druglord";
    private const string SettingsFileName = "druglord_rifles.xml";

    private static Game? _loadedGame;
    private static Dictionary<string, RifleSettings> _settingsByItemId =
        new Dictionary<string, RifleSettings>(StringComparer.Ordinal);
    private static IReadOnlyList<RifleSettings> _debugLoadouts =
        Array.Empty<RifleSettings>();

    internal static void EnsureLoaded(Game game)
    {
        if (ReferenceEquals(_loadedGame, game))
        {
            return;
        }

        string settingsPath = Path.Combine(
            ModuleHelper.GetModuleFullPath(ModuleId),
            "ModuleData",
            SettingsFileName);

        if (!File.Exists(settingsPath))
        {
            throw new FileNotFoundException(
                "Druglord rifle settings were not found.",
                settingsPath);
        }

        XmlDocument document = new XmlDocument();
        document.Load(settingsPath);

        XmlNode? root = document.DocumentElement;
        if (root is null || root.Name != "Rifles")
        {
            throw new InvalidDataException(
                "Druglord rifle settings must have a Rifles root node.");
        }

        Dictionary<string, RifleSettings> settingsByItemId =
            new Dictionary<string, RifleSettings>(StringComparer.Ordinal);
        List<RifleSettings> debugLoadouts =
            new List<RifleSettings>();

        foreach (XmlNode rifleNode in root.ChildNodes)
        {
            if (rifleNode.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (rifleNode.Name != "Rifle")
            {
                throw new InvalidDataException(
                    $"Unexpected element '{rifleNode.Name}' in Druglord rifle settings.");
            }

            RifleSettings settings = ParseSettings(rifleNode);
            if (settingsByItemId.ContainsKey(settings.ItemId))
            {
                throw new InvalidDataException(
                    $"Duplicate Druglord rifle settings for '{settings.ItemId}'.");
            }

            settingsByItemId.Add(settings.ItemId, settings);
            if (settings.IsDebugLoadout)
            {
                debugLoadouts.Add(settings);
            }

            ValidateGameObjects(game, settings);
        }

        if (settingsByItemId.Count == 0)
        {
            throw new InvalidDataException(
                "Druglord rifle settings do not define any rifles.");
        }

        _settingsByItemId = settingsByItemId;
        _debugLoadouts = debugLoadouts.AsReadOnly();
        _loadedGame = game;
    }

    internal static bool TryGet(
        Game game,
        string itemId,
        out RifleSettings? settings)
    {
        EnsureLoaded(game);
        return _settingsByItemId.TryGetValue(itemId, out settings);
    }

    internal static RifleSettings GetRequired(Game game, string itemId)
    {
        if (!TryGet(game, itemId, out RifleSettings? settings) ||
            settings is null)
        {
            throw new InvalidOperationException(
                $"Druglord rifle settings for '{itemId}' are unavailable.");
        }

        return settings;
    }

    internal static IReadOnlyList<RifleSettings> GetDebugLoadouts(
        Game game)
    {
        EnsureLoaded(game);

        if (_debugLoadouts.Count == 0)
        {
            throw new InvalidOperationException(
                "Druglord does not have any rifles enabled for the debug battle.");
        }

        return _debugLoadouts;
    }

    private static RifleSettings ParseSettings(XmlNode node)
    {
        string itemId = GetRequiredAttribute(node, "item_id");
        string ammunitionItemId =
            GetRequiredAttribute(node, "ammunition_item_id");
        string fireModeText = GetRequiredAttribute(node, "fire_mode");

        if (!Enum.TryParse(
                fireModeText,
                ignoreCase: true,
                out RifleFireMode fireMode))
        {
            throw new InvalidDataException(
                $"Unknown rifle fire mode '{fireModeText}' for '{itemId}'.");
        }

        string muzzleFaceText =
            GetRequiredAttribute(node, "muzzle_face");
        if (!Enum.TryParse(
                muzzleFaceText,
                ignoreCase: true,
                out RifleMuzzleFace muzzleFace))
        {
            throw new InvalidDataException(
                $"Unknown muzzle face '{muzzleFaceText}' for '{itemId}'.");
        }

        RifleSettings settings = new RifleSettings(
            itemId,
            ammunitionItemId,
            GetRequiredAttribute(node, "sound_event"),
            ParseBoolean(node, "debug_loadout", false),
            fireMode,
            ParseInt16(node, "magazine_size"),
            ParseInt32(node, "projectile_count_per_shot"),
            ParseRequiredBoolean(node, "is_explosive"),
            ParseSingle(node, "shot_interval"),
            ParseSingle(node, "raise_duration"),
            ParseSingle(node, "recoil_duration"),
            ParseSingle(node, "reload_duration"),
            ParseInt32(node, "peak_recoil_shot_count"),
            ParseSingle(node, "recoil_reset_delay"),
            ParseSingle(node, "minimum_vertical_kick_degrees"),
            ParseSingle(node, "maximum_vertical_kick_degrees"),
            ParseSingle(node, "maximum_horizontal_kick_degrees"),
            ParseSingle(node, "minimum_spread_degrees"),
            ParseSingle(node, "maximum_spread_degrees"),
            GetRequiredAttribute(node, "muzzle_mesh_material"),
            muzzleFace,
            Vec3.Parse(GetRequiredAttribute(node, "muzzle_offset")));

        ValidateValues(settings);
        return settings;
    }

    private static void ValidateValues(RifleSettings settings)
    {
        if (settings.MagazineSize <= 0)
        {
            throw InvalidValue(settings, "magazine_size");
        }

        if (settings.ProjectileCountPerShot <= 0 ||
            settings.ProjectileCountPerShot > 32)
        {
            throw InvalidValue(settings, "projectile_count_per_shot");
        }

        if (settings.IsExplosive &&
            settings.ProjectileCountPerShot != 1)
        {
            throw new InvalidDataException(
                $"Explosive rifle '{settings.ItemId}' must fire exactly " +
                "one projectile per shot.");
        }

        if (settings.ShotInterval <= 0f)
        {
            throw InvalidValue(settings, "shot_interval");
        }

        if (settings.RaiseDuration < 0f ||
            settings.RecoilDuration < 0f ||
            settings.ReloadDuration <= 0f ||
            settings.RecoilResetDelay < 0f)
        {
            throw new InvalidDataException(
                $"Rifle timing values for '{settings.ItemId}' are invalid.");
        }

        if (settings.PeakRecoilShotCount < 2)
        {
            throw InvalidValue(settings, "peak_recoil_shot_count");
        }

        if (settings.MinimumVerticalKickDegrees < 0f ||
            settings.MaximumVerticalKickDegrees <
                settings.MinimumVerticalKickDegrees ||
            settings.MaximumHorizontalKickDegrees < 0f)
        {
            throw new InvalidDataException(
                $"Rifle recoil values for '{settings.ItemId}' are invalid.");
        }

        if (settings.MinimumSpreadDegrees < 0f ||
            settings.MaximumSpreadDegrees < settings.MinimumSpreadDegrees)
        {
            throw new InvalidDataException(
                $"Rifle spread values for '{settings.ItemId}' are invalid.");
        }
    }

    private static void ValidateGameObjects(
        Game game,
        RifleSettings settings)
    {
        ItemObject? rifle =
            game.ObjectManager.GetObject<ItemObject>(settings.ItemId);
        if (rifle is null ||
            !rifle.IsReady ||
            rifle.PrimaryWeapon?.WeaponClass != WeaponClass.Musket)
        {
            throw new InvalidDataException(
                $"Configured rifle '{settings.ItemId}' is not a ready Musket item.");
        }

        ItemObject? ammunition =
            game.ObjectManager.GetObject<ItemObject>(
                settings.AmmunitionItemId);
        if (ammunition is null ||
            !ammunition.IsReady ||
            ammunition.PrimaryWeapon is null ||
            !ammunition.PrimaryWeapon.IsConsumable)
        {
            throw new InvalidDataException(
                $"Configured ammunition '{settings.AmmunitionItemId}' for " +
                $"'{settings.ItemId}' is unavailable or not consumable.");
        }
    }

    private static string GetRequiredAttribute(XmlNode node, string name)
    {
        string? value = node.Attributes?[name]?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"A Druglord rifle setting is missing '{name}'.");
        }

        return value!;
    }

    private static short ParseInt16(XmlNode node, string name)
    {
        return short.Parse(
            GetRequiredAttribute(node, name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture);
    }

    private static int ParseInt32(XmlNode node, string name)
    {
        return int.Parse(
            GetRequiredAttribute(node, name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture);
    }

    private static float ParseSingle(XmlNode node, string name)
    {
        return float.Parse(
            GetRequiredAttribute(node, name),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }

    private static bool ParseBoolean(
        XmlNode node,
        string name,
        bool defaultValue)
    {
        string? value = node.Attributes?[name]?.Value;
        if (value is null)
        {
            return defaultValue;
        }

        if (!bool.TryParse(value, out bool result))
        {
            throw new InvalidDataException(
                $"Rifle setting '{name}' must be true or false.");
        }

        return result;
    }

    private static bool ParseRequiredBoolean(
        XmlNode node,
        string name)
    {
        string value = GetRequiredAttribute(node, name);
        if (!bool.TryParse(value, out bool result))
        {
            throw new InvalidDataException(
                $"Rifle setting '{name}' must be true or false.");
        }

        return result;
    }

    private static InvalidDataException InvalidValue(
        RifleSettings settings,
        string name)
    {
        return new InvalidDataException(
            $"Rifle setting '{name}' for '{settings.ItemId}' is invalid.");
    }
}
