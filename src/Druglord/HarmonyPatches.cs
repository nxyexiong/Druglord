using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
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

    }

    private static void FirearmRelevantSkillPostfix(
        WeaponClass weaponClass,
        ref SkillObject __result)
    {
        if (__result is null &&
            (weaponClass == WeaponClass.Pistol ||
             weaponClass == WeaponClass.Musket))
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
