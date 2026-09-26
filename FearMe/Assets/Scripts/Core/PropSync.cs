using System;
using System.Collections.Generic;
using System.Text;
using FearMe.Net;
using UnityEngine;

namespace FearMe.Core
{
    // Doors, levers, gates, carried things: state that both players have to
    // agree on, without each one needing to be a network object.
    //
    // A prop is identified by where it sits in the scene. Both machines load
    // the same scene, so the same door hashes to the same id on both - and a
    // change is just (id, state, value) sent to the other side. The value is
    // a Vector3 so a dropped item can say where it landed; a door just uses x.
    //
    // In a solo run Publish goes nowhere and nothing else changes.
    public static class PropSync
    {
        private static readonly Dictionary<int, Action<int, Vector3>> handlers = new Dictionary<int, Action<int, Vector3>>();
        private static readonly Dictionary<int, Component> owners = new Dictionary<int, Component>();

        public static int Register(Component owner, Action<int, Vector3> apply)
        {
            int id = StableId(owner.transform);

            if (handlers.ContainsKey(id))
                Debug.LogWarning($"[FearMe] Two synced props share an id at '{Path(owner.transform)}'.", owner);

            handlers[id] = apply;
            owners[id] = owner;
            return id;
        }

        // For something built at runtime, whose place in the hierarchy may
        // differ between machines: the caller supplies an id both agree on.
        public static int RegisterWithId(int id, Component owner, Action<int, Vector3> apply)
        {
            handlers[id] = apply;
            owners[id] = owner;
            return id;
        }

        public static void Unregister(int id)
        {
            handlers.Remove(id);
            owners.Remove(id);
        }

        // The same prop on this machine, from an id the other one sent.
        public static T Find<T>(int id) where T : class
        {
            return owners.TryGetValue(id, out Component owner) ? owner as T : null;
        }

        // Tell the other player's copy what just happened here.
        public static void Publish(int id, int state, Vector3 value)
        {
            CoopHooks.PropChanged?.Invoke(id, state, value);
        }

        // The other player's change arriving.
        public static void Receive(int id, int state, Vector3 value)
        {
            if (handlers.TryGetValue(id, out Action<int, Vector3> apply)) apply(state, value);
        }

        // FNV-1a over the scene name and the hierarchy path by sibling index.
        // Sibling indices rather than names, so two doors both called "Door"
        // under the same parent still come out different.
        private static int StableId(Transform transform)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in Path(transform))
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }

        private static string Path(Transform transform)
        {
            StringBuilder path = new StringBuilder();
            for (Transform t = transform; t != null; t = t.parent)
                path.Insert(0, "/" + t.GetSiblingIndex());

            path.Insert(0, transform.gameObject.scene.name);
            return path.ToString();
        }
    }
}
