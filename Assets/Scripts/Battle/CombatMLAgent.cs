using Unity.MLAgents;
using Unity.MLAgents.Actuators;

using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.AI;

namespace FPSManager.Battle
{
    // AIBrain(핸드튜닝 Utility AI)을 대체하는 학습형 전투 결정 레이어.
    // 타겟팅/사격 판단/이동을 전부 이 Agent가 담당한다 - AIBrain은 폐기하지 않고 코드는 남겨두되,
    // 이 컴포넌트가 붙은 오브젝트에서는 Awake에서 자동으로 비활성화해 충돌을 막는다.
    [RequireComponent(typeof(PlayerHealth), typeof(PlayerMovement), typeof(WeaponController))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class CombatMLAgent : Agent
    {
        [Header("이동 설정")]
        public float moveSpeed = 4.2f;
        public float turnSpeed = 240f;
        public float pitchSpeed = 180f;

        [Header("탐지 설정 (AIBrain과 동일 개념)")]
        public float detectRadius = 40f;
        public LayerMask losBlockMask = ~0;

        [Header("보상 설정")]
        public float damageDealtRewardScale = 0.01f;
        public float damageTakenPenaltyScale = 0.01f;
        public float killReward = 1f;
        public float deathPenalty = -1f;
        public float stepPenalty = -0.0005f;
        [Tooltip("데미지 보상과 별개로, 헤드샷이면 추가로 붙는 보너스 - 헤드샷 비중을 늘리기 위함")]
        public float headshotBonus = 0.3f;

        [Header("조준 정확도 보상 (명중률이 너무 낮은 문제 해결용)")]
        [Tooltip("적을 보고 있을 때, 조준 방향이 적과 얼마나 정렬됐는지에 비례해 매 스텝 주는 보상 - 맞혀야만 보상받던 기존 방식은 신호가 너무 희소해서 조준을 못 배웠음")]
        // v3에서 이 값이 너무 커서(0.003) 가만히 쳐다보기만 해도 한 에피소드 동안 킬 보상(+1)에
        // 맞먹는 보상이 누적되는 reward hacking이 발생 - 명중/킬 보상이 항상 우세하도록 10배 축소.
        public float aimRewardScale = 0.0003f;
        [Tooltip("조준이 거의 완벽(코사인 0.995 이상)할 때 추가로 주는 보너스 - '조준을 딱 맞춘다'는 감각을 강화")]
        public float preciseAimBonus = 0.0002f;

        [Header("엄폐물 활용 보상 (전혀 안 쓰는 문제 해결용)")]
        public float coverSearchRadius = 15f;
        [Tooltip("교전 중(적이 보일 때) 가까운 엄폐물에 붙어있을수록 주는 보상")]
        public float coverRewardScale = 0.0001f;

        [Header("교전거리 보상 (너무 붙지도, 너무 멀어지지도 않게)")]
        [Tooltip("가장 보상을 많이 받는 목표 교전거리. AIBrain의 preferredCombatDistance와 동일 값 사용")]
        public float preferredCombatDistance = 14f;
        [Tooltip("이 거리만큼 목표에서 벗어나면 보상이 0까지 떨어짐 (선형 감쇠)")]
        public float distanceTolerance = 8f;
        [Tooltip("스텝 페널티(-0.0005)보다 살짝 큰 정도로 - 거리 하나로 전략이 결정되지 않도록 작게 유지")]
        public float distanceRewardScale = 0.0002f;

        private PlayerHealth health;
        private PlayerMovement movement;
        private WeaponController weapon;
        private NavMeshAgent agent;
        private PlayerCombatStats stats;

        private PlayerHealth currentTarget;
        private float currentPitch;

        protected override void Awake()
        {
            base.Awake();

            health = GetComponent<PlayerHealth>();
            movement = GetComponent<PlayerMovement>();
            weapon = GetComponent<WeaponController>();
            agent = GetComponent<NavMeshAgent>();

            stats = GetComponent<PlayerCombatStats>();
            if (stats == null) stats = gameObject.AddComponent<PlayerCombatStats>();

            // AIBrain과 동시에 붙어 있으면 이동/사격 명령이 충돌하므로, 학습 에이전트가 있으면 AIBrain은 끈다.
            // (AIBrain.cs 자체는 삭제하지 않고 유지 - 언제든 다시 켜서 비교/폴백 가능)
            var brain = GetComponent<AIBrain>();
            if (brain != null) brain.enabled = false;

            agent.updateRotation = false;
            agent.updatePosition = true;

            health.OnDamaged += HandleDamaged;
            health.OnDeathWithAttacker += HandleDeath;
        }

        public override void OnEpisodeBegin()
        {
            currentTarget = null;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (health == null || health.IsDead)
            {
                // 죽은 뒤에도 한 스텝 정도 관측이 요청될 수 있어 0으로 채워 크기를 맞춘다.
                for (int i = 0; i < 17; i++) sensor.AddObservation(0f);
                return;
            }

            sensor.AddObservation(Mathf.Clamp01(health.CurrentHealth / health.maxHealth));

            PlayerHealth enemy = FindNearestVisibleEnemy();
            currentTarget = enemy;

            if (enemy != null)
            {
                Vector3 toEnemy = enemy.transform.position - transform.position;
                float dist = toEnemy.magnitude;
                Vector3 dir = transform.InverseTransformDirection(toEnemy.normalized);

                sensor.AddObservation(1f); // 적 발견 여부
                sensor.AddObservation(dir.x);
                sensor.AddObservation(dir.z);
                sensor.AddObservation(Mathf.Clamp01(dist / detectRadius));
                sensor.AddObservation(Mathf.Clamp01(enemy.CurrentHealth / enemy.maxHealth));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(1f); // 거리 없음 -> 최대값
                sensor.AddObservation(0f);
            }

            if (MatchManager.Instance != null)
            {
                var allies = MatchManager.Instance.GetTeam(health.teamId);
                var enemies = MatchManager.Instance.GetEnemies(health.teamId);
                sensor.AddObservation(Mathf.Clamp01(MatchManager.Instance.CountAlive(allies) / 5f));
                sensor.AddObservation(Mathf.Clamp01(MatchManager.Instance.CountAlive(enemies) / 5f));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }

            sensor.AddObservation(stats.clutch);
            sensor.AddObservation(stats.positioning);
            sensor.AddObservation(agent != null && agent.isOnNavMesh ? agent.velocity.magnitude / Mathf.Max(moveSpeed, 0.01f) : 0f);

            // 현재 조준이 적과 얼마나 정렬됐는지(코사인, -1~1) - 보상뿐 아니라 관측으로도 줘서
            // "지금 얼마나 잘 조준하고 있는지"를 직접 인지하고 미세조정할 수 있게 함
            if (enemy != null && movement != null && movement.aimPivot != null)
            {
                Vector3 aimDir = movement.aimPivot.forward;
                Vector3 toEnemyHead = (enemy.transform.position + Vector3.up * 1.5f) - movement.aimPivot.position;
                sensor.AddObservation(Vector3.Dot(aimDir.normalized, toEnemyHead.normalized));

                // aimDot은 "얼마나 틀렸는지" 크기만 알려주고 "위/아래 어느 쪽으로 고쳐야 하는지" 방향은
                // 안 알려줘서 피치 제어를 못 배우던 버그(v3) 수정 - aimPivot 로컬 기준 수직 성분을 추가로 줘서
                // yaw의 dir.x/dir.z처럼 명시적인 방향 신호를 제공한다.
                Vector3 localAimDir = movement.aimPivot.InverseTransformDirection(toEnemyHead.normalized);
                sensor.AddObservation(localAimDir.y);
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }

            // 가장 가까운 엄폐물 - 이게 없으면 엄폐물이 어디 있는지 알 방법이 없어서 활용을 배울 수가 없었음
            Transform nearestCover = FindNearestCover();
            if (nearestCover != null)
            {
                Vector3 toCover = nearestCover.position - transform.position;
                Vector3 coverDir = transform.InverseTransformDirection(toCover.normalized);
                sensor.AddObservation(1f);
                sensor.AddObservation(coverDir.x);
                sensor.AddObservation(coverDir.z);
                sensor.AddObservation(Mathf.Clamp01(toCover.magnitude / coverSearchRadius));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(1f);
            }
        }

        Transform FindNearestCover()
        {
            GameObject[] covers = GameObject.FindGameObjectsWithTag("Cover");
            Transform best = null;
            float bestDist = coverSearchRadius;

            foreach (var cover in covers)
            {
                float dist = Vector3.Distance(transform.position, cover.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = cover.transform;
                }
            }
            return best;
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            AddReward(stepPenalty);

            if (health == null || health.IsDead)
            {
                weapon.triggerPressed = false;
                return;
            }

            float moveForward = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
            float moveStrafe = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
            float yawDelta = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);
            float pitchDelta = Mathf.Clamp(actions.ContinuousActions[3], -1f, 1f);
            bool wantsFire = actions.DiscreteActions[0] == 1;

            ApplyRotation(yawDelta, pitchDelta);
            ApplyMovement(moveForward, moveStrafe);
            ApplyDistanceReward();
            ApplyAimReward();
            ApplyCoverReward();

            // 적이 보일 때만 실제로 발사되도록 하드 게이팅 - "안 보이는데도 난사" 문제를
            // 보상만으로 학습시키는 대신 구조적으로 원천 차단한다(AIBrain도 동일하게 LOS로 발사를 게이팅함).
            weapon.triggerPressed = wantsFire && currentTarget != null;
        }

        // 목표 교전거리에서 가장 큰 보상, 너무 붙거나 너무 멀어지면 선형으로 감소(0까지) - "멀수록 좋다"가 아니라
        // 특정 구간으로 수렴하도록 유도해 무한 후퇴/무한 근접을 둘 다 억제한다.
        void ApplyDistanceReward()
        {
            if (currentTarget == null || currentTarget.IsDead) return;

            float dist = Vector3.Distance(transform.position, currentTarget.transform.position);
            float diff = Mathf.Abs(dist - preferredCombatDistance);
            float band = Mathf.Clamp01(1f - diff / distanceTolerance);
            AddReward(band * distanceRewardScale);
        }

        // 적을 보고 있을 때, 조준 방향이 적에게 정렬될수록 매 스텝 보상 - 명중이라는 희귀한 사건만 기다리지 않고
        // "지금 잘 조준하고 있다"는 걸 매 프레임 알려줘서 조준 자체를 학습하게 만든다.
        void ApplyAimReward()
        {
            if (currentTarget == null || currentTarget.IsDead || movement == null || movement.aimPivot == null) return;

            Vector3 aimDir = movement.aimPivot.forward;
            Vector3 toEnemyHead = (currentTarget.transform.position + Vector3.up * 1.5f) - movement.aimPivot.position;
            float dot = Vector3.Dot(aimDir.normalized, toEnemyHead.normalized);
            float aligned = Mathf.Clamp01(dot);

            AddReward(aligned * aimRewardScale);
            if (dot > 0.995f) AddReward(preciseAimBonus);
        }

        // 교전 중(적이 보일 때) 엄폐물에 가까이 있을수록 보상 - 엄폐물 위치를 관측에 추가한 것과 짝을 이룬다.
        void ApplyCoverReward()
        {
            if (currentTarget == null || currentTarget.IsDead) return;

            Transform cover = FindNearestCover();
            if (cover == null) return;

            float dist = Vector3.Distance(transform.position, cover.position);
            float band = Mathf.Clamp01(1f - dist / coverSearchRadius);
            AddReward(band * coverRewardScale);
        }

        void ApplyRotation(float yawDelta, float pitchDelta)
        {
            transform.Rotate(Vector3.up, yawDelta * turnSpeed * Time.fixedDeltaTime, Space.World);

            if (movement != null && movement.aimPivot != null)
            {
                currentPitch = Mathf.Clamp(currentPitch - pitchDelta * pitchSpeed * Time.fixedDeltaTime, -50f, 50f);
                movement.aimPivot.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
            }
        }

        void ApplyMovement(float moveForward, float moveStrafe)
        {
            if (agent == null || !agent.isOnNavMesh) return;

            Vector3 moveDir = transform.forward * moveForward + transform.right * moveStrafe;
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            // NavMeshAgent를 경로탐색이 아니라 직접 위치 구동 방식으로 사용
            // (nextPosition을 매 스텝 갱신하면 NavMesh 표면에 붙은 채로 커스텀 이동이 가능)
            Vector3 desired = agent.nextPosition + moveDir * moveSpeed * Time.fixedDeltaTime;
            agent.nextPosition = desired;
        }

        void HandleDamaged(PlayerHealth victim, PlayerHealth attacker, bool isHeadshot, float amount)
        {
            AddReward(-amount * damageTakenPenaltyScale);

            if (attacker != null)
            {
                var attackerAgent = attacker.GetComponent<CombatMLAgent>();
                if (attackerAgent != null)
                {
                    attackerAgent.AddReward(amount * damageDealtRewardScale);
                    if (isHeadshot) attackerAgent.AddReward(headshotBonus);
                }
            }
        }

        void HandleDeath(PlayerHealth victim, PlayerHealth attacker, bool isHeadshot)
        {
            AddReward(deathPenalty);
            weapon.triggerPressed = false;
            EndEpisode();

            if (attacker != null)
            {
                var attackerAgent = attacker.GetComponent<CombatMLAgent>();
                attackerAgent?.AddReward(killReward);
            }
        }

        bool HasLineOfSight(PlayerHealth other)
        {
            if (other == null) return false;

            Vector3 eye = movement.aimPivot != null ? movement.aimPivot.position : transform.position + Vector3.up * 1.5f;
            Vector3 targetPoint = other.transform.position + Vector3.up * 1.5f;
            Vector3 dir = targetPoint - eye;
            float dist = dir.magnitude;
            if (dist > detectRadius) return false;

            RaycastHit[] hits = Physics.RaycastAll(eye, dir.normalized, dist, losBlockMask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider.isTrigger) continue;
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

                PlayerHealth hitHealth = hit.collider.GetComponentInParent<PlayerHealth>();
                return hitHealth == other;
            }
            return true;
        }

        PlayerHealth FindNearestVisibleEnemy()
        {
            if (MatchManager.Instance == null) return null;

            var enemies = MatchManager.Instance.GetEnemies(health.teamId);
            PlayerHealth best = null;
            float bestDist = detectRadius;

            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.IsDead) continue;

                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist > bestDist) continue;
                if (!HasLineOfSight(enemy)) continue;

                bestDist = dist;
                best = enemy;
            }

            return best;
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            // 트레이너/학습된 모델이 안 붙어 있을 때(BehaviorType=Default) 자동으로 여기로 빠짐.
            // 프로젝트가 New Input System 전용이라 레거시 Input 클래스는 예외를 던지므로 사용하지 않음 - 정지 상태로 대기.
            var continuous = actionsOut.ContinuousActions;
            continuous[0] = 0f;
            continuous[1] = 0f;
            continuous[2] = 0f;
            continuous[3] = 0f;

            var discrete = actionsOut.DiscreteActions;
            discrete[0] = 0;
        }
    }
}
