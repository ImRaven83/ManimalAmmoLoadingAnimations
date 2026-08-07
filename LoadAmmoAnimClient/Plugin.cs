using BepInEx;
using BepInEx.Logging;
using Manimal.LoadAmmoAnim.Patches;

namespace Manimal.LoadAmmoAnim
{
    [BepInPlugin(BuildInfo.ModGuid, "Manimal-LoadAmmoAnim", BuildInfo.Version)]
    // soft-dep on CLA so we load AFTER it. our plugin's GUID sorts alphabetically
    // before CLA's, which means without this hint BepInEx loads us first — and our
    // Awake-time IsInstalled check would then miss CLA in the plugin registry,
    // skipping the compat patches and breaking chained-mag loading.
    [BepInDependency("com.ozen.continuousloadammo", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;

        private void Awake()
        {
            LogSource = Logger;
            LogSource.LogInfo("LoadAmmoAnim loaded!");

            // Class1204 hooks — detect mag-loading sessions + extend the first-bullet
            // delay to fit the bundle's draw clip. driver-side, unrelated to controller dispatch.
            new LoadAmmoAnimDetectPatch().Enable();
            new Class1204DrawDelayPatch().Enable();
            new RaidStartBundleWarmPatch().Enable();

            // dispatch patches. route LoadAmmoBundleItem through LoadAmmoBundleController
            // instead of the engine's default usable-item handlers. mirror of HackerMod's
            // four-patch shim for custom items.
            new SetInHandsBundlePatch().Enable();
            new ClientUsableBundleSmethod11Patch().Enable();
            new HandsControllerAnimationTypeBundlePatch().Enable();
            new UsableBundleInterfaceDispatchPatch().Enable();

            // defensive PWA guards — there's still a brief window during controller
            // teardown/respawn where WeaponRootAnim can be null/destroyed before the
            // new firearm controllers smethod_8 rebinds. these silence the NRE storm
            // for those frames.
            new ProcessEffectorsNullGuardPatch().Enable();
            new VisualPassNullGuardPatch().Enable();

            if (ContinuousLoadAmmoCompat.IsInstalled)
                ContinuousLoadAmmoCompat.EnablePatches();
        }
    }
}
