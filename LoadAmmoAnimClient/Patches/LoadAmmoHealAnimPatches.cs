using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using UnityEngine;

namespace Manimal.LoadAmmoAnim.Patches
{
    // shared state for all the patches in this file. _count tracks how many Class1204
    // sessions are alive right now, so we know when to start and stop the visible anim.
    internal static class LoadAmmoAnimState
    {
        private static int _count;

        public static bool IsLoading => _count > 0;
        public static bool IsOurAnimation;
        public static Player.MedsController.ObservedMedsControllerClass ActiveController;
        public static Player ActivePlayer;
        public static Coroutine LoopCoroutine;

        // seconds per round at the player's current mag drills level. we re-read it
        // from Class1204.Float_0 every session, so skill ups take effect right away.
        public static float LoadOneAmmoSpeed = 1f;

        // gets armed at the start of each session, and consumed by Class1204DrawDelayPatch
        // on the first bullet only. that way we only stretch the initial delay, not every one.
        public static bool DrawPhasePending;

        public static void OnLoadingStarted()
        {
            // only start a new anim on the 0 to 1 edge. anything else is just an extra
            // session piling on top of one we are already animating for.
            if (_count++ == 0 && !IsOurAnimation)
            {
                DrawPhasePending = true;

                // wait a frame so Class1204.Start can finish its synchronous setup first.
                // Proceeding inline races the inventory state and gets weird.
                var player = Singleton<GameWorld>.Instance?.MainPlayer;
                player?.StartCoroutine(LoadAmmoAnimController.StartNextFrame());
            }
        }

        public static void OnLoadingEnded()
        {
            if (--_count < 0)
                _count = 0;
        }

        // template id of the mag we are loading. captured in Class1204.Start, so
        // ApplyMeshSelection knows which mesh to switch on.
        public static string CurrentMagTemplateId = null;

        // the live mag item. AnimLoop checks this every frame to know when its full
        // and we should stop.
        public static MagazineItemClass CurrentMag = null;

        // hard reset for _count, used when the stall logic decides things have gone off the rails.
        internal static void ForceResetLoading() { _count = 0; }

        public static void StopLoop()
        {
            IsOurAnimation = false;
            if (ActivePlayer != null && LoopCoroutine != null)
                ActivePlayer.StopCoroutine(LoopCoroutine);
            LoopCoroutine = null;
            ActiveController = null;
            ActivePlayer = null;
        }
    }

    // runs the meds controller anim while the player is loading ammo. no healing
    // happens, no resources get used. we are just borrowing the ifak use animation
    // as a vehicle for the mag-loading prefab.
    internal static class LoadAmmoAnimController
    {
        // roughly the real ifak quick-use anim length. once elapsed passes this,
        // AnimLoop calls method_6 to cycle to the next variant, so the anim doesnt
        // snap back to idle mid-reload.
        private const float AnimDuration = 3.5f;
        // the hidden ifak the server mod registers. running Proceed against this
        // gives us the use anim and the mag bundle prefab, without touching real items.
        private const string IfakTemplateId = "69d69c70ed183ba9c882f7f7";

        // wait one frame, make sure the bundle is ready, then start the anim.
        // the frame of latency lets Class1204.Start finish its synchronous setup
        // first. Proceeding inline races the inventory state and gets weird.
        public static IEnumerator StartNextFrame()
        {
            yield return null;
            if (!LoadAmmoAnimState.IsLoading) yield break;

            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null) yield break;

            var medkit = GetOrCreateMedkit(player);
            if (medkit == null)
            {
                Plugin.LogSource.LogWarning("[LoadAmmoAnim] could not obtain medkit item, skipping animation.");
                yield break;
            }

            // the raid-start warm-up hasnt finished yet, so retain the bundle inline
            // here. the first reload of the raid sometimes lands on this path. every
            // reload after is a no-op, since _bundleWarmed sticks around.
            if (!_bundleWarmed)
            {
                var usePrefab = medkit.UsePrefab;
                if (usePrefab != null)
                {
                    var poolManager = Singleton<PoolManagerClass>.Instance;
                    if (poolManager?.EasyAssets != null)
                    {
                        var retainTask = GClass1857.RetainSeparateTask(
                            poolManager.EasyAssets, new[] { usePrefab.path });

                        while (!retainTask.IsCompleted) yield return null;

                        if (!retainTask.IsFaulted && retainTask.Result?.LoadingJob != null)
                        {
                            var loadTask = retainTask.Result.LoadingJob;
                            while (!loadTask.IsCompleted) yield return null;
                        }

                        _bundleWarmed = true;
                    }
                }
            }

            TryStart(player, medkit);
        }

        // bundle is loaded, fire Proceed and spin up the helper coroutines.
        private static void TryStart(Player player, MedKitItemClass medkit)
        {
            LoadAmmoAnimState.IsOurAnimation = true;
            LoadAmmoAnimState.ActivePlayer = player;

            player.Proceed(medkit, default(GStruct382<EBodyPart>), NoOpCallback, 0, false);

            // safety net. if method_5 never fires because the controller never gets
            // set up, this clears IsOurAnimation so we dont get stuck.
            player.StartCoroutine(WatchForStuckStart());
        }

        // waits until the controller comes online, or until loading ends. if loading
        // ends first and CLA isnt installed, clear IsOurAnimation so the next reload
        // starts fresh. with CLA the count can dip to 0 between bullets, so we just
        // keep waiting and let AnimLoop's idle timer make the call.
        private static IEnumerator WatchForStuckStart()
        {
            while (LoadAmmoAnimState.IsOurAnimation && LoadAmmoAnimState.ActiveController == null)
            {
                yield return null;

                if (!LoadAmmoAnimState.IsLoading)
                {
                    if (ContinuousLoadAmmoCompat.IsInstalled)
                        continue;

                    LoadAmmoAnimState.IsOurAnimation = false;
                    yield break;
                }
            }
        }

        private static void NoOpCallback(Result<GInterface203> _) { }

        // builds a throwaway ifak that only lives in memory. it never goes into any
        // inventory, and never heals or gets consumed. we just need a real Item with
        // a valid MedKitComponent so Player.Proceed will accept it.
        private static MedKitItemClass GetOrCreateMedkit(Player _player)
        {
            try
            {
                var factory = Singleton<ItemFactoryClass>.Instance;
                if (factory != null)
                {
                    var virtualItem = factory.CreateItem(
                        MongoID.Generate(false).ToString(),
                        IfakTemplateId,
                        null) as MedKitItemClass;

                    if (virtualItem?.MedKitComponent != null)
                        return virtualItem;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[LoadAmmoAnim] could not create virtual IFAK: {ex.Message}");
            }

            return null;
        }

        // set to true once WarmBundleAsync has retained the bundle for this raid.
        private static bool _bundleWarmed;

        // the renderer we enabled this session. we disable it again in StopAnimationInstantly
        // so the bundle resets to all-off before EFT returns the prefab to its pool.
        // all meshes start disabled in the bundle, so we only ever need to enable one.
        private static MeshRenderer _activeRenderer;

        // preloads the bundle at raid start, so its already in memory by the time the
        // player does their first reload.
        public static IEnumerator WarmBundleAsync()
        {
            _bundleWarmed = false;
            _activeRenderer = null;

            string bundlePath = null;
            try
            {
                var factory = Singleton<ItemFactoryClass>.Instance;
                if (factory != null)
                {
                    var tmp = factory.CreateItem(
                        MongoID.Generate(false).ToString(),
                        IfakTemplateId, null) as MedKitItemClass;
                    bundlePath = tmp?.UsePrefab?.path;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(bundlePath)) yield break;

            var poolManager = Singleton<PoolManagerClass>.Instance;
            if (poolManager?.EasyAssets == null) yield break;

            var retainTask = GClass1857.RetainSeparateTask(
                poolManager.EasyAssets, new[] { bundlePath });
            while (!retainTask.IsCompleted) yield return null;

            if (!retainTask.IsFaulted && retainTask.Result?.LoadingJob != null)
            {
                var loadTask = retainTask.Result.LoadingJob;
                while (!loadTask.IsCompleted) yield return null;
            }

            _bundleWarmed = true;
        }

        public static void StartLoop(Player.MedsController.ObservedMedsControllerClass controller)
        {
            // stop any leftover coroutine from a previous session before we start a new one.
            if (LoadAmmoAnimState.ActivePlayer != null && LoadAmmoAnimState.LoopCoroutine != null)
                LoadAmmoAnimState.ActivePlayer.StopCoroutine(LoadAmmoAnimState.LoopCoroutine);

            // mesh selection only needs to happen once per session, on the first method_5 call.
            bool isFirstCall = LoadAmmoAnimState.ActiveController == null;
            LoadAmmoAnimState.ActiveController = controller;
            if (isFirstCall)
                ApplyMeshSelection();

            // tie playback rate to the player's mag drills speed, so faster reloads play faster.
            var animator = controller.MedsController_0?.FirearmsAnimator;
            if (animator != null && LoadAmmoAnimState.LoadOneAmmoSpeed > 0f)
            {
                float animSpeed = Plugin.AnimSpeedProportion.Value / LoadAmmoAnimState.LoadOneAmmoSpeed;
                animator.SetAnimationSpeed(animSpeed);
            }

            LoadAmmoAnimState.LoopCoroutine =
                LoadAmmoAnimState.ActivePlayer.StartCoroutine(AnimLoop(controller));
        }

        // finds the target renderer and enables it. all other meshes are already disabled
        // in the bundle, so we dont have to touch them.
       
        private static void ApplyMeshSelection()
        {
            string targetMesh = MagAnimLookup.GetMeshName(LoadAmmoAnimState.CurrentMagTemplateId);

            var prefabRoot = LoadAmmoAnimState.ActiveController?.MedsController_0?.ControllerGameObject;
            if (prefabRoot == null)
            {
                Plugin.LogSource.LogWarning("[LoadAmmoAnim] ApplyMeshSelection: ControllerGameObject is null. is the bundle loaded?");
                return;
            }

            foreach (var mr in prefabRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.gameObject.name != targetMesh) continue;
                mr.enabled = true;
                _activeRenderer = mr;
                return;
            }

            Plugin.LogSource.LogWarning($"[LoadAmmoAnim] couldn't find mesh '{targetMesh}' under ControllerGameObject.");
        }

        // disables the active renderer so the bundle is clean for next time (all meshes
        // back to disabled, same as they ship in the bundle), then calls method_9 to
        // trigger the put-away transition and re-equip the weapon via OnOutUseEvent.
        public static void StopAnimationInstantly(Player.MedsController.ObservedMedsControllerClass controller)
        {
            if (_activeRenderer != null)
            {
                _activeRenderer.enabled = false;
                _activeRenderer = null;
            }
            if (controller == null) return;
            controller.method_9();
        }

        // with CLA installed _count can dip to 0 for a tick between bullets, so we need
        // a small grace window before deciding the session is actually over.
        private const float ClaStopTimeoutSeconds = 0.3f;

        private static IEnumerator AnimLoop(Player.MedsController.ObservedMedsControllerClass controller)
        {
            float elapsed = 0f;
            bool claInstalled = ContinuousLoadAmmoCompat.IsInstalled;
            float idleSince = -1f;

            // snapshot the mag this session was started for. once the next mag's
            // Class1204.Start fires, CurrentMag gets overwritten, so we cant trust it
            // inside the loop body.
            MagazineItemClass sessionMag = LoadAmmoAnimState.CurrentMag;

            while (LoadAmmoAnimState.IsOurAnimation)
            {
                yield return null;

                if (!LoadAmmoAnimState.IsOurAnimation || controller == null)
                    break;

                if (!LoadAmmoAnimState.IsLoading)
                {
                    if (!claInstalled)
                    {
                        // no CLA, stop right away.
                        var player = LoadAmmoAnimState.ActivePlayer;
                        LoadAmmoAnimState.IsOurAnimation = false;
                        StopAnimationInstantly(controller);
                        if (player != null && !player.IsInventoryOpened)
                            player.StartCoroutine(RestoreWeaponAfterDelay(player));
                        break;
                    }

                    if (idleSince < 0f)
                        idleSince = Time.realtimeSinceStartup;

                    if (Time.realtimeSinceStartup - idleSince >= ClaStopTimeoutSeconds)
                    {
                        // grace window is up, the session is really over now.
                        var player = LoadAmmoAnimState.ActivePlayer;
                        LoadAmmoAnimState.IsOurAnimation = false;
                        StopAnimationInstantly(controller);
                        if (player != null && !player.IsInventoryOpened)
                            player.StartCoroutine(RestoreWeaponAfterDelay(player));
                        break;
                    }

                    continue;
                }

                idleSince = -1f;

                // mag we were loading hit max, time to wrap up.
                if (sessionMag != null && sessionMag.Count >= sessionMag.MaxCount)
                {
                    var player = LoadAmmoAnimState.ActivePlayer;
                    LoadAmmoAnimState.IsOurAnimation = false;
                    StopAnimationInstantly(controller);

                    // if Class1204.Start already fired for the next mag, CurrentMag is
                    // pointing at the new instance. chain straight into a new anim,
                    // instead of resetting and waiting for OnLoadingStarted to fire.
                    bool nextMagAlreadyLoading = LoadAmmoAnimState.CurrentMag != null
                                                 && LoadAmmoAnimState.CurrentMag != sessionMag;

                    if (nextMagAlreadyLoading && player != null)
                    {
                        // _count already counts the new session, so just start the next anim.
                        player.StartCoroutine(LoadAmmoAnimController.StartNextFrame());
                    }
                    else
                    {
                        // nothing queued. reset so the next OnLoadingStarted triggers
                        // normally, and re-equip the weapon if the inventory isnt open.
                        LoadAmmoAnimState.ForceResetLoading();
                        if (player != null && !player.IsInventoryOpened)
                            player.StartCoroutine(RestoreWeaponAfterDelay(player));
                    }

                    break;
                }

                elapsed += Time.deltaTime;
                if (elapsed >= AnimDuration)
                {
                    // anim played all the way through. cycle to the next variant so the
                    // ifak use anim keeps going, instead of snapping back to idle.
                    elapsed = 0f;
                    try { controller.method_6(); } catch { }
                    controller.MedsController_0?.FirearmsAnimator?.SetActiveParam(true, false);
                }
            }

            LoadAmmoAnimState.LoopCoroutine = null;
            LoadAmmoAnimState.ActiveController = null;
        }

        // small delay, then re-equip the last weapon. waiting a beat lets the meds
        // put-away transition finish first. without it the equip races the transition
        // and looks janky.
        private static System.Collections.IEnumerator RestoreWeaponAfterDelay(Player player)
        {
            yield return new UnityEngine.WaitForSeconds(0.1f);
            if (player != null && player.HandsIsEmpty)
                player.TrySetLastEquippedWeapon(true, null);
        }
    }

    // catches Class1204.Start, so we can grab the mag's per-bullet speed and template id
    // before the loading session actually starts.
    public class LoadAmmoAnimDetectPatch : ModulePatch
    {
        private static FieldInfo _float0Field;
        private static FieldInfo _magazineField;

        protected override MethodBase GetTargetMethod()
        {
            var class1204 = AccessTools.Inner(typeof(Player.PlayerInventoryController), "Class1204");
            return AccessTools.Method(class1204, "Start");
        }

        [PatchPrefix]
        private static void Prefix(object __instance)
        {
            var type = __instance.GetType();

            // Float_0 is loadOneAmmoSpeed, which is seconds per round at the player's
            // current skill level.
            if (_float0Field == null)
                _float0Field = type.GetField("Float_0",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (_float0Field != null)
            {
                float speed = (float)_float0Field.GetValue(__instance);
                if (speed > 0f)
                    LoadAmmoAnimState.LoadOneAmmoSpeed = speed;
            }

            // bsg's obfuscator names this field after its type, so the field name
            // really is "MagazineItemClass".
            if (_magazineField == null)
                _magazineField = type.GetField("MagazineItemClass",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            LoadAmmoAnimState.CurrentMagTemplateId = null;
            LoadAmmoAnimState.CurrentMag = null;
            if (_magazineField != null)
            {
                var mag = _magazineField.GetValue(__instance) as MagazineItemClass;
                if (mag != null)
                {
                    LoadAmmoAnimState.CurrentMagTemplateId = mag.TemplateId;
                    LoadAmmoAnimState.CurrentMag = mag;
                }
            }

            LoadAmmoAnimState.OnLoadingStarted();
        }

        [PatchPostfix]
        private static void Postfix(Task<IResult> __result)
        {
            // Class1204.Start returns a Task that resolves when the session ends.
            // hook the continuation so _count gets decremented no matter how it ends.
            __result?.ContinueWith(_ => LoadAmmoAnimState.OnLoadingEnded());
        }
    }

    // when the meds controller would normally do the heal effect, we skip it entirely
    // and start our anim loop instead. skipping DoMedEffect means no healing happens,
    // and no resources get used.
    public class LoadAmmoMedsMethod5Patch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(Player.MedsController.ObservedMedsControllerClass).GetMethod("method_5");

        [PatchPrefix]
        public static bool Prefix(Player.MedsController.ObservedMedsControllerClass __instance)
        {
            if (!LoadAmmoAnimState.IsOurAnimation)
                return true;

            // loading already finished by the time method_5 fired. without CLA we bail
            // out cleanly. with CLA we let AnimLoop's idle timer make the call, since
            // _count can dip to 0 between bullets, and we dont want to kill it early.
            if (!LoadAmmoAnimState.IsLoading)
            {
                if (!ContinuousLoadAmmoCompat.IsInstalled)
                {
                    LoadAmmoAnimState.IsOurAnimation = false;
                    LoadAmmoAnimController.StopAnimationInstantly(__instance);
                    return false;
                }
            }

            // method_6 just cycles the anim variant, no side effects.
            __instance.method_6();
            LoadAmmoAnimController.StartLoop(__instance);
            return false;
        }
    }

    // player hits the cancel-use key mid-anim. flip IsOurAnimation off so AnimLoop
    // tears down on the next frame.
    public class LoadAmmoMedsCancelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(GClass3010).GetMethod(nameof(GClass3010.CancelApplyingItem));

        [PatchPrefix]
        public static void Prefix(Player ___Player)
        {
            if (___Player?.IsYourPlayer == true && LoadAmmoAnimState.IsOurAnimation)
                LoadAmmoAnimState.IsOurAnimation = false;
        }
    }

    // ObservedMedsControllerClass.Start does `this.Item.Owner.RemoveItemEvent += method_2`,
    // but our virtual ifak has no Owner, so that line NREs. swap the event-add call
    // for a helper that null-checks Owner first.
    public class LoadAmmoMedsStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(Player.MedsController.ObservedMedsControllerClass),
                "Start",
                new[] { typeof(GStruct382<EBodyPart>), typeof(float), typeof(Action) });

        [PatchTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var addRemoveEventMethod = typeof(IItemOwner).GetEvent("RemoveItemEvent")?.GetAddMethod();
            var helperMethod = typeof(LoadAmmoMedsStartPatch)
                .GetMethod(nameof(SubscribeIfOwnerNotNull), BindingFlags.Static | BindingFlags.NonPublic);

            if (addRemoveEventMethod == null || helperMethod == null)
            {
                Plugin.LogSource.LogWarning("[LoadAmmoAnim] StartPatch transpiler: could not resolve methods, patch skipped.");
                return codes;
            }

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(addRemoveEventMethod))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, helperMethod);
                    break;
                }
            }

            return codes;
        }

        private static void SubscribeIfOwnerNotNull(IItemOwner owner, Action<GEventArgs3> handler)
        {
            if (owner != null)
                owner.RemoveItemEvent += handler;
        }
    }

    // method_5 on Class1204 is the per-bullet wait. on the first bullet of a session
    // we extend it by DrawDurationSeconds, so the ifak draw anim has time to finish
    // before a round actually loads. every bullet after that gets the normal wait.
    public class Class1204DrawDelayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var class1204 = AccessTools.Inner(typeof(Player.PlayerInventoryController), "Class1204");
            return AccessTools.Method(class1204, "method_5");
        }

        [PatchPrefix]
        public static bool Prefix(object __instance, ref Task __result)
        {
            if (!LoadAmmoAnimState.DrawPhasePending)
                return true;

            LoadAmmoAnimState.DrawPhasePending = false;

            // Float_1 == Float_0 on the first bullet of a session, so LoadOneAmmoSpeed
            // is a fine stand-in here. drawMs is the player-tuned draw delay.
            int normalMs = Mathf.CeilToInt(LoadAmmoAnimState.LoadOneAmmoSpeed * 1000f);
            int drawMs   = Mathf.RoundToInt(Plugin.DrawDurationSeconds.Value * 1000f);

            __result = Task.Delay(drawMs + normalMs);
            return false;
        }
    }

    // preload the bundle as soon as the raid starts so the first reload in-raid
    // doesnt hitch waiting for it to load.
    public class RaidStartBundleWarmPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(GameWorld).GetMethod("OnGameStarted");

        [PatchPostfix]
        public static void Postfix()
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player != null)
                player.StartCoroutine(LoadAmmoAnimController.WarmBundleAsync());
        }
    }

}
