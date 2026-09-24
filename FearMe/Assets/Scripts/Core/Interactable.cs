using UnityEngine;

namespace FearMe.Core
{
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string Prompt { get; }

        // True when Interact should be called every frame the key is held,
        // for things that take time rather than happening at a keypress.
        public virtual bool HoldToUse => false;

        public abstract void Interact();
    }
}
