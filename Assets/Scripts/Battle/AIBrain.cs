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
                roamPoint = MatchManager.Instance != null
                    ? MatchManager.Instance.GetRoamPoint(health.teamId)
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
