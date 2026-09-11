using UnityEngine;

namespace FearMe.AI
{
    // A simple ordered set of waypoints an EnemyStalkerAI cycles through while patrolling.
    public class PatrolRoute : MonoBehaviour
    {
        [SerializeField] private Transform[] waypoints;
        public int Count => waypoints != null ? waypoints.Length : 0;

        public Transform GetWaypoint(int index)
        {
            return waypoints[index % waypoints.Length];
        }

        private void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Length == 0) return;

            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawWireSphere(waypoints[i].position, 0.3f);

                Transform next = waypoints[(i + 1) % waypoints.Length];
                if (next != null)
                    Gizmos.DrawLine(waypoints[i].position, next.position);
            }
        }
    }
}
