using UnityEngine;

namespace FearMe.Core
{
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string Prompt { get; }
        public abstract void Interact();
    }
}
