using UnityEngine;

namespace FearMe.Core
{
    // A place a key or a rite page is allowed to turn up: under a bed, on a
    // trolley, at the back of a cupboard. The run director picks from these,
    // never two on top of each other and only in zones that are open.
    public class SpawnSpot : MonoBehaviour
    {
        [SerializeField] private bool allowKeys = true;
        [SerializeField] private bool allowPages = true;
        [Tooltip("Leave at -1 to use whichever zone box it sits in.")]
        [SerializeField] private int zoneOverride = -1;
        [Tooltip("How much likelier than a normal spot to be picked - " +
            "raise it behind a lever gate, say, to make that puzzle matter.")]
        [SerializeField, Min(0.1f)] private float weight = 1f;

        public bool AllowKeys => allowKeys;
        public bool AllowPages => allowPages;
        public float Weight => weight;
        public int Zone => zoneOverride >= 0 ? zoneOverride : HospitalZone.ZoneOf(transform.position);

        private void OnDrawGizmos()
        {
            Gizmos.color = allowPages && allowKeys ? new Color(0.9f, 0.8f, 0.3f)
                : allowPages ? new Color(1f, 0.85f, 0.2f) : new Color(0.6f, 0.65f, 0.7f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.15f, 0.15f * weight);
        }
    }
}
