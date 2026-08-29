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
        public float detectRadius = 40f;
        public float engageRange = 24f;
        public float repathInterval = 0.25f;
        public LayerMask losBlockMask = ~0;

        [Header("정찰 이동 설정")]
        public float roamStoppingDistance = 0.5f;
        public float roamReachedThreshold = 1.5f;

        [Header("정지사격 우선 설정")]
        [Tooltip("교전 중 자세 전환 시점마다 '정지하고 쏘기'를 고를 확률 (나머지는 스트레이프)")]
        public float plantChance = 0.6f;
        public float plantMinDuration = 0.4f;
        public float plantMaxDuration = 1.0f;
        public float strafeBurstMinDuration = 0.3f;
        public float strafeBurstMaxDuration = 0.7f;

        [Header("교전 중 엄폐 활용 설정")]
        [Tooltip("교전 중 자세 전환 시점에 근처 엄폐물로 붙는 것을 고를 확률")]
        public float coverModeChance = 0.35f;
        public float combatCoverRadius = 10f;

        [Header("타겟 기억 설정")]
        [Tooltip("스트레이프 중 순간적으로 시야가 끊겨도 이 시간 동안은 마지막 위치를 계속 조준(홱 돌아보는 현상 방지)")]
        public float targetMemoryDuration = 0.6f;

        [Header("정찰 경로 다양화 설정")]
        [Tooltip("정찰 중 다음 목적지로 엄폐물을 고를 확률 (나머지는 기존처럼 적 진영 방향)")]
        public float coverPatrolChance = 0.6f;

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
        private float lastSeenTime = -999f;
        private Vector3 roamPoint;

        private enum CombatMoveMode { Planted, Strafe, Cover }

        // 정지사격 <-> 스트레이프 <-> 엄폐 이동 전환
        private CombatMoveMode moveMode = CombatMoveMode.Strafe;
        private Transform combatCoverTarget;
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

            PlayerHealth visibleEnemy = FindNearestVisibleEnemy();
            if (visibleEnemy != null)
            {
                currentTarget = visibleEnemy;
                lastSeenTime = Time.time;
                Engage(currentTarget, canFire: true);
            }
            else if (currentTarget != null && !currentTarget.IsDead && Time.time - lastSeenTime < targetMemoryDuration)
            {
                // 스트레이프/엄폐 이동 중 순간적으로 시야가 끊겨도 곧바로 이동 방향을 보지 않고,
                // 잠깐은 마지막으로 본 위치를 계속 조준한다 (홱 돌아보는 현상 방지)
                Engage(currentTarget, canFire: false);
            }
            else
            {
                currentTarget = null;
                Search();
            }
        }

        // ---- 피격 반응 결정 ----

        void HandleDamaged(PlayerHealth victim, PlayerHealth attacker, bool isHeadshot, float amount)
        {
            bool attackerKnown = attacker != null && !attacker.IsDead && HasLineOfSight(attacker);
            Transform nearestCover = FindNearestCover(coverSearchRadius);

            var ctx = new CombatReactionEvaluator.Context
            {
                healthPct = Mathf.Clamp01(health.CurrentHealth / health.maxHealth),
                attackerKnown = attackerKnown,
                coverAvailable = nearestCover != null,
                coverDistance = nearestCover != null ? Vector3.Distance(transform.position, nearestCover.position) : 999f,
                clutch = stats.clutch,
                positioning = stats.positioning,
                nearbyEnemyCount = 1 // 1v1 프로토타입 단순화. 5v5 확장 시 근처 교전 인원 카운트로 교체.
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
            if (MatchManager.Instance == null) return transform.position;

            Transform[] ownSpawns = health.teamId == 0 ? MatchManager.Instance.teamASpawns : MatchManager.Instance.teamBSpawns;
            if (ownSpawns != null && ownSpawns.Length > 0)
            {
                Transform t = ownSpawns[Random.Range(0, ownSpawns.Length)];
                if (t != null) return t.position + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
            }
            return transform.position;
        }

        Transform FindNearestCover(float searchRadius)
        {
            GameObject[] covers = GameObject.FindGameObjectsWithTag("Cover");
            Transform best = null;
            float bestDist = searchRadius;

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

        void Engage(PlayerHealth target, bool canFire)
        {
            // 몸/조준은 이동 모드와 무관하게 항상 타겟을 향한다 (스트레이프/엄폐 이동 중에도 적을 계속 봄)
            Vector3 aimPoint = target.transform.position + Vector3.up * 1.5f;
            movement.AimAt(aimPoint);

            UpdateCombatStance();

            switch (moveMode)
            {
                case CombatMoveMode.Planted:
                    // 원칙: 사격 중엔 웬만하면 정지. moveBlendSpeed에 의해 서서히 감속하므로 뚝 끊기지 않는다.
                    movementSelector.SetMovementAllowed(false);
                    break;

                case CombatMoveMode.Cover:
                    movementSelector.SetMovementAllowed(true);
                    if (combatCoverTarget != null)
                        movementSelector.TickTowards(combatCoverTarget.position, 1.2f, repathInterval);
                    else
                        movementSelector.TickCombatStrafe(target.transform, repathInterval);
                    break;

                default:
                    movementSelector.SetMovementAllowed(true);
                    movementSelector.TickCombatStrafe(target.transform, repathInterval);
                    break;
            }

            weapon.triggerPressed = canFire;
        }

        void UpdateCombatStance()
        {
            if (Time.time < nextStanceChangeTime) return;

            Transform cover = FindNearestCover(combatCoverRadius);
            bool alreadyAtCover = cover != null && Vector3.Distance(transform.position, cover.position) < 2.5f;
            if (cover != null && !alreadyAtCover && Random.value < coverModeChance)
            {
                moveMode = CombatMoveMode.Cover;
                combatCoverTarget = cover;
            }
            else
            {
                moveMode = Random.value < plantChance ? CombatMoveMode.Planted : CombatMoveMode.Strafe;
            }

            float duration = moveMode == CombatMoveMode.Planted
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
                roamPoint = PickNextRoamPoint();
            }
            movementSelector.TickTowards(roamPoint, roamStoppingDistance, repathInterval);
        }

        // 정찰 목적지: 확률적으로 엄폐물 사이를 옮겨 다니듯 순찰, 나머지는 기존처럼 적 진영 방향으로 전진
        Vector3 PickNextRoamPoint()
        {
            if (Random.value < coverPatrolChance)
            {
                GameObject[] covers = GameObject.FindGameObjectsWithTag("Cover");
                if (covers.Length > 0)
                {
                    return covers[Random.Range(0, covers.Length)].transform.position;
                }
            }
            return MatchManager.Instance != null
                ? MatchManager.Instance.GetRoamPoint(health.teamId)
                : transform.position;
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

            List<PlayerHealth> enemies = MatchManager.Instance.GetEnemies(health.teamId);
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
    }
}
