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
        }

        // ArenaGenerator가 커버 배치 + NavMesh 베이크를 끝낸 뒤 호출한다.
        public void BeginMatch()
        {
            ClearPreviousMatch();
            IsRoundEnded = false;

            SpawnTeam(teamASpawns, 0, teamAColor, teamA, teamAPrefab != null ? teamAPrefab : playerPrefab);
            SpawnTeam(teamBSpawns, 1, teamBColor, teamB, teamBPrefab != null ? teamBPrefab : playerPrefab);

            OnScoreChanged?.Invoke(ScoreTeamA, ScoreTeamB);
            OnAliveCountChanged?.Invoke(CountAlive(teamA), CountAlive(teamB));
            OnMatchStarted?.Invoke();

            Debug.Log($"[MatchManager] Round {CurrentRound} Started: Team Blue ({teamA.Count}) vs Team Red ({teamB.Count})");
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

        void SpawnTeam(Transform[] spawns, int teamId, Color color, List<PlayerHealth> list, GameObject prefabToUse)
        {
            if (spawns == null || prefabToUse == null)
            {
                Debug.LogError($"[MatchManager] Spawns or Prefab missing for Team {teamId}");
                return;
            }

            for (int i = 0; i < spawns.Length; i++)
            {
                Transform spawn = spawns[i];
                if (spawn == null) continue;

                GameObject go = Instantiate(prefabToUse, spawn.position, spawn.rotation);
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

            if (aliveA == 0 || aliveB == 0)
            {
                IsRoundEnded = true;
                string winnerText;
                Color winnerColor;

                if (aliveA == 0 && aliveB == 0)
                {
                    winnerText = "DRAW!";
                    winnerColor = Color.white;
                }
                else if (aliveA > 0)
                {
                    ScoreTeamA++;
                    winnerText = "TEAM BLUE WINS ROUND!";
                    winnerColor = teamAColor;
                }
                else
                {
                    ScoreTeamB++;
                    winnerText = "TEAM RED WINS ROUND!";
                    winnerColor = teamBColor;
                }

                OnScoreChanged?.Invoke(ScoreTeamA, ScoreTeamB);
                OnRoundFinished?.Invoke(winnerText, winnerColor);
                Debug.Log($"[MatchManager] Round {CurrentRound} Over - {winnerText}");

                if (autoNextRound)
                {
                    StartCoroutine(NextRoundRoutine());
                }
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

