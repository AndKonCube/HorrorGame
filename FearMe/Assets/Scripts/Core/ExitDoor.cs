using UnityEngine;

namespace FearMe.Core
{
    public class ExitDoor : Interactable
    {
        [SerializeField] private GameOverController gameFlow;

        private bool Unlocked =>
            ObjectiveTracker.Instance != null && ObjectiveTracker.Instance.AllKeysCollected;

        public override string Prompt => Unlocked ? "Escape" : "Locked - find every key";

        public override void Interact()
        {
            if (!Unlocked) return;
            if (gameFlow == null) gameFlow = FindFirstObjectByType<GameOverController>();
            if (gameFlow != null) gameFlow.OnPlayerEscaped();
        }
    }
}
