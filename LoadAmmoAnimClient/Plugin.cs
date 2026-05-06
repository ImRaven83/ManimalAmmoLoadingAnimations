using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Manimal.LoadAmmoAnim.Patches;

namespace Manimal.LoadAmmoAnim
{
    [BepInPlugin(BuildInfo.ModGuid, "Manimal-LoadAmmoAnim", BuildInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;

        // ties anim playback rate to the per-bullet load time.
        // animSpeed = AnimSpeedProportion / loadOneAmmoSpeed
        public static ConfigEntry<float> AnimSpeedProportion;

        // small wait before the first bullet loads, so the mag draw anim has time to play.
        public static ConfigEntry<float> DrawDurationSeconds;

        private void Awake()
        {
            LogSource = Logger;
            LogSource.LogInfo("LoadAmmoAnim loaded!");

            AnimSpeedProportion = Config.Bind(
                "Animation",
                "AnimSpeedProportion",
                1.0f,
                new ConfigDescription(
                    "how fast the loading anim plays vs the actual mag loading speed.\n" +
                    "animSpeed = this value / loadOneAmmoSpeed (seconds per round).\n" +
                    "raise it if the anim feels too slow, lower it if its too fast.",
                    new AcceptableValueRange<float>(0.1f, 5.0f)));

            DrawDurationSeconds = Config.Bind(
                "Animation",
                "DrawDurationSeconds",
                0.867f,
                new ConfigDescription(
                    "how long to wait after the mag draw starts before the first round actually loads.\n" +
                    "set this to roughly match the draw phase length in your anim clip.",
                    new AcceptableValueRange<float>(0f, 5.0f)));

            new LoadAmmoAnimDetectPatch().Enable();
            new LoadAmmoMedsMethod5Patch().Enable();
            new LoadAmmoMedsCancelPatch().Enable();
            new LoadAmmoMedsStartPatch().Enable();
            new RaidStartBundleWarmPatch().Enable();
            new Class1204DrawDelayPatch().Enable();
            new ApplyPositionNullGuardPatch().Enable();
            new ApplyComplexRotationNullGuardPatch().Enable();

            if (ContinuousLoadAmmoCompat.IsInstalled)
                ContinuousLoadAmmoCompat.EnablePatches();
        }
    }
}
