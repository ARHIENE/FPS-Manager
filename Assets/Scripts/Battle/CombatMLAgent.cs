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
        // MaxStep(3000)까지 타임아웃되면 stepPenalty가 누적돼 -1.5가 됐었음 - deathPenalty(-1)보다 나빠서
        // "죽는 게 오히려 덜 나쁜 선택"이 되는 역유인이 있었음(v5에서 발견). 3000스텝 누적 상한이 deathPenalty보다
        // 확실히 약하도록(-0.9) 축소해 타임아웃 회피 목적의 자살 유인을 제거.
        public float stepPenalty = -0.0003f;
        [Tooltip("데미지 보상과 별개로, 헤드샷이면 추가로 붙는 보너스 - 헤드샷 비중을 늘리기 위함")]
        public float headshotBonus = 0.3f;
        [Tooltip("사격했는데 빗맞았을 때 페널티 - v4까지는 이게 없어서 정렬 상태와 무관하게 난사해도 손해가 없었음(명중률 저조의 핵심 원인으로 추정)")]
        // v5 첫 시도에서 -0.02는 너무 강해서, 학습 초반 명중률이 0에 가까울 때 "쏘는 행동" 자체의
        // 기대값이 거의 항상 마이너스가 되어 정책이 아예 발사를 포기해버림(교전 회피로 보상만 챙기는 현상 관찰).
        // 난사보다 조준이 낫다는 신호는 유지하되, 사격 자체를 지워버리지 않을 만큼 약화.
        public float missPenalty = -0.003f;

        [Header("조준 정확도 보상 (명중률이 너무 낮은 문제 해결용)")]
        [Tooltip("적을 보고 있을 때, 조준 방향이 적과 얼마나 정렬됐는지에 비례해 매 스텝 주는 보상 - 맞혀야만 보상받던 기존 방식은 신호가 너무 희소해서 조준을 못 배웠음")]
        // v3에서 이 값이 너무 커서(0.003) 가만히 쳐다보기만 해도 한 에피소드 동안 킬 보상(+1)에
        // 맞먹는 보상이 누적되는 reward hacking이 발생 - 명중/킬 보상이 항상 우세하도록 10배 축소.
        // v5에서 missPenalty 도입과 함께 추가로 절반 축소 - "쳐다보기만" 해도 받는 보상 비중을 더 낮추고
        // "정확히 쏴서 맞추기" 쪽으로 신호를 더 쏠리게 함.
        public float aimRewardScale = 0.00015f;
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
        [Tooltip("이 거리보다 가까이 붙으면 페널티가 시작됨 - 설치/해체 판정 범위가 좁아서(interactRadius) 양팀이 그냥 밀착해서 쏘는 문제 방지용")]
        public float tooCloseDistance = 3f;
        public float tooClosePenaltyScale = 0.0003f;

        [Header("탐색 보상 (적을 못 찾고 벽에 붙어 정체되는 문제 해결용)")]
        [Tooltip("적이 안 보일 때는 이동에 대한 보상이 전혀 없어서, 가만히 있으나 벽에 막혀있으나 학습 입장에서 손해가 똑같아 정체 현상이 발생함(v5 학습 중 관측). 실제 이동 속도에 비례한 보상을 줘서 최소한 돌아다닐 유인을 만듦.")]
        public float explorationRewardScale = 0.0002f;

        [Header("설치/해체 보상 (v7 - 숨기만 하는 문제 해결용, 라운드 타임리밋으로 강제 교전 유도)")]
        [Tooltip("사이트에서 채널링(설치/해체) 진행 중 매 스텝 주는 작은 보상 - 끝까지 버티게 유인")]
        public float channelProgressRewardScale = 0.01f;
        [Tooltip("설치 성공 보너스 (공격팀)")]
        public float plantReward = 0.6f;
        [Tooltip("해체 성공 보너스 (수비팀)")]
        public float defuseReward = 0.6f;
        [Tooltip("라운드 승리 시 생존자 전원에게 주는 보너스 - 개인 킬뿐 아니라 팀 목표(설치/해체/타임아웃) 달성을 학습시키기 위함")]
        public float roundWinReward = 0.5f;
        [Tooltip("라운드 패배 시 생존자 전원에게 주는 페널티")]
        public float roundLossPenalty = -0.5f;

        private float channelProgress;
        private bool isChanneling;

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
            weapon.OnShotFired += HandleShotFired;
        }

        public override void OnEpisodeBegin()
        {
            currentTarget = null;
            channelProgress = 0f;
            isChanneling = false;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (health == null || health.IsDead)
            {
                // 죽은 뒤에도 한 스텝 정도 관측이 요청될 수 있어 0으로 채워 크기를 맞춘다.
                for (int i = 0; i < 24; i++) sensor.AddObservation(0f);
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

            // 설치/해체 목표(사이트) 관측 - 사이트가 하나뿐이라 공격/수비 모두 라운드 내내 이 지점이
            // 목표점이다(설치 전엔 가서 심어야 하고, 설치 후엔 지키거나 해체하러 가야 함).
            var mm = MatchManager.Instance;
            if (mm != null)
            {
                bool isAttacker = health.teamId == mm.attackerTeamId;
                sensor.AddObservation(isAttacker ? 1f : 0f);
                sensor.AddObservation(mm.CurrentBombPhase == MatchManager.BombPhase.Planted ? 1f : 0f);

                // 설치 전엔 사이트 중심, 설치 후엔 실제로 설치된 지점이 목표점 - 수비팀은 폭탄을
                // 찾아가야 하고, 공격팀도 설치 후엔 그 자리를 지키러 돌아가야 하므로 둘 다 유효하다.
                Vector3 objectivePoint = mm.CurrentBombPhase == MatchManager.BombPhase.Planted
                    ? mm.PlantedBombPosition
                    : mm.bombSiteWorldPosition;
                Vector3 toSite = objectivePoint - transform.position;
                Vector3 siteDir = transform.InverseTransformDirection(toSite.normalized);
                sensor.AddObservation(siteDir.x);
                sensor.AddObservation(siteDir.z);
                sensor.AddObservation(Mathf.Clamp01(toSite.magnitude / detectRadius));

                float holdTime = isAttacker ? mm.plantHoldTime : mm.defuseHoldTime;
                float timePressure = mm.CurrentBombPhase == MatchManager.BombPhase.Planted
                    ? Mathf.Clamp01(mm.BombTimeRemaining / Mathf.Max(mm.bombFuseTime, 0.01f))
                    : Mathf.Clamp01(mm.RoundTimeRemaining / Mathf.Max(mm.roundTimeLimit, 0.01f));
                sensor.AddObservation(timePressure);
                sensor.AddObservation(holdTime > 0f ? Mathf.Clamp01(channelProgress / holdTime) : 0f);
            }
            else
            {
                for (int i = 0; i < 7; i++) sensor.AddObservation(0f);
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
            bool wantsInteract = actions.DiscreteActions[1] == 1;

            ApplyRotation(yawDelta, pitchDelta);
            ApplyMovement(moveForward, moveStrafe);
            ApplyDistanceReward();
            ApplyAimReward();
            ApplyCoverReward();
            ApplyExplorationReward();
            UpdateObjectiveProgress(wantsInteract);

            // 적이 보일 때만 실제로 발사되도록 하드 게이팅 - "안 보이는데도 난사" 문제를
            // 보상만으로 학습시키는 대신 구조적으로 원천 차단한다(AIBrain도 동일하게 LOS로 발사를 게이팅함).
            // 설치/해체 채널링 중에는(실제 CS/Valorant처럼) 사격 불가.
            weapon.triggerPressed = wantsFire && currentTarget != null && !isChanneling;
        }

        // 공격팀은 설치 전에 "사이트(넓은 구역) 안 아무 데서나" 설치 가능 - 위치를 자유롭게 고를 수 있다.
        // 수비팀은 설치 후에 "실제로 설치된 그 지점 근처(좁은 판정 범위)"에서만 해체 가능 - 사이트를
        // 통째로 알아도 정확한 지점까지 와서 싸워야 한다. 조건이 깨지면(범위 밖으로 나가거나 손을 떼면)
        // 진행도가 리셋된다 - 실제 CS/Valorant의 "채널링 중단" 규칙과 동일.
        void UpdateObjectiveProgress(bool wantsInteract)
        {
            var mm = MatchManager.Instance;
            if (mm == null) { channelProgress = 0f; isChanneling = false; return; }

            bool isAttacker = health.teamId == mm.attackerTeamId;
            bool validPhase = isAttacker
                ? mm.CurrentBombPhase == MatchManager.BombPhase.NotPlanted
                : mm.CurrentBombPhase == MatchManager.BombPhase.Planted;
            bool inSite = isAttacker
                ? Vector3.Distance(transform.position, mm.bombSiteWorldPosition) <= mm.bombSiteRadius
                : Vector3.Distance(transform.position, mm.PlantedBombPosition) <= mm.interactRadius;

            isChanneling = wantsInteract && inSite && validPhase;
            if (!isChanneling)
            {
                channelProgress = 0f;
                return;
            }

            channelProgress += Time.fixedDeltaTime;
            AddReward(channelProgressRewardScale);

            float holdTime = isAttacker ? mm.plantHoldTime : mm.defuseHoldTime;
            if (channelProgress < holdTime) return;

            channelProgress = 0f;
            bool success = isAttacker ? mm.PlantBomb(transform.position) : mm.DefuseBomb();
            if (success) AddReward(isAttacker ? plantReward : defuseReward);
        }

        // 라운드가 킬이 아니라 타임아웃/설치/해체로 끝났을 때, 그 시점에 살아있던 에이전트들은
        // HandleDeath를 못 거치므로 별도로 종료 보상 + EndEpisode를 줘야 한다(MatchManager가 호출).
        public void OnRoundResolved(bool won)
        {
            AddReward(won ? roundWinReward : roundLossPenalty);
            if (weapon != null) weapon.triggerPressed = false;
            EndEpisode();
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

            // 설치/해체 판정 범위(interactRadius)가 좁아서 양팀이 그 안에서 밀착한 채로 쏘는 문제가
            // 있었음(실측 확인 - 명중률이 오른 이유가 실력이 아니라 그냥 근접사격이었음). 위의 선호거리
            // 보상은 붙어도 0일 뿐 페널티가 없어서 억제력이 부족했음 - 너무 가까우면 명시적으로 깎는다.
            if (dist < tooCloseDistance)
            {
                float closeness = Mathf.Clamp01(1f - dist / tooCloseDistance);
                AddReward(-closeness * tooClosePenaltyScale);
            }
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

        // 적이 안 보일 때, 실제로 움직이고 있으면 보상 - 가만히 있거나 벽에 막혀 정체된 상태를 벗어나
        // 돌아다니며 적을 찾도록 유도한다(정지 상태와 손해가 똑같으면 탐색할 이유가 없어짐).
        void ApplyExplorationReward()
        {
            if (currentTarget != null || agent == null || !agent.isOnNavMesh) return;

            float speedRatio = Mathf.Clamp01(agent.velocity.magnitude / Mathf.Max(moveSpeed, 0.01f));
            AddReward(speedRatio * explorationRewardScale);
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

            float moveDist = moveDir.magnitude * moveSpeed * Time.fixedDeltaTime;
            if (moveDist > 0.0001f) moveDir = ApplyWallSlide(moveDir, moveDist);

            // NavMeshAgent를 경로탐색이 아니라 직접 위치 구동 방식으로 사용
            // (nextPosition을 매 스텝 갱신하면 NavMesh 표면에 붙은 채로 커스텀 이동이 가능)
            Vector3 desired = agent.nextPosition + moveDir * moveSpeed * Time.fixedDeltaTime;
            agent.nextPosition = desired;
        }

        // direct-position-drive 방식이라 NavMeshAgent 자체 충돌회피가 작동하지 않아서, 정책이 회전을
        // 학습하기 전까지 장애물(엄폐물 등)에 그대로 눌려 못 움직이는 문제가 있었음(실측 확인 - 사용자가
        // 화면에서 "장애물에 비비면서 서로를 못 찾는다"고 확인). 이동하려는 방향에 장애물이 있으면 파고드는
        // 성분만 제거하고 벽을 따라 미끄러지는 성분은 남긴다 - 엄폐물 뒤에 가만히 서서 쏘는 전술적 정지는
        // 애초에 이동 명령이 0에 가까워서 이 로직이 개입하지 않으므로 영향 없음. 다른 플레이어(적/아군)는
        // 제외해서 근접 교전 시 서로를 밀어내지 않게 한다.
        Vector3 ApplyWallSlide(Vector3 moveDir, float moveDist)
        {
            const float castRadius = 0.3f;
            Vector3 origin = transform.position + Vector3.up * 1f;
            RaycastHit[] hits = Physics.SphereCastAll(origin, castRadius, moveDir.normalized, moveDist, losBlockMask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider.isTrigger) continue;
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<PlayerHealth>() != null) continue;

                // 접선 성분만 남기고 끝내면, 보정된 위치가 NavMesh 경계에 다시 딱 붙어서 다음 프레임에
                // 똑같은 충돌이 재감지되는 피드백 루프가 생겨 제자리에서 떠는 문제가 있었음(실측 확인 -
                // 사용자가 화면에서 "미세하게 떨림" 확인). 표면에서 살짝 밀어내는 성분을 더해 재충돌을 막는다.
                // 거의 정면충돌(접선이 거의 0)이면 미세한 접선 방향으로 계속 떠는 대신 아예 멈춘다.
                Vector3 tangent = Vector3.ProjectOnPlane(moveDir, hit.normal);
                if (tangent.sqrMagnitude < 0.0025f) return Vector3.zero;

                const float pushOut = 0.05f;
                return tangent + hit.normal * pushOut;
            }
            return moveDir;
        }

        void HandleDamaged(PlayerHealth victim, PlayerHealth attacker, bool isHeadshot, float amount)
        {
            AddReward(-amount * damageTakenPenaltyScale);

            // 실제 CS/Valorant처럼 설치/해체 채널링 중 피격당하면 진행도가 끊긴다 - 이게 없으면
            // 사이트 위치를 양팀 다 처음부터 알고 있는 지금 구조에서, 수비팀이 그냥 뛰어가서 총알
            // 맞아가며 버티기만 해도 해체가 되는 문제가 있었음(실측: 사용자가 화면에서 확인).
            // 이 규칙을 넣어야 "설치 후 지키기"(공격) / "해체 전에 사살"(수비)이 실제로 유인된다.
            if (isChanneling) channelProgress = 0f;

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

        // 사격했는데 빗맞았을 때 페널티 - 명중(HandleDamaged)은 이미 별도로 보상받으므로 여기선 miss만 처리.
        void HandleShotFired(bool hit)
        {
            if (!hit) AddReward(missPenalty);
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
            discrete[1] = 0;
        }
    }
}
