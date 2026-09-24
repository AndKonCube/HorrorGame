using FearMe.Items;
using FearMe.Net;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Core
{
    public class ExitDoor : Interactable
    {
        [SerializeField] private GameOverController gameFlow;

        [Header("Heavy item (optional)")]
        [Tooltip("Something that has to be carried here before the way out opens.")]
        [SerializeField] private HeavyItem requiredItem;
        [Tooltip("Where it is set down. Defaults to this door.")]
        [SerializeField] private Transform deliverySlot;

        private bool KeysDone =>
            ObjectiveTracker.Instance != null && ObjectiveTracker.Instance.AllKeysCollected;

        private bool ItemDone => requiredItem == null || requiredItem.IsDelivered;

        // Every exit bolt drawn, if the door has any.
        private static bool BoltsDone =>
            Deadbolt.Count == 0 || (SpawnDirector.Instance != null && SpawnDirector.Instance.State.boltsOpen >= Deadbolt.Count);

        private bool Unlocked => KeysDone && ItemDone && BoltsDone;

        private bool CarryingRequired
        {
            get
            {
                PlayerController local = PlayerRegistry.Local;
                PlayerHands hands = local != null ? local.GetComponent<PlayerHands>() : null;
                return requiredItem != null && hands != null && hands.Held == requiredItem;
            }
        }

        public override string Prompt
        {
            get
            {
                if (!KeysDone) return "Locked - find every key";
                if (!BoltsDone)
                {
                    int open = SpawnDirector.Instance != null ? SpawnDirector.Instance.State.boltsOpen : 0;
                    return $"Barred - draw the bolts ({open}/{Deadbolt.Count})";
                }
                if (ItemDone) return "Escape";
                return CarryingRequired
                    ? "Set down the " + requiredItem.DisplayName
                    : "It needs the " + requiredItem.DisplayName;
            }
        }

        public override void Interact()
        {
            if (KeysDone && !ItemDone && CarryingRequired)
            {
                requiredItem.Deliver(deliverySlot != null ? deliverySlot : transform);
                return;
            }

            if (!Unlocked) return;

            // One player reaching the door gets everyone out.
            if (CoopHooks.EscapeRequested != null && CoopHooks.EscapeRequested()) return;

            if (gameFlow == null) gameFlow = FindFirstObjectByType<GameOverController>();
            if (gameFlow != null) gameFlow.OnPlayerEscaped();
        }
    }
}
