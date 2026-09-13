using UnityEngine;
using UnityEngine.AI;

namespace FearMe.Core
{
    public static class NavMeshUtility
    {
        // Storeys sit only a few metres apart, so NavMesh.SamplePosition with
        // a generous radius will happily hand back a point on the floor above
        // or below. Every lookup in a multi-storey level has to bound the
        // vertical error, or an agent ends up chasing the wrong floor.
        public static bool TrySampleSameFloor(Vector3 target, out Vector3 result,
            float radius = 2f, float maxVerticalDrift = 2f)
        {
            result = target;

            if (!NavMesh.SamplePosition(target, out NavMeshHit hit, radius, NavMesh.AllAreas))
                return false;

            if (Mathf.Abs(hit.position.y - target.y) > maxVerticalDrift)
                return false;

            result = hit.position;
            return true;
        }
    }
}
