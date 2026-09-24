using System.Collections.Generic;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Core
{
    // Every system used to hold a direct reference to "the player". With two
    // of them that breaks, so they ask here instead.
    //
    // Deliberately plain: registration happens in OnEnable, which works the
    // same whether a player is spawned by the scene or later by netcode.
    public static class PlayerRegistry
    {
        private static readonly List<PlayerController> players = new List<PlayerController>();

        public static IReadOnlyList<PlayerController> All => players;

        // The player this machine controls. Remote players are not it.
        public static PlayerController Local { get; private set; }

        public static void Register(PlayerController player)
        {
            if (player == null || players.Contains(player)) return;

            players.Add(player);
            if (player.IsLocalPlayer) Local = player;
        }

        public static void Unregister(PlayerController player)
        {
            players.Remove(player);
            if (Local == player) Local = null;
        }

        public static bool AnyAlive()
        {
            foreach (PlayerController player in players)
            {
                if (player != null && !IsOutOfAction(player)) return true;
            }
            return false;
        }

        // Downed players are out of the running: the stalker should move on
        // to whoever is still standing.
        public static bool IsOutOfAction(PlayerController player)
        {
            PlayerVitals vitals = player.GetComponent<PlayerVitals>();
            return vitals != null && vitals.IsDown;
        }

        public static PlayerController Nearest(Vector3 position, bool skipDowned = true)
        {
            PlayerController nearest = null;
            float best = float.MaxValue;

            foreach (PlayerController player in players)
            {
                if (player == null) continue;
                if (skipDowned && IsOutOfAction(player)) continue;

                float distance = (player.transform.position - position).sqrMagnitude;
                if (distance >= best) continue;

                best = distance;
                nearest = player;
            }

            return nearest;
        }
    }
}
