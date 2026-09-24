using System.Collections.Generic;
using FearMe.Core;
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
        [Tooltip("Optional fallback; players normally register themselves.")]
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
        [Tooltip("Give up on a waypoint after this long and move to the next.")]
        [SerializeField] private float waypointTimeout = 12f;

        [Header("Hearing")]
        [Tooltip("Sounds from further above or below than this are on another " +
            "storey - heard through a floor, they would drag it the wrong way.")]
        [SerializeField] private float hearingFloorGap = 2.5f;

        [Header("Events")]
        public UnityEvent onPlayerCaught;

        private NavMeshAgent agent;
        private State state;
        private int patrolIndex;
        private Vector3 lastKnownPosition;
        private float stateTimer;
        private float sightLostTimer;
        private bool warnedOffMesh;
        private float destinationSetAt;
        private PlayerController quarry;

        private bool noisePending;
        private Vector3 noiseAt;
        private float noiseStrength;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (eyes == null) eyes = transform;
        }

        private void Start()
        {
            EnterPatrol();
        }

        private void OnEnable()
        {
            NoiseBus.Emitted += OnNoise;
        }

        private void OnDisable()
        {
            NoiseBus.Emitted -= OnNoise;
        }

        // Several sounds in one frame: go for the one that was loudest here.
        private void OnNoise(NoiseEvent noise)
        {
            if (!SameStorey(noise.position)) return;

            float distance = Vector3.Distance(transform.position, noise.position);
            if (distance > noise.radius) return;

            float strength = 1f - distance / noise.radius;
            if (noisePending && strength <= noiseStrength) return;

            noisePending = true;
            noiseAt = noise.position;
            noiseStrength = strength;
        }

        private bool SameStorey(Vector3 position)
        {
            return Mathf.Abs(position.y - transform.position.y) <= hearingFloorGap;
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

            PlayerController seen = FindVisiblePlayer();
            PlayerController heard = seen == null ? FindAudiblePlayer() : seen;
            if (seen != null) quarry = seen;

            // A sound is a place to go, not a person to chase: it never pulls
            // the stalker off someone it can actually see.
            bool noise = noisePending;
            noisePending = false;
            if (noise && seen == null && state != State.Chase)
            {
                EnterInvestigate(noiseAt);
                CheckCatch();
                return;
            }

            switch (state)
            {
                case State.Patrol:
                    TickPatrol(seen, heard);
                    break;
                case State.Investigate:
                    TickInvestigate(seen, heard);
                    break;
                case State.Chase:
                    TickChase(seen);
                    break;
                case State.Search:
                    TickSearch(seen, heard);
                    break;
            }

            CheckCatch();
        }

        // Whoever is closest and actually in view: with two players it should
        // commit to one rather than flicker between them.
        private PlayerController FindVisiblePlayer()
        {
            PlayerController best = null;
            float bestDistance = float.MaxValue;

            foreach (PlayerController candidate in Targets())
            {
                if (candidate.IsHidden) continue;

                Vector3 toPlayer = candidate.transform.position - eyes.position;
                float distance = toPlayer.magnitude;
                if (distance > viewDistance || distance >= bestDistance) continue;

                if (Vector3.Angle(eyes.forward, toPlayer) > viewAngle * 0.5f) continue;
                if (Physics.Raycast(eyes.position, toPlayer.normalized, distance, obstructionMask))
                    continue; // something is blocking line of sight

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        private PlayerController FindAudiblePlayer()
        {
            foreach (PlayerController candidate in Targets())
            {
                if (!SameStorey(candidate.transform.position)) continue;

                float distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (distance <= candidate.CurrentNoiseRadius) return candidate;
            }

            return null;
        }

        // Registered players, ignoring anyone already down - it has dealt with
        // them and should go after whoever is still up.
        private IEnumerable<PlayerController> Targets()
        {
            IReadOnlyList<PlayerController> registered = PlayerRegistry.All;

            if (registered.Count == 0)
            {
                if (player != null) yield return player;
                yield break;
            }

            foreach (PlayerController candidate in registered)
            {
                if (candidate == null || PlayerRegistry.IsOutOfAction(candidate)) continue;
                yield return candidate;
            }
        }

        private void EnterPatrol()
        {
            state = State.Patrol;
            agent.speed = patrolSpeed;
            GoToNextPatrolPoint();
        }

        // After losing the player it keeps working the area rather than
        // wandering off, so a closet stays a tense place to sit.
        private void ResumePatrolNear(Vector3 position)
        {
            if (patrolRoute != null && patrolRoute.Count > 0)
                patrolIndex = patrolRoute.IndexOfNearest(position);

            EnterPatrol();
        }

        private void GoToNextPatrolPoint()
        {
            if (patrolRoute == null || patrolRoute.Count == 0) return;

            // Reached from Start, before Update's off-mesh guard can run.
            if (!agent.isOnNavMesh) return;

            SetDestinationOnMesh(patrolRoute.GetWaypoint(patrolIndex).position);
            patrolIndex++;
        }

        // Waypoints get hand-placed slightly off the floor, which yields a
        // partial path the agent can never finish, so snap the target onto the
        // mesh first - but only onto this storey. A wider search would return
        // the floor above and send the stalker after the wrong level.
        private void SetDestinationOnMesh(Vector3 target)
        {
            if (NavMeshUtility.TrySampleSameFloor(target, out Vector3 onMesh))
                target = onMesh;

            destinationSetAt = Time.time;
            agent.SetDestination(target);
        }

        // A partial or invalid path never closes the remaining distance.
        // Without treating that as "done", one unreachable waypoint stalls
        // the patrol for the rest of the run.
        private bool ReachedDestination()
        {
            if (agent.pathPending) return false;
            if (agent.pathStatus != NavMeshPathStatus.PathComplete) return true;
            if (Time.time - destinationSetAt > waypointTimeout) return true;

            return agent.remainingDistance <= Mathf.Max(0.5f, agent.stoppingDistance);
        }

        private void TickPatrol(PlayerController seen, PlayerController heard)
        {
            if (seen != null) { EnterChase(); return; }
            if (heard != null) { EnterInvestigate(heard.transform.position); return; }

            if (ReachedDestination())
                GoToNextPatrolPoint();
        }

        private void EnterInvestigate(Vector3 location)
        {
            state = State.Investigate;
            agent.speed = investigateSpeed;
            lastKnownPosition = location;
            SetDestinationOnMesh(lastKnownPosition);
            stateTimer = 0f;
        }

        private void TickInvestigate(PlayerController seen, PlayerController heard)
        {
            if (seen != null) { EnterChase(); return; }
            if (heard != null)
            {
                lastKnownPosition = heard.transform.position;
                SetDestinationOnMesh(lastKnownPosition);
                stateTimer = 0f;
                return;
            }

            if (ReachedDestination())
            {
                stateTimer += Time.deltaTime;
                if (stateTimer >= investigateWaitTime) ResumePatrolNear(lastKnownPosition);
            }
        }

        private void EnterChase()
        {
            state = State.Chase;
            agent.speed = chaseSpeed;
            sightLostTimer = 0f;
        }

        private void TickChase(PlayerController seen)
        {
            if (seen != null)
            {
                quarry = seen;
                lastKnownPosition = seen.transform.position;
                SetDestinationOnMesh(lastKnownPosition);
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
            SetDestinationOnMesh(lastKnownPosition);
            stateTimer = 0f;
        }

        private void TickSearch(PlayerController seen, PlayerController heard)
        {
            if (seen != null) { EnterChase(); return; }
            if (heard != null) { EnterInvestigate(heard.transform.position); return; }

            if (ReachedDestination())
            {
                stateTimer += Time.deltaTime;
                if (stateTimer >= searchDuration) ResumePatrolNear(lastKnownPosition);
            }
        }

        private void CheckCatch()
        {
            if (state != State.Chase) return;

            PlayerController victim = PlayerRegistry.Nearest(transform.position) ?? quarry;
            if (victim == null) return;

            if (Vector3.Distance(transform.position, victim.transform.position) > catchDistance) return;

            // Downs this player and keeps hunting. Only when nobody is left
            // standing does the run actually end.
            PlayerVitals vitals = victim.GetComponent<PlayerVitals>();
            if (vitals != null && !vitals.IsDown)
            {
                vitals.GoDown();
                EnterSearch();
            }

            if (!PlayerRegistry.AnyAlive())
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
