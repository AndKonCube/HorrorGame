using System.Collections.Generic;
using UnityEngine;

namespace FearMe.Core
{
    // One region of the hospital - Lobby, Wards, Morgue - as a box. Keys and
    // pages only ever spawn in zones the team has already opened, so nothing
    // needed to progress can land behind a door nobody can open yet.
    //
    // Pure maths, deliberately not a collider: a trigger this size would
    // swallow line-of-sight and interaction raycasts all over the level.
    public class HospitalZone : MonoBehaviour
    {
        private static readonly List<HospitalZone> all = new List<HospitalZone>();

        [Tooltip("Order the team reaches it in. 0 is where the run starts.")]
        [SerializeField] private int index;
        [SerializeField] private string displayName = "Lobby";
        [Tooltip("Size of the box, in this object's local space.")]
        [SerializeField] private Vector3 size = new Vector3(20f, 8f, 20f);

        public int Index => index;
        public string DisplayName => displayName;

        // How many zones the level has: the highest index, plus one.
        public static int Count
        {
            get
            {
                int highest = 0;
                foreach (HospitalZone zone in all)
                    if (zone != null) highest = Mathf.Max(highest, zone.index);
                return highest + 1;
            }
        }

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        public bool Contains(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            Vector3 half = size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        // Where two boxes overlap, the earlier zone wins - a doorway belongs
        // to the side you reach first. Outside every box counts as zone 0.
        public static int ZoneOf(Vector3 point)
        {
            int found = int.MaxValue;
            foreach (HospitalZone zone in all)
            {
                if (zone != null && zone.index < found && zone.Contains(point)) found = zone.index;
            }
            return found == int.MaxValue ? 0 : found;
        }

        public static string NameOf(int index)
        {
            foreach (HospitalZone zone in all)
                if (zone != null && zone.index == index) return zone.displayName;
            return "zone " + (index + 1);
        }

        private void OnDrawGizmos()
        {
            Color[] tints = { new Color(0.3f, 0.8f, 0.4f), new Color(0.9f, 0.7f, 0.2f), new Color(0.8f, 0.2f, 0.2f) };
            Color tint = tints[Mathf.Abs(index) % tints.Length];

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(tint.r, tint.g, tint.b, 0.06f);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = tint;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
