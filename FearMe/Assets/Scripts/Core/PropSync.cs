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
    // A prop is identified by what it is, its name and where it stands in
    // the scene - things both machines read from the same saved level. A
    // change is just (id, state, value) sent to the other side. The value is
    // a Vector3 so a dropped item can say where it landed; a door just uses x.
    //
    // The kind of prop is part of the id, so a message meant for a door can
    // never be taken by a lever. If the two machines run different versions
    // of a level the ids stop matching; LevelFingerprint lets the network
    // layer notice that and say so, rather than things silently not syncing.
    //
    // In a solo run Publish goes nowhere and nothing else changes.
    public static class PropSync
    {
        private static readonly Dictionary<int, Action<int, Vector3>> handlers = new Dictionary<int, Action<int, Vector3>>();
        private static readonly Dictionary<int, Component> owners = new Dictionary<int, Component>();

        public static int Register(Component owner, Action<int, Vector3> apply)
        {
            string key = Key(owner);
            int id = Hash(key);

            if (handlers.ContainsKey(id) && owners.TryGetValue(id, out Component other) && other != null && other != owner)
                Debug.LogWarning($"[FearMe] Two synced props share an id: '{key}'. Move or rename one.", owner);

            handlers[id] = apply;
            owners[id] = owner;
            return id;
        }

        // For something built at runtime: the caller supplies an id both
        // machines work out the same way.
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

        // Every prop registered right now, as one number. Two machines on the
        // same level get the same number; different versions of it do not.
        public static int Fingerprint()
        {
            unchecked
            {
                int sum = 0;
                foreach (int id in handlers.Keys) sum += id * 16777619;
                return sum;
            }
        }

        // What kind of prop, in which scene, under which names, standing
        // where (to the centimetre). Names rather than sibling indices, so an
        // editor-only object stripped from a build does not shift everything.
        private static string Key(Component owner)
        {
            Transform t = owner.transform;
            Vector3Int cm = Vector3Int.RoundToInt(t.position * 100f);

            StringBuilder key = new StringBuilder(owner.GetType().FullName);
            key.Append('|').Append(t.gameObject.scene.name).Append('|');
            for (Transform node = t; node != null; node = node.parent)
                key.Insert(key.Length, "/" + node.name);
            key.Append('|').Append(cm.x).Append(',').Append(cm.y).Append(',').Append(cm.z);
            return key.ToString();
        }

        // FNV-1a: stable across machines and runs, unlike string.GetHashCode.
        public static int Hash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }
    }
}
