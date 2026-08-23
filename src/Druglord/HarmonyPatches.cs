using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;

namespace Druglord;

internal static class HarmonyPatches
{
    private static readonly PropertyInfo CameraElevationProperty =
        AccessTools.Property(
            typeof(MissionScreen),
            nameof(MissionScreen.CameraElevation));

    internal static void Apply(Harmony harmony)
    {
        ApplyPostfix(
            harmony,
            AccessTools.Method(
                typeof(WeaponComponentData),
                nameof(WeaponComponentData.GetRelevantSkillFromWeaponClass),
                new[] { typeof(WeaponClass) }),
            AccessTools.Method(
                typeof(HarmonyPatches),
                nameof(FirearmRelevantSkillPostfix)),
            "firearm skill");

        ApplyPostfix(
            harmony,
            AccessTools.Method(
                typeof(MissionMainAgentController),
                "ControlTick",
                System.Type.EmptyTypes),
            AccessTools.Method(
                typeof(HarmonyPatches),
                nameof(MainAgentInputPostfix)),
            "main-agent input");

        ApplyTranspiler(
            harmony,
            AccessTools.Method(
                typeof(Mission),
                "MissileHitCallback"),
            AccessTools.Method(
                typeof(HarmonyPatches),
                nameof(FirearmShieldPenetrationTranspiler)),
            "firearm shield penetration");
    }

    private static void FirearmRelevantSkillPostfix(
        WeaponClass weaponClass,
        ref SkillObject __result)
    {
        if (__result is null &&
            weaponClass == WeaponClass.Musket)
        {
            __result = DefaultSkills.Crossbow;
        }
    }

    private static void ApplyPostfix(
        Harmony harmony,
        MethodInfo? original,
        MethodInfo? postfix,
        string patchName)
    {
        if (original is null || postfix is null)
        {
            Debug.Print($"Druglord: Harmony target for {patchName} was not found.");
            return;
        }

        try
        {
            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
            Debug.Print($"Druglord: Harmony {patchName} patch applied.");
        }
        catch (System.Exception exception)
        {
            Debug.Print(
                $"Druglord: Harmony {patchName} patch failed: {exception}");
        }
    }

    private static void ApplyTranspiler(
        Harmony harmony,
        MethodInfo? original,
        MethodInfo? transpiler,
        string patchName)
    {
        if (original is null || transpiler is null)
        {
            Debug.Print($"Druglord: Harmony target for {patchName} was not found.");
            return;
        }

        try
        {
            harmony.Patch(
                original,
                transpiler: new HarmonyMethod(transpiler));
            Debug.Print($"Druglord: Harmony {patchName} patch applied.");
        }
        catch (System.Exception exception)
        {
            Debug.Print(
                $"Druglord: Harmony {patchName} patch failed: {exception}");
        }
    }

    private static IEnumerable<CodeInstruction>
        FirearmShieldPenetrationTranspiler(
            IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> patched =
            new List<CodeInstruction>(instructions);
        MethodInfo penetrationCheck = AccessTools.Method(
            typeof(HarmonyPatches),
            nameof(IsFirearmBulletShieldHit)) ??
            throw new MissingMethodException(
                typeof(HarmonyPatches).FullName,
                nameof(IsFirearmBulletShieldHit));

        int insertionIndex = -1;
        CodeInstruction? penetrationResultStore = null;
        int penetrationFlag = (int)WeaponFlags.CanPenetrateShield;

        for (int index = 0; index <= patched.Count - 6; index++)
        {
            if (patched[index].opcode != OpCodes.Ldc_I4_0 ||
                !IsStoreLocal(patched[index + 1]) ||
                !IsLoadLocal(patched[index + 2]) ||
                !LoadsInt32(patched[index + 3], penetrationFlag) ||
                patched[index + 4].opcode != OpCodes.Conv_I8 ||
                patched[index + 5].operand is not MethodInfo flagMethod ||
                flagMethod.Name != "HasAnyFlag")
            {
                continue;
            }

            if (insertionIndex >= 0)
            {
                throw new InvalidOperationException(
                    "Bannerlord's shield penetration branch matched more than once.");
            }

            insertionIndex = index + 2;
            penetrationResultStore = patched[index + 1];
        }

        if (insertionIndex < 0 || penetrationResultStore is null)
        {
            throw new InvalidOperationException(
                "Bannerlord's shield penetration branch was not found.");
        }

        patched.InsertRange(
            insertionIndex,
            new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call, penetrationCheck),
                new CodeInstruction(
                    penetrationResultStore.opcode,
                    penetrationResultStore.operand)
            });

        return patched;
    }

    private static bool IsFirearmBulletShieldHit(
        Mission mission,
        ref AttackCollisionData collisionData)
    {
        if (!collisionData.AttackBlockedWithShield)
        {
            return false;
        }

        int missileIndex = collisionData.AffectorWeaponSlotOrMissileIndex;
        foreach (Mission.Missile missile in mission.MissilesList)
        {
            if (missile.Index != missileIndex)
            {
                continue;
            }

            MissionWeapon weapon = missile.Weapon;
            if (!FirearmItemRegistry.IsBullet(weapon))
            {
                return false;
            }

            weapon.CurrentUsageItem.WeaponFlags |=
                WeaponFlags.CanPenetrateShield;
            return true;
        }

        return false;
    }

    private static bool IsStoreLocal(CodeInstruction instruction)
    {
        return instruction.opcode == OpCodes.Stloc ||
               instruction.opcode == OpCodes.Stloc_S ||
               instruction.opcode == OpCodes.Stloc_0 ||
               instruction.opcode == OpCodes.Stloc_1 ||
               instruction.opcode == OpCodes.Stloc_2 ||
               instruction.opcode == OpCodes.Stloc_3;
    }

    private static bool IsLoadLocal(CodeInstruction instruction)
    {
        return instruction.opcode == OpCodes.Ldloc ||
               instruction.opcode == OpCodes.Ldloc_S ||
               instruction.opcode == OpCodes.Ldloc_0 ||
               instruction.opcode == OpCodes.Ldloc_1 ||
               instruction.opcode == OpCodes.Ldloc_2 ||
               instruction.opcode == OpCodes.Ldloc_3;
    }

    private static bool LoadsInt32(
        CodeInstruction instruction,
        int value)
    {
        return instruction.opcode == OpCodes.Ldc_I4 &&
               instruction.operand is int operand &&
               operand == value;
    }

    private static void MainAgentInputPostfix(
        MissionMainAgentController __instance)
    {
        RifleControlMissionLogic? rifleLogic =
            __instance.Mission.GetMissionBehavior<RifleControlMissionLogic>();

        if (rifleLogic is not null)
        {
            rifleLogic.HandleMainAgentInput(
                __instance.Input,
                __instance.MissionScreen);
        }
    }

    internal static void ApplyCameraRecoil(
        MissionScreen missionScreen,
        float verticalRadians,
        float horizontalRadians)
    {
        missionScreen.CameraBearing += horizontalRadians;

        float currentElevation =
            (float)(CameraElevationProperty.GetValue(missionScreen) ?? 0f);
        float updatedElevation = MathF.Clamp(
            currentElevation + verticalRadians,
            -1.3659099f,
            MathF.PI * 5f / 14f);

        CameraElevationProperty.SetValue(missionScreen, updatedElevation);
    }
}
