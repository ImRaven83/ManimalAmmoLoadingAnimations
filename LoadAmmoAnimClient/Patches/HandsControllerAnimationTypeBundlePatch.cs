using System.Reflection;
using EFT;
using HarmonyLib;
using Manimal.LoadAmmoAnim.CustomEFTData;
using SPT.Reflection.Patching;

namespace Manimal.LoadAmmoAnim.Patches
{
    // patches Player.HandsController.method_49, the function that decides which
    // PlayerAnimator.EWeaponAnimationType the held item uses. vanilla only knows
    // pistols, revolvers, knives, etc, and falls through to a default that
    // leaves the player animator in a bad state for us.
    //
    // we force Pistol. one-handed object held in front of the camera is the
    // closest fit for a magazine.
    //
    // NOTE: "Player.HandsController" is an INFERENCE, not a sourced rename. Post-4.1
    // deobfuscation, sibling classes (FirearmController, KnifeController, MedsController,
    // UsableItemController) all sit as nested types under Player with no "Class" suffix,
    // unlike this one's old name "HandsControllerClass". If this doesn't compile or
    // resolve, the base class needs to be found via decompiler and this fixed.
    internal sealed class HandsControllerAnimationTypeBundlePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(Player.HandsController).GetMethod(
                "method_49",
                BindingFlags.Public | BindingFlags.Instance);

        [PatchPrefix]
        private static bool Prefix(
            ref PlayerAnimator.EWeaponAnimationType __result,
            Player.HandsController __instance)
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
