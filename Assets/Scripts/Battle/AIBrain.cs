using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FPSManager.Battle
{
    // 5v5 AI 배틀 FSM: Search(적 탐색/전진) <-> Engage(교전)
    [RequireComponent(typeof(PlayerHealth), typeof(PlayerMovement), typeof(WeaponController))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class AIBrain : MonoBehaviour
    {
        [Header("탐지/교전 설정")]
        public float detectRadius = 40f;
        public float engageRange = 24f;
        public float repathInterval = 0.25f;
        public LayerMask losBlockMask = ~0;

        [Header("정찰 이동 설정")]
        public float roamStoppingDistance = 0.5f;
        public float roamReachedThreshold = 1.5f;

        private PlayerHealth health;
        private PlayerMovement movement;
        private WeaponController weapon;
        private NavMeshAgent agent;

        private PlayerHealth currentTarget;
        private float lastRepathTime;
        private Vector3 roamPoint;
        private float engageStoppingDistance;

        void Awake()
        {
            health = GetComponent<PlayerHealth>();
            movement = GetComponent<PlayerMovement>();
            weapon = GetComponent<WeaponController>();
            agent = GetComponent<NavMeshAgent>();
            engageStoppingDistance = engageRange * 0.7f;
            if (agent != null)
            {
                agent.stoppingDistance = engageStoppingDistance;
                agent.speed = 4.2f;
                agent.angularSpeed = 720f;
                agent.acceleration = 14f;
            }
        }

        void Update()
        {
            if (health.IsDead)
            {
                weapon.triggerPressed = false;
                return;
            }

            currentTarget = FindNearestVisibleEnemy();

            if (currentTarget != null)
            {
                Engage(currentTarget);
            }
            else
            {
                Search();
            }
        }

        void Engage(PlayerHealth target)
        {
            Vector3 aimPoint = target.transform.position + Vector3.up * 1.5f;
            movement.AimAt(aimPoint);

            if (Time.time - lastRepathTime >= repathInterval && agent.isOnNavMesh)
            {
                lastRepathTime = Time.time;
                // 교전 중에는 사거리 밖에서 멈춰서 쏘도록 정지 거리를 크게 둔다.
                agent.stoppingDistance = engageStoppingDistance;
                agent.SetDestination(target.transform.position);
            }

            weapon.triggerPressed = true;
        }

        void Search()
        {
            weapon.triggerPressed = false;
            movement.FaceMoveDirection();

            if (Time.time - lastRepathTime >= repathInterval && agent.isOnNavMesh)
            {
                lastRepathTime = Time.time;
                // 정찰 이동은 목적지까지 끝까지 가야 하므로 정지 거리를 거의 0으로 둔다.
                // (교전용 stoppingDistance를 그대로 쓰면 목적지 한참 못 미쳐서 "도착" 판정 나
                // 멈춰버리고, 재탐색 조건도 충족 못 해서 계속 정지 상태로 남는 버그가 있었음)
                agent.stoppingDistance = roamStoppingDistance;

                if (roamPoint == Vector3.zero || Vector3.Distance(transform.position, roamPoint) < roamReachedThreshold)
                {
                    roamPoint = MatchManager.Instance != null
                        ? MatchManager.Instance.GetRoamPoint(health.teamId)
                        : transform.position;
                }
                agent.SetDestination(roamPoint);
            }
        }

        PlayerHealth FindNearestVisibleEnemy()
        {
            if (MatchManager.Instance == null) return null;

            List<PlayerHealth> enemies = MatchManager.Instance.GetEnemies(health.teamId);
            PlayerHealth best = null;
            float bestDist = detectRadius;

            Vector3 eye = movement.aimPivot != null ? movement.aimPivot.position : transform.position + Vector3.up * 1.5f;

            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.IsDead) continue;

                Vector3 targetPoint = enemy.transform.position + Vector3.up * 1.5f;
                float dist = Vector3.Distance(eye, targetPoint);
                if (dist > bestDist) continue;

                Vector3 dir = targetPoint - eye;
                RaycastHit[] hits = Physics.RaycastAll(eye, dir.normalized, dist, losBlockMask);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                bool blocked = false;
                foreach (var hit in hits)
                {
                    if (hit.collider.isTrigger) continue;
                    if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

                    PlayerHealth hitHealth = hit.collider.GetComponentInParent<PlayerHealth>();
                    if (hitHealth != enemy)
                    {
                        blocked = true;
                    }
                    break;
                }
                if (blocked) continue;

                bestDist = dist;
                best = enemy;
            }

            return best;
        }
    }
}

