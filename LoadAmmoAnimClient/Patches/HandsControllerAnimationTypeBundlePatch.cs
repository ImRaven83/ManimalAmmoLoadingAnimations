using System.Reflection;
using EFT;
using HarmonyLib;
using Manimal.LoadAmmoAnim.CustomEFTData;
using SPT.Reflection.Patching;

namespace Manimal.LoadAmmoAnim.Patches
{
    // patches Player.ItemHandsController.method_49, the function that decides which
    // PlayerAnimator.EWeaponAnimationType the held item uses. vanilla only knows
    // pistols, revolvers, knives, etc, and falls through to a default that
    // leaves the player animator in a bad state for us.
    //
    // we force Pistol. one-handed object held in front of the camera is the
    // closest fit for a magazine.
    //
    // NOTE: "Player.ItemHandsController" is an INFERENCE, not a sourced rename.
    // "Player.HandsController" (tried first) doesn't exist per CS0426. ItemHandsController
    // is the confirmed-working generic factory class used elsewhere in this codebase
    // (Player.ItemHandsController.smethod_1<T>), a plausible base class for the
    // per-item hands controller family. If this doesn't resolve, the base class
    // needs to be found via decompiler and this fixed.
    internal sealed class HandsControllerAnimationTypeBundlePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(Player.ItemHandsController).GetMethod(
                "method_49",
                BindingFlags.Public | BindingFlags.Instance);

        [PatchPrefix]
        private static bool Prefix(
            ref PlayerAnimator.EWeaponAnimationType __result,
            Player.ItemHandsController __instance)
        {
            if (__instance.ItemInHands is LoadAmmoBundleItem)
            {
                __result = PlayerAnimator.EWeaponAnimationType.Pistol;
                return false;
            }
            return true;
        }
    }
}
