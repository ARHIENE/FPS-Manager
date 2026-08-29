using System.Collections;
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
        [Tooltip("좌우 스트레이프에 전진/후퇴를 섞어 원형 궤도만 도는 것을 방지")]
        public float distanceJitter = 3f;

        [Header("정지-이동 전환 블렌딩")]
        [Tooltip("클수록 정지사격 <-> 이동 전환이 빠르게(딱딱하게) 일어남")]
        public float moveBlendSpeed = 6f;

        [Header("앉기/뛰기 속도 배율")]
        public float crouchSpeedMultiplier = 0.5f;
        public float sprintSpeedMultiplier = 1.5f;

        [Header("회피용 점프(홉) 설정")]
        public float jumpDistance = 2.5f;
        public float jumpHeight = 0.8f;
        public float jumpDuration = 0.35f;

        private const float combatMoveStoppingDistance = 0.6f;

        private NavMeshAgent agent;
        private PlayerMovement movement;
        private int strafeDir = 1;
        private float currentDistanceOffset;
        private float nextStrafeFlipTime;
        private float lastRepathTime;
        private float moveScale = 1f;
        private float desiredMoveScale = 1f;
        private bool isJumping;

        public bool IsJumping => isJumping;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            movement = GetComponent<PlayerMovement>();
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
                float stateMultiplier = 1f;
                if (movement != null)
                {
                    if (movement.IsCrouching) stateMultiplier = crouchSpeedMultiplier;
                    else if (movement.IsSprinting) stateMultiplier = sprintSpeedMultiplier;
                }
                agent.speed = moveSpeed * moveScale * stateMultiplier;
            }
        }

        // 회피용 짧은 홉: NavMesh 경로와 무관하게 지정 방향으로 포물선 이동 후 복귀
        public void TriggerEvadeHop(Vector3 direction)
        {
            if (isJumping || agent == null || !agent.isOnNavMesh) return;
            StartCoroutine(EvadeHopRoutine(direction));
        }

        IEnumerator EvadeHopRoutine(Vector3 direction)
        {
            isJumping = true;

            Vector3 flatDir = new Vector3(direction.x, 0f, direction.z);
            if (flatDir.sqrMagnitude < 0.0001f) flatDir = transform.forward;
            flatDir.Normalize();

            Vector3 startPos = transform.position;
            Vector3 endPos = startPos + flatDir * jumpDistance;
            if (NavMesh.SamplePosition(endPos, out NavMeshHit navHit, jumpDistance, NavMesh.AllAreas))
            {
                endPos = navHit.position;
            }

            agent.enabled = false;

            float t = 0f;
            while (t < jumpDuration)
            {
                t += Time.deltaTime;
                float ratio = Mathf.Clamp01(t / jumpDuration);
                Vector3 flatPos = Vector3.Lerp(startPos, endPos, ratio);
                float arc = Mathf.Sin(ratio * Mathf.PI) * jumpHeight;
                transform.position = flatPos + Vector3.up * arc;
                yield return null;
            }

            transform.position = endPos;
            agent.enabled = true;
            if (agent.isOnNavMesh) agent.Warp(endPos);

            isJumping = false;
        }

        // 상위 레이어가 호출: true면 자유롭게 이동, false면 서서히 감속해 정지(정지사격 우선)
        public void SetMovementAllowed(bool allowed)
        {
            desiredMoveScale = allowed ? 1f : 0f;
        }

        // 목표 주위를 좌우로 스트레이프하며 교전 거리 유지
        public void TickCombatStrafe(Transform target, float repathInterval)
        {
            if (isJumping || agent == null || !agent.isOnNavMesh || target == null) return;

            if (Time.time >= nextStrafeFlipTime)
            {
                strafeDir = -strafeDir;
                currentDistanceOffset = Random.Range(-distanceJitter, distanceJitter);
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

            Vector3 anchor = target.position - dirToTarget * (preferredCombatDistance + currentDistanceOffset);
            return anchor + tangent * (strafeDir * strafeRadius);
        }

        // 정찰/엄폐/후퇴처럼 특정 지점으로 곧장 이동해야 할 때 공용으로 사용
        public void TickTowards(Vector3 point, float stoppingDistance, float repathInterval)
        {
            if (isJumping || agent == null || !agent.isOnNavMesh) return;

            if (Time.time - lastRepathTime >= repathInterval)
            {
                lastRepathTime = Time.time;
                agent.stoppingDistance = stoppingDistance;
                agent.SetDestination(point);
            }
        }
    }
}
