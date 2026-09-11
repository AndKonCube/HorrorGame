using FearMe.Player;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace FearMe.AI
{
    // Core scare mechanic: a stalking enemy that patrols, investigates noise,
    // chases on sight, and searches around the last known position before
    // giving up and returning to its patrol.
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyStalkerAI : MonoBehaviour
    {
        private enum State { Patrol, Investigate, Chase, Search }

        [Header("References")]
        [SerializeField] private PlayerController player;
        [SerializeField] private PatrolRoute patrolRoute;
        [Tooltip("Vision origin, e.g. the enemy's head. Defaults to this transform.")]
        [SerializeField] private Transform eyes;

        [Header("Vision")]
        [SerializeField] private float viewDistance = 14f;
        [SerializeField] private float viewAngle = 70f;
        [SerializeField] private LayerMask obstructionMask;

        [Header("Speeds")]
        [SerializeField] private float patrolSpeed = 1.6f;
        [SerializeField] private float investigateSpeed = 2.2f;
        [SerializeField] private float chaseSpeed = 4.5f;

        [Header("Timings")]
        [SerializeField] private float investigateWaitTime = 4f;
        [SerializeField] private float loseSightGrace = 1.5f;
        [SerializeField] private float searchDuration = 6f;
        [SerializeField] private float catchDistance = 1.0f;

        [Header("Events")]
        public UnityEvent onPlayerCaught;

        private NavMeshAgent agent;
        private State state;
        private int patrolIndex;
        private Vector3 lastKnownPosition;
        private float stateTimer;
        private float sightLostTimer;
        private bool warnedOffMesh;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (eyes == null) eyes = transform;
        }

        private void Start()
        {
            EnterPatrol();
        }

        private void Update()
        {
            // Without a mesh under it the agent is never placed, and every
            // call below would throw once a frame. Say so once instead.
            if (!agent.isOnNavMesh)
            {
                if (!warnedOffMesh)
                {
                    warnedOffMesh = true;
                    Debug.LogWarning("[FearMe] '" + name + "' is not on a NavMesh, so it cannot move. " +
                        "Bake one with Tools > FearMe > Rebake NavMesh (current scene).", this);
                }
                return;
            }

            bool canSeePlayer = CanSeePlayer();
            bool canHearPlayer = CanHearPlayer();

            switch (state)
            {
                case State.Patrol:
                    TickPatrol(canSeePlayer, canHearPlayer);
                    break;
                case State.Investigate:
                    TickInvestigate(canSeePlayer, canHearPlayer);
                    break;
                case State.Chase:
                    TickChase(canSeePlayer);
                    break;
                case State.Search:
                    TickSearch(canSeePlayer, canHearPlayer);
                    break;
            }

            CheckCatch();
        }

        private bool CanSeePlayer()
        {
            if (player == null || player.IsHidden) return false;

            Vector3 toPlayer = player.transform.position - eyes.position;
            float distance = toPlayer.magnitude;
            if (distance > viewDistance) return false;

            float angle = Vector3.Angle(eyes.forward, toPlayer);
            if (angle > viewAngle * 0.5f) return false;

            if (Physics.Raycast(eyes.position, toPlayer.normalized, distance, obstructionMask))
                return false; // something is blocking line of sight

            return true;
        }

        private bool CanHearPlayer()
        {
            if (player == null) return false;
            float distance = Vector3.Distance(transform.position, player.transform.position);
            return distance <= player.CurrentNoiseRadius;
        }

        private void EnterPatrol()
        {
            state = State.Patrol;
            agent.speed = patrolSpeed;
            GoToNextPatrolPoint();
        }

        private void GoToNextPatrolPoint()
        {
            if (patrolRoute == null || patrolRoute.Count == 0) return;

            // Reached from Start, before Update's off-mesh guard can run.
            if (!agent.isOnNavMesh) return;

            agent.SetDestination(patrolRoute.GetWaypoint(patrolIndex).position);
            patrolIndex++;
        }

        private void TickPatrol(bool canSee, bool canHear)
        {
            if (canSee) { EnterChase(); return; }
            if (canHear) { EnterInvestigate(player.transform.position); return; }

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                GoToNextPatrolPoint();
        }

        private void EnterInvestigate(Vector3 location)
        {
            state = State.Investigate;
            agent.speed = investigateSpeed;
            lastKnownPosition = location;
            agent.SetDestination(lastKnownPosition);
            stateTimer = 0f;
        }

        private void TickInvestigate(bool canSee, bool canHear)
        {
            if (canSee) { EnterChase(); return; }
            if (canHear)
            {
                lastKnownPosition = player.transform.position;
                agent.SetDestination(lastKnownPosition);
                stateTimer = 0f;
                return;
            }

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                stateTimer += Time.deltaTime;
                if (stateTimer >= investigateWaitTime) EnterPatrol();
            }
        }

        private void EnterChase()
        {
            state = State.Chase;
            agent.speed = chaseSpeed;
            sightLostTimer = 0f;
        }

        private void TickChase(bool canSee)
        {
            if (canSee)
            {
                lastKnownPosition = player.transform.position;
                agent.SetDestination(lastKnownPosition);
                sightLostTimer = 0f;
            }
            else
            {
                sightLostTimer += Time.deltaTime;
                if (sightLostTimer >= loseSightGrace) EnterSearch();
            }
        }

        private void EnterSearch()
        {
            state = State.Search;
            agent.speed = investigateSpeed;
            agent.SetDestination(lastKnownPosition);
            stateTimer = 0f;
        }

        private void TickSearch(bool canSee, bool canHear)
        {
            if (canSee) { EnterChase(); return; }
            if (canHear) { EnterInvestigate(player.transform.position); return; }

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                stateTimer += Time.deltaTime;
                if (stateTimer >= searchDuration) EnterPatrol();
            }
        }

        private void CheckCatch()
        {
            if (state != State.Chase || player == null) return;

            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance <= catchDistance)
            {
                onPlayerCaught?.Invoke();
                agent.isStopped = true;
                enabled = false;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Transform origin = eyes != null ? eyes : transform;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, viewDistance);

            Quaternion leftRayRotation = Quaternion.AngleAxis(-viewAngle * 0.5f, Vector3.up);
            Quaternion rightRayRotation = Quaternion.AngleAxis(viewAngle * 0.5f, Vector3.up);
            Vector3 leftRayDirection = leftRayRotation * origin.forward;
            Vector3 rightRayDirection = rightRayRotation * origin.forward;

            Gizmos.color = Color.red;
            Gizmos.DrawRay(origin.position, leftRayDirection * viewDistance);
            Gizmos.DrawRay(origin.position, rightRayDirection * viewDistance);
        }
    }
}
