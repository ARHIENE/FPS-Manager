using UnityEngine;
using UnityEngine.AI;

namespace FPSManager.Battle
{
    // "어떻게 움직이는가"만 담당하는 생성형 무빙 레이어.
    // AIBrain(상위 결정 레이어)이 매 프레임 Tick*/SetMovementAllowed를 호출해 지시하고,
    // 이 클래스는 그 지시를 실제 NavMeshAgent 목적지/속도로 변환한다.
    [RequireComponent(typeof(NavMeshAgent))]
    public class MovementStepSelector : MonoBehaviour
    {
        [Header("이동 속도 설정")]
        public float moveSpeed = 4.2f;
        public float angularSpeed = 720f;
        public float acceleration = 14f;

        [Header("교전 중 무빙(스트레이프) 설정")]
        public float preferredCombatDistance = 14f;
        public float strafeRadius = 5f;
        public float strafeMinInterval = 0.5f;
        public float strafeMaxInterval = 1.4f;
        public float jukeChance = 0.25f;

        [Header("정지-이동 전환 블렌딩")]
        [Tooltip("클수록 정지사격 <-> 이동 전환이 빠르게(딱딱하게) 일어남")]
        public float moveBlendSpeed = 6f;

        private const float combatMoveStoppingDistance = 0.6f;

        private NavMeshAgent agent;
        private int strafeDir = 1;
        private float nextStrafeFlipTime;
        private float lastRepathTime;
        private float moveScale = 1f;
        private float desiredMoveScale = 1f;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.speed = moveSpeed;
            agent.angularSpeed = angularSpeed;
            agent.acceleration = acceleration;

            strafeDir = Random.value > 0.5f ? 1 : -1;
            nextStrafeFlipTime = Time.time + Random.Range(strafeMinInterval, strafeMaxInterval);
        }

        void Update()
        {
            moveScale = Mathf.MoveTowards(moveScale, desiredMoveScale, moveBlendSpeed * Time.deltaTime);
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.speed = moveSpeed * moveScale;
            }
        }

        // 상위 레이어가 호출: true면 자유롭게 이동, false면 서서히 감속해 정지(정지사격 우선)
        public void SetMovementAllowed(bool allowed)
        {
            desiredMoveScale = allowed ? 1f : 0f;
        }

        // 목표 주위를 좌우로 스트레이프하며 교전 거리 유지
        public void TickCombatStrafe(Transform target, float repathInterval)
        {
            if (agent == null || !agent.isOnNavMesh || target == null) return;

            if (Time.time >= nextStrafeFlipTime)
            {
                strafeDir = -strafeDir;
                bool juke = Random.value < jukeChance;
                nextStrafeFlipTime = Time.time + (juke ? Random.Range(0.12f, 0.22f) : Random.Range(strafeMinInterval, strafeMaxInterval));
                lastRepathTime = -999f;
            }

            if (Time.time - lastRepathTime >= repathInterval)
            {
                lastRepathTime = Time.time;
                agent.stoppingDistance = combatMoveStoppingDistance;

                Vector3 strafePoint = ComputeStrafePoint(target);
                if (NavMesh.SamplePosition(strafePoint, out NavMeshHit navHit, strafeRadius + 2f, NavMesh.AllAreas))
                {
                    strafePoint = navHit.position;
                }
                agent.SetDestination(strafePoint);
            }
        }

        Vector3 ComputeStrafePoint(Transform target)
        {
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            Vector3 dirToTarget = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;
            Vector3 tangent = Vector3.Cross(Vector3.up, dirToTarget);

            Vector3 anchor = target.position - dirToTarget * preferredCombatDistance;
            return anchor + tangent * (strafeDir * strafeRadius);
        }

        // 정찰/엄폐/후퇴처럼 특정 지점으로 곧장 이동해야 할 때 공용으로 사용
        public void TickTowards(Vector3 point, float stoppingDistance, float repathInterval)
        {
            if (agent == null || !agent.isOnNavMesh) return;

            if (Time.time - lastRepathTime >= repathInterval)
            {
                lastRepathTime = Time.time;
                agent.stoppingDistance = stoppingDistance;
                agent.SetDestination(point);
            }
        }
    }
}
