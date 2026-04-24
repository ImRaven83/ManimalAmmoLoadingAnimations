using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;

namespace Manimal.LoadAmmoAnim.Patches
{
    // compat shim for ContinuousLoadAmmo (com.ozen.continuousloadammo).
    // CLA listens for OnHandsControllerChanged and cancels its loading session whenever
    // the hands controller swaps to anything other than empty hands. our Proceed call
    // swaps to the meds controller, which trips that listener, and CLA bails out after
    // bullet 1. these patches keep CLA from killing our session.
    internal static class ContinuousLoadAmmoCompat
    {
        private const string ClaGuid = "com.ozen.continuousloadammo";

        public static bool IsInstalled =>
            Chainloader.PluginInfos.ContainsKey(ClaGuid);

        public static void EnablePatches()
        {
            Plugin.LogSource.LogInfo("[LoadAmmoAnim] ContinuousLoadAmmo detected, enabling compat patches.");
            new ClaSetEmptyHandsPatch().Enable();
            new ClaStopOnHandsChangePatch().Enable();
            new ClaTrySetLastEquippedWeaponPatch().Enable();
        }
    }

    // CLA calls SetEmptyHands(null) to put the gun away before its loading anim starts.
    // while we are actively loading we just block it, so it cant race our Proceed.
    // if loading finished but our anim is still winding down, we tear our anim down
    // first, then let CLA's call go through.
    public class ClaSetEmptyHandsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player), "SetEmptyHands",
                new[] { typeof(Callback<GInterface198>) });

        [PatchPrefix]
        public static bool Prefix(Player __instance)
        {
            if (!__instance.IsYourPlayer) return true;

            if (LoadAmmoAnimState.IsLoading) return false;

            if (LoadAmmoAnimState.IsOurAnimation)
            {
                var controller = LoadAmmoAnimState.ActiveController;
                LoadAmmoAnimState.StopLoop();
                if (controller != null)
                    LoadAmmoAnimController.StopAnimationInstantly(controller);
            }

            return true;
        }
    }

    // suppresses CLA's StopLoadingOnHandsChange while our anim is active. without
    // this, it fires every time we Proceed into the meds controller, and kills the
    // session after bullet 1.
    public class ClaStopOnHandsChangePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // CLA lives in a different assembly, so we look it up at runtime instead
            // of taking a hard reference to it.
            var claType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.FullName == "ContinuousLoadAmmo.Controllers.LoadAmmoController");

            if (claType == null)
            {
                Plugin.LogSource.LogWarning("[LoadAmmoAnim] CLA compat: could not find LoadAmmoController type.");
                return null;
            }

            var method = claType.GetMethod(
                "StopLoadingOnHandsChange",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (method == null)
                Plugin.LogSource.LogWarning("[LoadAmmoAnim] CLA compat: could not find StopLoadingOnHandsChange method.");

            return method;
        }

        [PatchPrefix]
        public static bool Prefix()
        {
            return !LoadAmmoAnimState.IsOurAnimation;
        }
    }

    // CLA calls TrySetLastEquippedWeapon when its own session is wrapping up. thats
    // our cue to tear our anim down too, so we dont fight CLA for the weapon equip.
    public class ClaTrySetLastEquippedWeaponPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player), "TrySetLastEquippedWeapon",
                new[] { typeof(bool), typeof(Callback) });

        [PatchPrefix]
        public static void Prefix(Player __instance)
        {
            if (!__instance.IsYourPlayer || !LoadAmmoAnimState.IsOurAnimation)
                return;

            var controller = LoadAmmoAnimState.ActiveController;
            LoadAmmoAnimState.StopLoop();
            if (controller != null)
                LoadAmmoAnimController.StopAnimationInstantly(controller);
        }
    }
}
