using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FPSManager.Battle
{
    // AI 전투 행동 결정 레이어: 적 탐색/교전 여부, 사격 중 정지 우선, 피격 시 반응(반격/엄폐/후퇴/역공격)까지 총괄한다.
    // "어떻게 움직이는가"는 MovementStepSelector에 위임하고, 이 클래스는 "지금 쏠지/움직일지/어떤 반응을 택할지"만 결정한다.
    [RequireComponent(typeof(PlayerHealth), typeof(PlayerMovement), typeof(WeaponController))]
    [RequireComponent(typeof(NavMeshAgent), typeof(MovementStepSelector), typeof(PlayerCombatStats))]
    public class AIBrain : MonoBehaviour
    {
        [Header("탐지/교전 설정")]
        public float detectRadius = 55f;
        public float engageRange = 28f;
        public float repathInterval = 0.25f;
        public LayerMask losBlockMask = ~0;

        [Header("탐지 스로틀링 설정 (80인 규모 성능 대응)")]
        [Tooltip("적 탐색(그리드 조회 + 라인오브사이트 레이캐스트)을 매 프레임이 아니라 이 주기마다만 갱신한다")]
        public float detectInterval = 0.15f;

        [Header("정찰 이동 설정")]
        public float roamStoppingDistance = 0.5f;
        public float roamReachedThreshold = 1.5f;
        public float roamWanderRadius = 30f;

        [Header("자기장 대응 설정")]
        [Tooltip("자기장 위급도(0~1)가 이 값 근처에 오면 교전 중이어도 확률적으로 자기장 이동을 우선한다")]
        public float zoneUrgencyThreshold = 0.5f;
        [Tooltip("임계치 위아래로 확률이 부드럽게 변하는 폭 (하드 컷오프 방지)")]
        public float zoneUrgencyBand = 0.15f;

        [Header("정지사격 우선 설정")]
        [Tooltip("교전 중 자세 전환 시점마다 '정지하고 쏘기'를 고를 확률 (나머지는 스트레이프)")]
        public float plantChance = 0.6f;
        public float plantMinDuration = 0.4f;
        public float plantMaxDuration = 1.0f;
        public float strafeBurstMinDuration = 0.3f;
        public float strafeBurstMaxDuration = 0.7f;

        [Header("피격 반응 설정")]
        public float coverSearchRadius = 15f;
        public float takeCoverMinDuration = 1.0f;
        public float takeCoverMaxDuration = 1.8f;
        public float retreatMinDuration = 2.0f;
        public float retreatMaxDuration = 3.5f;
        public float peekMinDuration = 0.5f;
        public float peekMaxDuration = 0.9f;

        private PlayerHealth health;
        private PlayerMovement movement;
        private WeaponController weapon;
        private MovementStepSelector movementSelector;
        private PlayerCombatStats stats;

        private PlayerHealth currentTarget;
        private Vector3 roamPoint;

        // 매 프레임 리스트를 새로 할당하지 않도록 재사용하는 버퍼 (80인 규모 GC 압박 완화)
        private readonly List<PlayerHealth> nearbyEnemiesBuffer = new List<PlayerHealth>();
        private float nextDetectTime;

        // 정지사격 <-> 스트레이프 자세 전환
        private bool isPlanted;
        private float nextStanceChangeTime;

        // 피격 반응 상태
        private CombatReaction currentReaction = CombatReaction.Fight;
        private Vector3 reactionDestination;
        private float reactionStopDistance = 1.0f;
        private float reactionUntilTime;

        void Awake()
        {
            health = GetComponent<PlayerHealth>();
            movement = GetComponent<PlayerMovement>();
            weapon = GetComponent<WeaponController>();

            movementSelector = GetComponent<MovementStepSelector>();
            if (movementSelector == null) movementSelector = gameObject.AddComponent<MovementStepSelector>();

            stats = GetComponent<PlayerCombatStats>();
            if (stats == null) stats = gameObject.AddComponent<PlayerCombatStats>();

            health.OnDamaged += HandleDamaged;

            // 80체 전부가 같은 프레임에 탐지 스캔을 몰아서 돌리지 않도록 초기 시점을 흩뿌린다.
            nextDetectTime = Time.time + Random.Range(0f, detectInterval);
        }

        void Update()
        {
            if (health.IsDead)
            {
                weapon.triggerPressed = false;
                return;
            }

            if (Time.time < reactionUntilTime)
            {
                TickReaction();
                return;
            }

            float urgency = ComputeZoneUrgency();
            if (ShouldForceZoneRetreat(urgency))
            {
                MoveTowardZone();
                return;
            }

            if (Time.time >= nextDetectTime)
            {
                currentTarget = FindNearestVisibleEnemy();
                nextDetectTime = Time.time + detectInterval;
            }
            else if (currentTarget != null && currentTarget.IsDead)
            {
                currentTarget = null;
            }

            if (currentTarget != null)
            {
                Engage(currentTarget);
            }
            else
            {
                Search();
            }
        }

        // ---- 자기장(세이프존) 대응 ----

        // 0~1: 자기장 밖에 있을 때 얼마나 위급한지. 체력이 낮을수록, 클러치가 낮을수록,
        // 이번 단계 데미지가 셀수록 빠르게 치솟는다. 원 안이면 항상 0.
        float ComputeZoneUrgency()
        {
            if (ZoneManager.Instance == null) return 0f;
            if (ZoneManager.Instance.IsInsideZone(transform.position)) return 0f;

            float healthRatio = Mathf.Clamp01(health.CurrentHealth / health.maxHealth);
            float dpsRatio = ZoneManager.Instance.CurrentDamagePerSecond / health.maxHealth;
            float tolerance = Mathf.Lerp(0.15f, 0.45f, stats.clutch);

            return Mathf.Clamp01(dpsRatio / Mathf.Max(tolerance, 0.01f) - (healthRatio - 0.5f));
        }

        // 임계치 근처에서 확률적으로 갈리도록 - 하드 컷오프 대신 부드러운 램프
        bool ShouldForceZoneRetreat(float urgency)
        {
            if (urgency <= 0f) return false;

            float rampStart = zoneUrgencyThreshold - zoneUrgencyBand;
            float rampEnd = zoneUrgencyThreshold + zoneUrgencyBand;
            float chance = Mathf.Clamp01(Mathf.InverseLerp(rampStart, rampEnd, urgency));

            return Random.value < chance;
        }

        void MoveTowardZone()
        {
            weapon.triggerPressed = false;
            movement.FaceMoveDirection();
            movementSelector.SetMovementAllowed(true);

            Vector3 target = ZoneManager.Instance != null ? ZoneManager.Instance.CurrentCenter : transform.position;
            movementSelector.TickTowards(target, roamStoppingDistance, repathInterval);
        }

        // ---- 피격 반응 결정 ----

        void HandleDamaged(PlayerHealth victim, PlayerHealth attacker, bool isHeadshot, float amount)
        {
            bool attackerKnown = attacker != null && !attacker.IsDead && HasLineOfSight(attacker);
            Transform nearestCover = FindNearestCover();

            var ctx = new CombatReactionEvaluator.Context
            {
                healthPct = Mathf.Clamp01(health.CurrentHealth / health.maxHealth),
                attackerKnown = attackerKnown,
                coverAvailable = nearestCover != null,
                coverDistance = nearestCover != null ? Vector3.Distance(transform.position, nearestCover.position) : 999f,
                clutch = stats.clutch,
                positioning = stats.positioning,
                nearbyEnemyCount = 1, // 1v1 프로토타입 단순화. 5v5 확장 시 근처 교전 인원 카운트로 교체.
                zoneUrgency = ComputeZoneUrgency()
            };

            CombatReaction reaction = CombatReactionEvaluator.Evaluate(ctx);
            currentReaction = reaction;

            if (attackerKnown)
            {
                currentTarget = attacker;
            }

            switch (reaction)
            {
                case CombatReaction.Fight:
                    reactionUntilTime = Time.time; // 즉시 일반 교전 루프로 복귀해 반격
                    break;

                case CombatReaction.TakeCover:
                    reactionDestination = nearestCover != null ? nearestCover.position : FallbackRetreatPoint(attacker);
                    reactionStopDistance = 1.0f;
                    reactionUntilTime = Time.time + Random.Range(takeCoverMinDuration, takeCoverMaxDuration);
                    break;

                case CombatReaction.Retreat:
                    reactionDestination = GetRetreatPoint();
                    reactionStopDistance = roamStoppingDistance;
                    reactionUntilTime = Time.time + Random.Range(retreatMinDuration, retreatMaxDuration);
                    break;

                case CombatReaction.PeekAndFight:
                    reactionDestination = nearestCover != null ? nearestCover.position : FallbackRetreatPoint(attacker);
                    reactionStopDistance = 1.0f;
                    reactionUntilTime = Time.time + Random.Range(peekMinDuration, peekMaxDuration);
                    break;
            }
        }

        void TickReaction()
        {
            // Fight는 reactionUntilTime을 즉시 과거로 설정해두므로 이 분기까지 오지 않는다(안전장치로만 유지).
            if (currentReaction == CombatReaction.Fight) return;

            weapon.triggerPressed = false;
            movementSelector.SetMovementAllowed(true);
            movement.FaceMoveDirection();
            movementSelector.TickTowards(reactionDestination, reactionStopDistance, repathInterval);
        }

        Vector3 FallbackRetreatPoint(PlayerHealth attacker)
        {
            Vector3 away = attacker != null ? (transform.position - attacker.transform.position).normalized : -transform.forward;
            return transform.position + away * 6f;
        }

        Vector3 GetRetreatPoint()
        {
            // 자기장이 위급하면 팀 스폰 방향이 아니라 자기장 중심 쪽으로 후퇴한다.
            if (ZoneManager.Instance != null && ComputeZoneUrgency() >= zoneUrgencyThreshold)
            {
                return ZoneManager.Instance.CurrentCenter;
            }

            if (MatchManager.Instance == null) return transform.position;

            Vector3 center = MatchManager.Instance.GetTeamSpawnCenter(health.teamId);
            return center + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
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

        // ---- 평상시 교전/정찰 ----

        void Engage(PlayerHealth target)
        {
            Vector3 aimPoint = target.transform.position + Vector3.up * 1.5f;
            movement.AimAt(aimPoint);

            UpdateCombatStance();

            if (isPlanted)
            {
                // 원칙: 사격 중엔 웬만하면 정지. moveBlendSpeed에 의해 서서히 감속하므로 뚝 끊기지 않는다.
                movementSelector.SetMovementAllowed(false);
            }
            else
            {
                movementSelector.SetMovementAllowed(true);
                movementSelector.TickCombatStrafe(target.transform, repathInterval);
            }

            weapon.triggerPressed = true;
        }

        void UpdateCombatStance()
        {
            if (Time.time < nextStanceChangeTime) return;

            isPlanted = Random.value < plantChance;
            float duration = isPlanted
                ? Random.Range(plantMinDuration, plantMaxDuration)
                : Random.Range(strafeBurstMinDuration, strafeBurstMaxDuration);
            nextStanceChangeTime = Time.time + duration;
        }

        void Search()
        {
            weapon.triggerPressed = false;
            movement.FaceMoveDirection();
            movementSelector.SetMovementAllowed(true);

            if (roamPoint == Vector3.zero || Vector3.Distance(transform.position, roamPoint) < roamReachedThreshold)
            {
                Vector3 origin = transform.position;
                float radius = roamWanderRadius;

                // 로밍 목표도 자기장 안쪽에서만 뽑히도록 제한: 이미 밖이면 중심 근처에서, 안이면 반경을 현재 자기장 크기로 제한
                if (ZoneManager.Instance != null)
                {
                    if (!ZoneManager.Instance.IsInsideZone(origin))
                        origin = ZoneManager.Instance.CurrentCenter;

                    radius = Mathf.Min(radius, ZoneManager.Instance.CurrentRadius);
                }

                roamPoint = MatchManager.Instance != null
                    ? MatchManager.Instance.GetWanderPoint(origin, radius)
                    : transform.position;
            }
            movementSelector.TickTowards(roamPoint, roamStoppingDistance, repathInterval);
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
            return true; // 가로막는 것이 없으면 시야 확보
        }

        PlayerHealth FindNearestVisibleEnemy()
        {
            if (MatchManager.Instance == null) return null;

            // 전체 적 리스트 대신 자기 주변 그리드 셀의 후보만 조회 (80인 규모 성능 대응)
            MatchManager.Instance.GetNearbyEnemies(transform.position, health.teamId, detectRadius, nearbyEnemiesBuffer);

            PlayerHealth best = null;
            float bestDist = detectRadius;

            foreach (var enemy in nearbyEnemiesBuffer)
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
    }
}
