using UnityEngine;

namespace FearMe.Core
{
    public enum Captivity
    {
        None = 0,
        Dragged = 1, // in the stalker's grip, on the way to a cage
        Caged = 2    // locked up, waiting for a rescue that might not come
    }

    // Whatever is holding a caught player: the stalker's grip while it drags
    // them, then the cage. Found on the other machine by its PropSync id.
    public interface ICaptiveAnchor
    {
        int AnchorId { get; }
        Transform HoldPoint { get; }
    }
}
