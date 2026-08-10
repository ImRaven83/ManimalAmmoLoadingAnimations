using EFT.NextObservedPlayer;
using UnityEngine;

namespace Manimal.LoadAmmoAnim.CustomEFTData
{
    // stub IObservedUsableItem the dispatch patch hands back when
    // ObservedPlayerUsableItemController.GetObservedUsableItem is called for our
    // item. without it, GetObservedUsableItem returns null and downstream
    // controller-swap code that expects a non-null instance silently breaks.
    public class LoadAmmoBundleInterfaceClass : IObservedUsableItem
    {
        public void Initialize(GameObject gameObject) { }
        public void UpdateData(ObservedUsableItemUpdatedData observedUsableItemUpdatedData) { }
        public void Disable() { }
    }
}
