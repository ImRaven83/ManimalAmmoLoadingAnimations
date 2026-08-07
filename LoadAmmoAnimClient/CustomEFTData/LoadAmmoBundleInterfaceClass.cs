using EFT.NextObservedPlayer;
using UnityEngine;

namespace Manimal.LoadAmmoAnim.CustomEFTData
{
    // stub IObservedUsableItem (formerly GInterface323 pre-4.1 deobfuscation) the
    // dispatch patch hands back when the engine's dispatcher is called for our
    // item. without it, the dispatcher returns null and downstream controller-swap
    // code that expects a non-null instance silently breaks.
    public class LoadAmmoBundleInterfaceClass : IObservedUsableItem
    {
        public void Initialize(GameObject gameObject) { }
        public void UpdateData(ObservedUsableItemUpdatedData observedUsableItemUpdatedData) { }
        public void Disable() { }
    }
}
