using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;

namespace Druglord;

internal sealed class RifleControlMissionLogic : MissionLogic
{
    private enum WeaponState
    {
        Lowered,
        Raising,
        Ready,
        Firing,
        Reloading
    }

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

    private ShootMissileDelegate? _shootMissile;
    private ActionIndexCache _readyAction;
    private ActionIndexCache _readyContinueAction;
    private ActionIndexCache _releaseAction;
    private ActionIndexCache _reloadAction;
    private WeaponState _weaponState;
    private bool _aimHeld;
    private bool _triggerHeld;
    private bool _shotQueued;
    private bool _outOfAmmoNotified;
    private bool _wasRifleWielded;
    private float _raiseCompletionTime;
    private float _firingCompletionTime;
    private float _nextShotTime;
    private float _reloadCompletionTime;
    private Vec3 _cameraPosition;
    private Vec3 _cameraDirection;
    private MissionScreen? _missionScreen;
    private int _consecutiveShotCount;
    private float _lastShotTime = float.MinValue;

    public RifleControlMissionLogic()
    {
        _weaponState = WeaponState.Lowered;
    }

    public override void OnBehaviorInitialize()
    {
        base.OnBehaviorInitialize();

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
        _readyAction = ActionIndexCache.Create("act_ready_crossbow");
        _readyContinueAction =
            ActionIndexCache.Create("act_ready_continue_crossbow");
        _releaseAction = ActionIndexCache.Create("act_release_crossbow");
        _reloadAction = ActionIndexCache.Create("act_reload_crossbow_light");

        Debug.Print("Druglord: Harmony rifle controller initialized.");
    }

    internal void HandleMainAgentInput(
        IInputContext input,
        MissionScreen missionScreen)
    {
        Agent? mainAgent = Mission.MainAgent;
        if (mainAgent is null ||
            !mainAgent.IsActive() ||
            !TryGetWieldedRifle(mainAgent, out EquipmentIndex rifleSlot))
        {
            OnRifleNoLongerWielded();
            return;
        }

        if (!_wasRifleWielded)
        {
            _wasRifleWielded = true;
            Debug.Print("Druglord: Harmony rifle input hook is active.");
            InformationManager.DisplayMessage(
                new InformationMessage(
                    "Rifle: AUTO | RMB readies weapon."));
        }

        SuppressNativeRifleControls(mainAgent);

        _missionScreen = missionScreen;
        _cameraPosition = missionScreen.CombatCamera.Position;
        _cameraDirection = missionScreen.CombatCamera.Direction;
        _aimHeld =
            input.IsKeyDown(InputKey.RightMouseButton) ||
            input.IsGameKeyDown(10);
        _triggerHeld = IsAttackButtonDown(input);
        bool triggerPressed = IsAttackButtonPressed(input);

        if (Mission.CurrentTime - _lastShotTime >
            RifleSettings.RecoilResetDelay)
        {
            _consecutiveShotCount = 0;
        }

        if (_weaponState == WeaponState.Reloading)
        {
            if (Mission.CurrentTime >= _reloadCompletionTime)
            {
                CompleteReload(mainAgent, rifleSlot);
            }

            return;
        }

        MissionWeapon rifle = mainAgent.Equipment[rifleSlot];
        if (_weaponState != WeaponState.Firing && rifle.Ammo <= 0)
        {
            BeginReload(mainAgent);
            return;
        }

        switch (_weaponState)
        {
        case WeaponState.Lowered:
            if (_aimHeld ||
                triggerPressed ||
                _triggerHeld)
            {
                _shotQueued = triggerPressed || _triggerHeld;
                BeginRaise(mainAgent);
            }
            break;

        case WeaponState.Raising:
            if (triggerPressed || _triggerHeld)
            {
                _shotQueued = true;
            }

            if (!_aimHeld && !_triggerHeld && !_shotQueued)
            {
                LowerRifle(mainAgent);
            }
            else if (IsRaiseComplete(mainAgent))
            {
                EnterReady(mainAgent);
                TryFireQueuedShot(mainAgent, rifleSlot);
            }
            break;

        case WeaponState.Ready:
            MaintainReadyPose(mainAgent);

            if (_triggerHeld &&
                Mission.CurrentTime >= _nextShotTime)
            {
                FireRifle(mainAgent, rifleSlot);
            }
            else if (!_aimHeld && !_triggerHeld)
            {
                LowerRifle(mainAgent);
            }
            break;

        case WeaponState.Firing:
            if (Mission.CurrentTime >= _firingCompletionTime)
            {
                rifle = mainAgent.Equipment[rifleSlot];
                if (rifle.Ammo <= 0)
                {
                    BeginReload(mainAgent);
                }
                else if (_aimHeld || _triggerHeld || _shotQueued)
                {
                    EnterReady(mainAgent);
                    TryFireQueuedShot(mainAgent, rifleSlot);
                }
                else
                {
                    LowerRifle(mainAgent);
                }
            }
            break;
        }
    }

    protected override void OnEndMission()
    {
        if (Mission.MainAgent is { } mainAgent)
        {
            LowerRifle(mainAgent);
        }

        base.OnEndMission();
    }

    private void BeginRaise(Agent agent)
    {
        _weaponState = WeaponState.Raising;
        _raiseCompletionTime =
            Mission.CurrentTime + RifleSettings.RaiseDuration;

        agent.SetActionChannel(
            1,
            _readyAction,
            ignorePriority: true,
            actionSpeed: 1f,
            blendInPeriod: 0.08f,
            blendOutPeriod: 0.08f);

        Debug.Print("Druglord: rifle raising.");
    }

    private bool IsRaiseComplete(Agent agent)
    {
        ActionIndexCache currentAction = agent.GetCurrentAction(1);
        if (currentAction == _readyContinueAction)
        {
            return true;
        }

        if (currentAction == _readyAction &&
            agent.GetCurrentActionProgress(1) >= 0.9f)
        {
            return true;
        }

        return Mission.CurrentTime >= _raiseCompletionTime;
    }

    private void EnterReady(Agent agent)
    {
        _weaponState = WeaponState.Ready;
        agent.SetActionChannel(
            1,
            _readyContinueAction,
            ignorePriority: true,
            actionSpeed: 1f,
            blendInPeriod: 0.04f,
            blendOutPeriod: 0.08f);
    }

    private void MaintainReadyPose(Agent agent)
    {
        ActionIndexCache currentAction = agent.GetCurrentAction(1);
        if (currentAction != _readyAction &&
            currentAction != _readyContinueAction)
        {
            EnterReady(agent);
        }
    }

    private void TryFireQueuedShot(Agent agent, EquipmentIndex rifleSlot)
    {
        bool shouldFire =
            _shotQueued ||
            (_triggerHeld &&
             Mission.CurrentTime >= _nextShotTime);

        if (shouldFire)
        {
            FireRifle(agent, rifleSlot);
        }
    }

    private void FireRifle(Agent agent, EquipmentIndex rifleSlot)
    {
        MissionWeapon rifle = agent.Equipment[rifleSlot];
        if (rifle.Ammo <= 0 || rifle.AmmoWeapon.IsEmpty)
        {
            BeginReload(agent);
            return;
        }

        Vec3 cameraDirection = _cameraDirection;
        cameraDirection.Normalize();
        Vec3 position = agent.GetEyeGlobalPosition();
        Vec3 aimPoint = _cameraPosition + cameraDirection * 1000f;
        Vec3 direction = aimPoint - position;
        direction.Normalize();

        int recoilShotCount = UpdateRecoilShotCount();
        float recoilIntensity = GetRecoilIntensity(recoilShotCount);
        float spreadDegrees = MathF.Lerp(
            RifleSettings.MinimumSpreadDegrees,
            RifleSettings.MaximumSpreadDegrees,
            recoilIntensity);
        float horizontalSpread =
            MBRandom.RandomFloatRanged(-spreadDegrees, spreadDegrees)
                .ToRadians();
        float verticalSpread =
            MBRandom.RandomFloatRanged(-spreadDegrees, spreadDegrees)
                .ToRadians();

        Mat3 orientation = Mat3.Identity;
        orientation.f = direction;
        orientation.u = Vec3.Up;
        orientation.Orthonormalize();
        orientation.RotateAboutUp(horizontalSpread);
        orientation.RotateAboutSide(verticalSpread);
        direction = orientation.f;
        position += direction * 0.7f;

        float missileSpeed = rifle.GetModifiedMissileSpeedForCurrentUsage();
        Vec3 velocity = direction * missileSpeed;

        (_shootMissile ?? throw new InvalidOperationException(
            "Druglord rifle shooting was not initialized."))(
            Mission,
            agent,
            rifleSlot,
            position,
            velocity,
            orientation,
            false,
            true,
            -1);

        short remainingAmmo = (short)(rifle.Ammo - 1);
        agent.Equipment.SetConsumedAmmoOfSlot(rifleSlot, 1);
        agent.SetWeaponAmmoAsClient(
            rifleSlot,
            EquipmentIndex.None,
            remainingAmmo);

        agent.SetActionChannel(
            1,
            _releaseAction,
            ignorePriority: true,
            actionSpeed: 2.5f,
            blendInPeriod: 0.02f,
            blendOutPeriod: 0.08f);

        _weaponState = WeaponState.Firing;
        _shotQueued = false;
        _firingCompletionTime =
            Mission.CurrentTime + RifleSettings.RecoilDuration;
        _nextShotTime =
            Mission.CurrentTime + RifleSettings.AutomaticShotInterval;
        _outOfAmmoNotified = false;
        ApplyCameraRecoil(recoilIntensity);

        Debug.Print(
            $"Druglord: rifle fired; magazine {remainingAmmo}/{RifleSettings.MagazineSize}; recoil {recoilShotCount}/{RifleSettings.PeakRecoilShotCount}.");
    }

    private void BeginReload(Agent agent)
    {
        if (_weaponState == WeaponState.Reloading)
        {
            return;
        }

        if (!TryFindCartridgeSlot(agent, out _))
        {
            if (!_outOfAmmoNotified)
            {
                _outOfAmmoNotified = true;
                InformationManager.DisplayMessage(
                    new InformationMessage("Rifle: out of cartridges."));
            }

            LowerRifle(agent);
            return;
        }

        _weaponState = WeaponState.Reloading;
        _shotQueued = false;
        _reloadCompletionTime =
            Mission.CurrentTime + RifleSettings.ReloadDuration;

        agent.SetActionChannel(
            1,
            _reloadAction,
            ignorePriority: true,
            actionSpeed: 1f,
            blendInPeriod: 0.05f,
            blendOutPeriod: 0.15f);

        InformationManager.DisplayMessage(new InformationMessage("Rifle reloading..."));
        Debug.Print("Druglord: rifle reload started.");
    }

    private void CompleteReload(Agent agent, EquipmentIndex rifleSlot)
    {
        if (!TryFindCartridgeSlot(agent, out EquipmentIndex cartridgeSlot))
        {
            _outOfAmmoNotified = true;
            InformationManager.DisplayMessage(
                new InformationMessage("Rifle: out of cartridges."));
            LowerRifle(agent);
            return;
        }

        MissionWeapon reserve = agent.Equipment[cartridgeSlot];
        short roundsToLoad = (short)Math.Min(
            RifleSettings.MagazineSize,
            reserve.Amount);
        MissionWeapon loadedRounds = reserve.Consume(roundsToLoad);

        MissionWeapon rifle = agent.Equipment[rifleSlot];
        rifle.ReloadAmmo(loadedRounds, rifle.ReloadPhaseCount);

        agent.EquipWeaponWithNewEntity(rifleSlot, ref rifle);
        agent.EquipWeaponWithNewEntity(cartridgeSlot, ref reserve);
        agent.TryToWieldWeaponInSlot(
            rifleSlot,
            Agent.WeaponWieldActionType.InstantAfterPickUp,
            false);

        _weaponState = WeaponState.Lowered;
        _nextShotTime =
            Mission.CurrentTime + RifleSettings.AutomaticShotInterval;
        _outOfAmmoNotified = false;

        InformationManager.DisplayMessage(
            new InformationMessage(
                $"Rifle reloaded: {roundsToLoad}/{RifleSettings.MagazineSize}"));
        Debug.Print($"Druglord: rifle reloaded with {roundsToLoad} rounds.");

        if (_aimHeld || _triggerHeld)
        {
            _shotQueued = _triggerHeld;
            BeginRaise(agent);
        }
    }

    private void LowerRifle(Agent agent)
    {
        _weaponState = WeaponState.Lowered;
        _shotQueued = false;
        agent.SetActionChannel(
            1,
            ActionIndexCache.act_none,
            ignorePriority: true,
            blendInPeriod: 0.08f,
            blendOutPeriodToNoAnim: 0.2f,
            blendOutPeriod: 0.1f);
    }

    private void SuppressNativeRifleControls(Agent agent)
    {
        agent.MovementFlags &=
            ~(Agent.MovementControlFlag.AttackMask |
              Agent.MovementControlFlag.DefendMask);
        agent.EventControlFlags &=
            ~Agent.EventControlFlag.ToggleAlternativeWeapon;
    }

    private void OnRifleNoLongerWielded()
    {
        _weaponState = WeaponState.Lowered;
        _aimHeld = false;
        _triggerHeld = false;
        _shotQueued = false;
        _wasRifleWielded = false;
        _consecutiveShotCount = 0;
        _missionScreen = null;
    }

    private static bool TryGetWieldedRifle(
        Agent agent,
        out EquipmentIndex rifleSlot)
    {
        rifleSlot = agent.GetPrimaryWieldedItemIndex();
        if (rifleSlot == EquipmentIndex.None)
        {
            return false;
        }

        MissionWeapon weapon = agent.Equipment[rifleSlot];
        return !weapon.IsEmpty &&
               weapon.Item.StringId == FirearmItemRegistry.RifleId;
    }

    private static bool TryFindCartridgeSlot(
        Agent agent,
        out EquipmentIndex cartridgeSlot)
    {
        for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
             slot < EquipmentIndex.NumAllWeaponSlots;
             slot++)
        {
            MissionWeapon weapon = agent.Equipment[slot];
            if (!weapon.IsEmpty &&
                weapon.Item.StringId == FirearmItemRegistry.CartridgeId &&
                weapon.Amount > 0)
            {
                cartridgeSlot = slot;
                return true;
            }
        }

        cartridgeSlot = EquipmentIndex.None;
        return false;
    }

    private static bool IsAttackButtonDown(IInputContext input)
    {
        return input.IsKeyDown(InputKey.LeftMouseButton) ||
               input.IsGameKeyDown(9);
    }

    private static bool IsAttackButtonPressed(IInputContext input)
    {
        return input.IsKeyPressed(InputKey.LeftMouseButton) ||
               input.IsGameKeyPressed(9);
    }

    private int UpdateRecoilShotCount()
    {
        if (Mission.CurrentTime - _lastShotTime >
            RifleSettings.RecoilResetDelay)
        {
            _consecutiveShotCount = 0;
        }

        _lastShotTime = Mission.CurrentTime;
        _consecutiveShotCount = Math.Min(
            _consecutiveShotCount + 1,
            RifleSettings.PeakRecoilShotCount);
        return _consecutiveShotCount;
    }

    private static float GetRecoilIntensity(int shotCount)
    {
        return (float)(shotCount - 1) /
               (RifleSettings.PeakRecoilShotCount - 1);
    }

    private void ApplyCameraRecoil(float recoilIntensity)
    {
        if (_missionScreen is null)
        {
            return;
        }

        float verticalKick = MathF.Lerp(
            RifleSettings.MinimumVerticalKickDegrees,
            RifleSettings.MaximumVerticalKickDegrees,
            recoilIntensity).ToRadians();
        float horizontalKick =
            MBRandom.RandomFloatRanged(
                -RifleSettings.MaximumHorizontalKickDegrees,
                RifleSettings.MaximumHorizontalKickDegrees) *
            recoilIntensity;

        HarmonyPatches.ApplyCameraRecoil(
            _missionScreen,
            verticalKick,
            horizontalKick.ToRadians());
    }
}
