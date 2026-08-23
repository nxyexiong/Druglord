using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Druglord;

internal static class MissionMissileLauncher
{
    private delegate void ShootMissileDelegate(
        Mission mission,
        Agent shooterAgent,
        EquipmentIndex weaponIndex,
        Vec3 position,
        Vec3 velocity,
        Mat3 orientation,
        bool hasRigidBody,
        bool isPrimaryWeaponShot,
        int forcedMissileIndex);

    private static ShootMissileDelegate? _shootMissile;

    internal static void Initialize()
    {
        if (_shootMissile is not null)
        {
            return;
        }

        System.Reflection.MethodInfo shootMethod = AccessTools.Method(
            typeof(Mission),
            "OnAgentShootMissile",
            new[]
            {
                typeof(Agent),
                typeof(EquipmentIndex),
                typeof(Vec3),
                typeof(Vec3),
                typeof(Mat3),
                typeof(bool),
                typeof(bool),
                typeof(int)
            }) ?? throw new MissingMethodException(
                typeof(Mission).FullName,
                "OnAgentShootMissile");

        _shootMissile = (ShootMissileDelegate)shootMethod.CreateDelegate(
            typeof(ShootMissileDelegate));
    }

    internal static void Shoot(
        Mission mission,
        Agent shooterAgent,
        EquipmentIndex weaponIndex,
        Vec3 position,
        Vec3 velocity,
        Mat3 orientation,
        bool hasRigidBody,
        bool isPrimaryWeaponShot,
        int forcedMissileIndex)
    {
        (_shootMissile ?? throw new InvalidOperationException(
            "Druglord missile launching was not initialized."))(
            mission,
            shooterAgent,
            weaponIndex,
            position,
            velocity,
            orientation,
            hasRigidBody,
            isPrimaryWeaponShot,
            forcedMissileIndex);
    }
}
