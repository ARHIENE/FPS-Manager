using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSManager.Battle
{
    // 5v5 스폰, 팀 생존자 집계, 라운드 종료 및 승패 관리
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("스폰 프리팹 설정")]
        public GameObject playerPrefab;
        public GameObject teamAPrefab;
        public GameObject teamBPrefab;

        [Header("스폰 지점")]
        public Transform[] teamASpawns;
        public Transform[] teamBSpawns;

        [Header("팀 색상")]
        public Color teamAColor = new Color(0.2f, 0.6f, 1f);
        public Color teamBColor = new Color(1f, 0.35f, 0.2f);

        [Header("라운드 설정")]
        public float roundRestartDelay = 4.0f;
        public bool autoNextRound = true;

        [Header("설치/해체 모드 (v7 - CS/Valorant 스타일)")]
        [Tooltip("설치(공격) 팀 ID - 0=Blue, 1=Red. 나머지 팀이 자동으로 해체(수비) 팀이 된다.")]
        public int attackerTeamId = 0;
        [Tooltip("설치 전까지 주어지는 라운드 제한시간(초) - 이 안에 설치 못 하면 수비팀 승리(실제 CS/Valorant 규칙). 숨기만 하면 지는 강제 장치.")]
        // 맵이 2배로 커지면서(스폰 간 거리도 약 2배) 공격팀이 사이트까지 가는 데 걸리는 시간도 늘어남 -
        // 그만큼 라운드 제한시간도 비례해서(약 1.8배) 늘림.
        public float roundTimeLimit = 180f;
        [Tooltip("설치 성공 후 폭발까지 남은 시간(초)")]
        public float bombFuseTime = 35f;
        [Tooltip("설치에 필요한 채널링(제자리에서 버티기) 시간(초)")]
        public float plantHoldTime = 3f;
        [Tooltip("해체에 필요한 채널링 시간(초)")]
        public float defuseHoldTime = 6f;
        [Tooltip("사이트 반경 - 설치는 이 구역 안 아무 데서나 가능(공격팀이 위치를 자유롭게 고를 수 있음)")]
        public float bombSiteRadius = 5f;
        [Tooltip("해체 판정 반경 - 사이트 중심이 아니라 '실제로 폭탄이 설치된 그 지점' 기준. 좁게 둬서 수비팀이 정확히 그 자리까지 와야만 해체를 시작할 수 있게 한다(실측 확인: 안 그러면 공격팀과 안 마주치고 해체가 끝나버림).")]
        public float interactRadius = 3.5f;
        [Tooltip("사이트 월드 좌표(중심) - 수비팀 진영 쪽에 위치. 공격팀 스폰은 이 지점을 원점 기준으로 정반대 대칭시킨 위치에 자동 계산됨(맵을 키워도 따로 안 맞춰줘도 됨).")]
        public Vector3 bombSiteWorldPosition = new Vector3(0f, 0f, 20f);
        [Tooltip("실제로 폭탄이 설치된 위치 - 설치 순간 공격팀이 서 있던 자리로 기록된다. 설치 후엔 이 위치가 해체 판정 기준점이자 관측상 목표점이 된다.")]
        public Vector3 PlantedBombPosition { get; private set; }

        public enum BombPhase { NotPlanted, Planted, Exploded, Defused }
        public BombPhase CurrentBombPhase { get; private set; } = BombPhase.NotPlanted;
        public float RoundTimeRemaining { get; private set; }
        public float BombTimeRemaining { get; private set; }

        public event Action OnBombPlanted;
        public event Action OnBombDefused;
        public event Action OnBombExploded;

        private GameObject bombSiteMarker;
        private GameObject interactRangeMarker;

        public int ScoreTeamA { get; private set; }
        public int ScoreTeamB { get; private set; }
        public int CurrentRound { get; private set; } = 1;
        public bool IsRoundEnded { get; private set; }

        private readonly List<PlayerHealth> teamA = new List<PlayerHealth>();
        private readonly List<PlayerHealth> teamB = new List<PlayerHealth>();
        private readonly List<GameObject> spawnedPlayers = new List<GameObject>();

        public event Action<int, int> OnScoreChanged;
        public event Action<int, int> OnAliveCountChanged;
        public event Action<string, Color, bool> OnKillFeedEntry;
        public event Action<string, Color> OnRoundFinished;
        public event Action OnMatchStarted;

        void Awake()
        {
            Instance = this;
        }

        [Header("명중률 로그 (ML-Agents 학습 모니터링용)")]
        [Tooltip("라운드가 끝나야만(팀 전멸) 명중률을 로그로 남기는데, 학습 중에는 라운드가 사실상 끝나지 않아서(root cause: 개별 사망 후 라운드 단위로만 리스폰) 이 로그가 전혀 안 찍힘. 라운드 상태와 무관하게 주기적으로 남겨서 학습 중에도 grep으로 추이를 볼 수 있게 한다.")]
        public float accuracyLogIntervalSec = 30f;
        private float accuracyLogTimer;

        void Update()
        {
            // Keyboard shortcut R or Space to restart / next round
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame || (IsRoundEnded && Keyboard.current.spaceKey.wasPressedThisFrame))
                {
                    RestartMatch();
                }
            }

            accuracyLogTimer += Time.unscaledDeltaTime;
            if (accuracyLogTimer >= accuracyLogIntervalSec)
            {
                accuracyLogTimer = 0f;
                if (WeaponController.TotalShotsFired > 0)
                {
                    Debug.Log($"[MatchManager] 누적 명중률: {WeaponController.AccuracyPercent:F1}% ({WeaponController.TotalHits}/{WeaponController.TotalShotsFired}), 헤드샷 비율: {WeaponController.HeadshotPercent:F1}% ({WeaponController.TotalHeadshots}/{WeaponController.TotalHits})");
                }
            }

            UpdateBombTimers();
        }

        // 설치 전엔 라운드 제한시간이 흐르고(못 채우면 수비 승 - 숨기만 하면 지는 핵심 강제 장치),
        // 설치 후엔 그 대신 폭탄 퓨즈 타이머가 흐른다(다 타면 공격 승, 그전에 해체되면 수비 승).
        void UpdateBombTimers()
        {
            if (IsRoundEnded) return;

            if (CurrentBombPhase == BombPhase.NotPlanted)
            {
                RoundTimeRemaining -= Time.deltaTime;
                if (RoundTimeRemaining <= 0f)
                {
                    RoundTimeRemaining = 0f;
                    EndRound(attackerWins: false, reason: "TIME OUT");
                }
            }
            else if (CurrentBombPhase == BombPhase.Planted)
            {
                BombTimeRemaining -= Time.deltaTime;
                if (BombTimeRemaining <= 0f)
                {
                    BombTimeRemaining = 0f;
                    CurrentBombPhase = BombPhase.Exploded;
                    OnBombExploded?.Invoke();
                    EndRound(attackerWins: true, reason: "BOMB EXPLODED");
                }
            }
        }

        // 설치 성공 - CombatMLAgent의 채널링 진행도가 다 찼을 때 공격팀 에이전트가 호출한다.
        // plantPosition은 그 순간 설치한 에이전트가 서 있던 자리 - 사이트 안 어디든 될 수 있고,
        // 그 지점이 곧바로 해체 판정 기준점이 된다.
        public bool PlantBomb(Vector3 plantPosition)
        {
            if (IsRoundEnded || CurrentBombPhase != BombPhase.NotPlanted) return false;
            CurrentBombPhase = BombPhase.Planted;
            BombTimeRemaining = bombFuseTime;
            PlantedBombPosition = plantPosition;
            SpawnInteractRangeMarker();
            OnBombPlanted?.Invoke();
            Debug.Log($"[MatchManager] Bomb planted at {plantPosition}!");
            return true;
        }

        // 해체 성공 - 수비팀 에이전트가 호출한다. 해체는 그 자리에서 바로 라운드 승리로 이어진다.
        public bool DefuseBomb()
        {
            if (IsRoundEnded || CurrentBombPhase != BombPhase.Planted) return false;
            CurrentBombPhase = BombPhase.Defused;
            OnBombDefused?.Invoke();
            Debug.Log("[MatchManager] Bomb defused!");
            EndRound(attackerWins: false, reason: "BOMB DEFUSED");
            return true;
        }

        // ArenaGenerator가 커버 배치 + NavMesh 베이크를 끝낸 뒤 호출한다.
        public void BeginMatch()
        {
            ClearPreviousMatch();
            IsRoundEnded = false;

            CurrentBombPhase = BombPhase.NotPlanted;
            RoundTimeRemaining = roundTimeLimit;
            BombTimeRemaining = 0f;
            PlantedBombPosition = Vector3.zero;
            SpawnBombSiteMarker();

            if (CurrentRound == 1) WeaponController.ResetAccuracyStats();

            bool attackerIsA = attackerTeamId == 0;
            SpawnAttackerOrDefender(0, attackerIsA, teamASpawns, teamAColor, teamA, teamAPrefab != null ? teamAPrefab : playerPrefab);
            SpawnAttackerOrDefender(1, !attackerIsA, teamBSpawns, teamBColor, teamB, teamBPrefab != null ? teamBPrefab : playerPrefab);

            OnScoreChanged?.Invoke(ScoreTeamA, ScoreTeamB);
            OnAliveCountChanged?.Invoke(CountAlive(teamA), CountAlive(teamB));
            OnMatchStarted?.Invoke();

            Debug.Log($"[MatchManager] Round {CurrentRound} Started: Team Blue ({teamA.Count}) vs Team Red ({teamB.Count})");
        }

        // 사이트(설치 가능 구역) 전체를 보여주는 큰 원판 마커 - 라운드 시작 때 한 번, 사이트 중심에 고정.
        void SpawnBombSiteMarker()
        {
            if (bombSiteMarker != null) Destroy(bombSiteMarker);
            if (interactRangeMarker != null) Destroy(interactRangeMarker);

            bombSiteMarker = CreateDiscMarker("BombSiteMarker", bombSiteWorldPosition, bombSiteRadius, 0.02f, new Color(1f, 0.7f, 0.1f, 0.25f));
        }

        // 실제로 폭탄이 설치된 위치에 좁은 해체 판정 범위를 보여주는 마커 - 설치되는 순간에만 생긴다
        // (설치 전엔 사이트 어디든 될 수 있어서 고정된 좁은 지점을 미리 보여줄 수가 없음).
        void SpawnInteractRangeMarker()
        {
            if (interactRangeMarker != null) Destroy(interactRangeMarker);
            interactRangeMarker = CreateDiscMarker("InteractRangeMarker", PlantedBombPosition, interactRadius, 0.04f, new Color(1f, 0.15f, 0.1f, 0.5f));
        }

        GameObject CreateDiscMarker(string markerName, Vector3 center, float radius, float height, Color color)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = markerName;
            Destroy(marker.GetComponent<Collider>());
            marker.transform.position = center + Vector3.up * height;
            marker.transform.localScale = new Vector3(radius * 2f, height, radius * 2f);

            var rend = marker.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            rend.material.color = color;
            return marker;
        }

        void ClearPreviousMatch()
        {
            foreach (var go in spawnedPlayers)
            {
                if (go != null) Destroy(go);
            }
            spawnedPlayers.Clear();
            teamA.Clear();
            teamB.Clear();
        }

        // 수비팀은 사이트(=자기 진영 안, 처음부터 지키는 자세)에서, 공격팀은 맵 반대쪽 끝(사이트를
        // 중심 기준으로 정반대 대칭시킨 위치)에서 시작한다 - 씬에 고정 배치된 Transform 대신 코드로
        // 계산해서, 맵 크기나 사이트 위치가 바뀌어도(맵을 키우는 등) 항상 자동으로 맞는 위치에 뜬다.
        void SpawnAttackerOrDefender(int teamId, bool isAttacker, Transform[] originalSpawns, Color color, List<PlayerHealth> list, GameObject prefabToUse)
        {
            int count = originalSpawns != null && originalSpawns.Length > 0 ? originalSpawns.Length : 5;
            Vector3 center = isAttacker ? -bombSiteWorldPosition : bombSiteWorldPosition;
            var (positions, rotations) = GetSpawnRing(center, count);
            SpawnTeamAt(positions, rotations, teamId, list, prefabToUse);
        }

        // 수비팀은 거점을 처음부터 지키고 있어야 하므로(공격팀이 와서 설치를 못 하게 막는 게 수비팀의
        // 역할) 원래 스폰 지점 대신 사이트 주변에 스폰시킨다 - 공격팀은 기존 먼 스폰 지점 그대로 사용.
        void SpawnTeamAt(Vector3[] positions, Quaternion[] rotations, int teamId, List<PlayerHealth> list, GameObject prefabToUse)
        {
            if (positions == null || prefabToUse == null)
            {
                Debug.LogError($"[MatchManager] Spawns or Prefab missing for Team {teamId}");
                return;
            }

            for (int i = 0; i < positions.Length; i++)
            {
                GameObject go = Instantiate(prefabToUse, positions[i], rotations[i]);
                string teamPrefix = teamId == 0 ? "Blue" : "Red";
                go.name = $"{teamPrefix}_{i + 1}";
                spawnedPlayers.Add(go);

                var health = go.GetComponent<PlayerHealth>();
                if (health != null)
                {
                    health.teamId = teamId;
                    health.OnDeathWithAttacker += HandlePlayerDeath;
                    list.Add(health);
                }
            }
        }

        // 주어진 중심점 둘레로 퍼뜨린 스폰 위치 - 중심을 바깥쪽으로 바라보게 배치한다(수비팀이면
        // 접근로 감시 자세, 공격팀이면 그냥 서로 안 겹치게 퍼지는 정도의 의미).
        (Vector3[], Quaternion[]) GetSpawnRing(Vector3 center, int count)
        {
            Vector3[] positions = new Vector3[count];
            Quaternion[] rotations = new Quaternion[count];
            // 사이트 중앙엔 큰 엄폐물이 항상 하나 있어서(ArenaGenerator) 너무 안쪽에 스폰시키면 그 안에
            // 끼어서 못 움직이는 문제가 생길 수 있음 - 중심 바깥쪽 둘레(가장자리 근처)에 퍼뜨린다.
            float ringRadius = bombSiteRadius * 0.9f;

            for (int i = 0; i < count; i++)
            {
                float angle = i * (360f / count) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle) * ringRadius, 0f, Mathf.Sin(angle) * ringRadius);
                positions[i] = center + offset;
                rotations[i] = offset.sqrMagnitude > 0.01f ? Quaternion.LookRotation(offset.normalized) : Quaternion.identity;
            }
            return (positions, rotations);
        }

        void HandlePlayerDeath(PlayerHealth victim, PlayerHealth killer, bool isHeadshot)
        {
            int aliveA = CountAlive(teamA);
            int aliveB = CountAlive(teamB);
            OnAliveCountChanged?.Invoke(aliveA, aliveB);

            string killerName = killer != null ? killer.name : "System";
            string victimName = victim != null ? victim.name : "Player";
            Color killerColor = killer != null && killer.teamId == 0 ? teamAColor : teamBColor;
            string headshotText = isHeadshot ? " [HEADSHOT]" : "";
            string feedMsg = $"{killerName} eliminated {victimName}{headshotText}";

            OnKillFeedEntry?.Invoke(feedMsg, killerColor, isHeadshot);
            Debug.Log($"[MatchManager] {feedMsg}. Alive - Blue: {aliveA} / Red: {aliveB}");

            if (IsRoundEnded) return;

            // 설치/해체 모드에서는 수비팀이 전멸해도 라운드가 안 끝난다(실제 CS/Valorant 규칙과 동일) -
            // 폭탄이 이미 설치돼 있으면 그 퓨즈 타이머가, 설치 전이면 아무도 못 막으니 공격팀이 알아서
            // 가서 설치하면 되고, 그마저도 안 하면 라운드 타임리밋에 걸려 수비 승. 즉 수비 전멸은
            // 항상 폭탄 상태머신/타이머로 자연히 해소되므로 여기선 공격팀 전멸만 즉시 판정한다
            // (설치 전에 공격팀이 전멸하면 아무도 설치할 사람이 없으니 그 자리에서 수비 승 확정).
            bool attackerIsA = attackerTeamId == 0;
            int aliveAttackers = attackerIsA ? aliveA : aliveB;

            if (aliveAttackers == 0 && CurrentBombPhase == BombPhase.NotPlanted)
            {
                EndRound(attackerWins: false, reason: "ATTACKERS ELIMINATED");
            }
        }

        // 킬(전멸)/타임아웃/폭발/해체 - 어떤 경로로 끝나든 여기 한 곳으로 모아서 점수·이벤트·다음 라운드
        // 진행을 처리한다. attackerWins는 "공격팀이 이겼는지"이고, 실제 팀(Blue/Red) 승패로 변환한다.
        void EndRound(bool attackerWins, string reason)
        {
            if (IsRoundEnded) return;
            IsRoundEnded = true;

            bool attackerIsA = attackerTeamId == 0;
            bool teamAWon = attackerWins ? attackerIsA : !attackerIsA;

            string winnerText = teamAWon
                ? $"TEAM BLUE WINS ROUND! ({reason})"
                : $"TEAM RED WINS ROUND! ({reason})";
            Color winnerColor = teamAWon ? teamAColor : teamBColor;

            if (teamAWon) ScoreTeamA++; else ScoreTeamB++;

            OnScoreChanged?.Invoke(ScoreTeamA, ScoreTeamB);
            OnRoundFinished?.Invoke(winnerText, winnerColor);
            Debug.Log($"[MatchManager] Round {CurrentRound} Over - {winnerText}");
            Debug.Log($"[MatchManager] 누적 명중률: {WeaponController.AccuracyPercent:F1}% ({WeaponController.TotalHits}/{WeaponController.TotalShotsFired}), 헤드샷 비율: {WeaponController.HeadshotPercent:F1}% ({WeaponController.TotalHeadshots}/{WeaponController.TotalHits})");

            NotifyRoundEndToSurvivors(teamAWon);

            if (autoNextRound)
            {
                StartCoroutine(NextRoundRoutine());
            }
        }

        // 킬로 죽은 에이전트는 HandleDeath에서 이미 EndEpisode를 받았지만, 타임아웃/폭발/해체로 라운드가
        // 끝나면 그 순간 살아있던 에이전트들은 그런 신호를 한 번도 못 받고 다음 라운드 시작 시
        // ClearPreviousMatch()로 그냥 Destroy돼버린다 - ML-Agents 입장에서 에피소드가 끊긴 채 사라지는
        // 셈이라 팀 목표(설치/해체/타임아웃) 승패를 학습할 신호 자체가 없었다. 생존자 전원에게 승/패
        // 보상과 함께 명시적으로 EndEpisode를 준다.
        void NotifyRoundEndToSurvivors(bool teamAWon)
        {
            foreach (var p in teamA)
            {
                if (p == null || p.IsDead) continue;
                p.GetComponent<CombatMLAgent>()?.OnRoundResolved(teamAWon);
            }
            foreach (var p in teamB)
            {
                if (p == null || p.IsDead) continue;
                p.GetComponent<CombatMLAgent>()?.OnRoundResolved(!teamAWon);
            }
        }

        IEnumerator NextRoundRoutine()
        {
            yield return new WaitForSeconds(roundRestartDelay);
            CurrentRound++;
            BeginMatch();
        }

        public void RestartMatch()
        {
            StopAllCoroutines();
            BeginMatch();
        }

        public int CountAlive(List<PlayerHealth> team)
        {
            int count = 0;
            foreach (var p in team)
                if (p != null && !p.IsDead) count++;
            return count;
        }

        public List<PlayerHealth> GetEnemies(int teamId) => teamId == 0 ? teamB : teamA;
        public List<PlayerHealth> GetTeam(int teamId) => teamId == 0 ? teamA : teamB;
        public List<PlayerHealth> GetAllPlayers()
        {
            var all = new List<PlayerHealth>(teamA);
            all.AddRange(teamB);
            return all;
        }

        public Vector3 GetRoamPoint(int teamId)
        {
            Transform[] enemySpawns = teamId == 0 ? teamBSpawns : teamASpawns;
            if (enemySpawns != null && enemySpawns.Length > 0)
            {
                Transform t = enemySpawns[UnityEngine.Random.Range(0, enemySpawns.Length)];
                if (t != null) return t.position + new Vector3(UnityEngine.Random.Range(-3f, 3f), 0, UnityEngine.Random.Range(-3f, 3f));
            }
            return transform.position;
        }
    }
}

