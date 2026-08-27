using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace FPSManager.Battle
{
    public class BattleRoyaleTeam
    {
        public int teamId;
        public Color color;
        public Vector3 spawnCenter;
        public readonly List<PlayerHealth> members = new List<PlayerHealth>();

        public bool IsEliminated
        {
            get
            {
                foreach (var m in members)
                    if (m != null && !m.IsDead) return false;
                return true;
            }
        }

        public int AliveCount
        {
            get
            {
                int count = 0;
                foreach (var m in members)
                    if (m != null && !m.IsDead) count++;
                return count;
            }
        }
    }

    // 배틀로얄(4인 x N팀) 스폰, 팀 생존 집계, 팀 탈락/매치 종료 관리
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("스폰 프리팹 설정")]
        public GameObject playerPrefab;

        [Header("배틀로얄 설정")]
        public int teamCount = 20;
        public int playersPerTeam = 4;

        [Header("연동")]
        public BattleRoyaleSpawner spawner;

        [Header("재시작 설정")]
        public float restartDelay = 6.0f;

        [Header("탐지 성능 설정 (공간 분할 그리드)")]
        public float gridCellSize = 20f;
        public float gridRebuildInterval = 0.15f;

        public bool IsMatchEnded { get; private set; }
        public int AliveTeamCount { get; private set; }
        public int TotalTeamCount => teams.Count;

        private readonly List<BattleRoyaleTeam> teams = new List<BattleRoyaleTeam>();
        private readonly Dictionary<int, BattleRoyaleTeam> teamLookup = new Dictionary<int, BattleRoyaleTeam>();
        private readonly List<GameObject> spawnedPlayers = new List<GameObject>();
        private readonly Dictionary<Vector2Int, List<PlayerHealth>> spatialGrid = new Dictionary<Vector2Int, List<PlayerHealth>>();
        private float nextGridRebuildTime;

        public event Action<int, int> OnAliveTeamsChanged; // aliveTeams, totalTeams
        public event Action<string, Color, bool> OnKillFeedEntry;
        public event Action<int, int> OnTeamEliminated; // eliminatedTeamId, remainingTeams
        public event Action<int, Color> OnMatchEnded; // winningTeamId, winningTeamColor
        public event Action OnMatchStarted;

        void Awake()
        {
            Instance = this;
        }

        void Update()
        {
            if (Time.time >= nextGridRebuildTime)
            {
                RebuildSpatialGrid();
                nextGridRebuildTime = Time.time + gridRebuildInterval;
            }

            if (Keyboard.current != null && IsMatchEnded && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                RestartMatch();
            }
        }

        // TerrainGenerator.GenerateTerrain()이 지형 생성 + NavMesh 베이크를 끝낸 뒤 호출한다.
        public void BeginMatch()
        {
            ClearPreviousMatch();
            IsMatchEnded = false;

            if (spawner == null) spawner = GetComponent<BattleRoyaleSpawner>();
            if (spawner == null || playerPrefab == null)
            {
                Debug.LogError("[MatchManager] spawner 또는 playerPrefab이 지정되지 않았습니다.");
                return;
            }

            List<Vector3[]> clusters = spawner.GenerateClusterSpawns(teamCount, playersPerTeam);

            for (int t = 0; t < clusters.Count; t++)
            {
                var team = new BattleRoyaleTeam { teamId = t, color = GenerateTeamColor(t, clusters.Count) };
                SpawnTeamMembers(team, clusters[t]);

                if (team.members.Count > 0)
                {
                    Vector3 sum = Vector3.zero;
                    foreach (var m in team.members) sum += m.transform.position;
                    team.spawnCenter = sum / team.members.Count;
                }

                teams.Add(team);
                teamLookup[team.teamId] = team;
            }

            AliveTeamCount = teams.Count;
            RebuildSpatialGrid();

            OnAliveTeamsChanged?.Invoke(AliveTeamCount, TotalTeamCount);
            OnMatchStarted?.Invoke();

            Debug.Log($"[MatchManager] Battle Royale Started: {teams.Count} teams, {spawnedPlayers.Count} players");
        }

        void ClearPreviousMatch()
        {
            foreach (var go in spawnedPlayers)
                if (go != null) Destroy(go);
            spawnedPlayers.Clear();

            teams.Clear();
            teamLookup.Clear();
            spatialGrid.Clear();
        }

        void SpawnTeamMembers(BattleRoyaleTeam team, Vector3[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject go = Instantiate(playerPrefab, positions[i], Quaternion.identity);
                go.name = $"Team{team.teamId + 1}_{i + 1}";
                spawnedPlayers.Add(go);
                TeamColorApplier.Apply(go, team.color);

                var health = go.GetComponent<PlayerHealth>();
                if (health == null) continue;

                health.teamId = team.teamId;
                health.OnDeathWithAttacker += HandlePlayerDeath;
                team.members.Add(health);
            }
        }

        void HandlePlayerDeath(PlayerHealth victim, PlayerHealth killer, bool isHeadshot)
        {
            string victimName = victim != null ? victim.name : "Player";

            if (killer == null)
            {
                OnKillFeedEntry?.Invoke($"☣ {victimName} died to the zone", new Color(0.4f, 0.75f, 1f), false);
            }
            else
            {
                Color killerColor = teamLookup.TryGetValue(killer.teamId, out var killerTeam) ? killerTeam.color : Color.white;
                string headshotText = isHeadshot ? " [HEADSHOT]" : "";
                OnKillFeedEntry?.Invoke($"{killer.name} eliminated {victimName}{headshotText}", killerColor, isHeadshot);
            }

            if (IsMatchEnded) return;
            if (victim == null || !teamLookup.TryGetValue(victim.teamId, out var victimTeam)) return;
            if (!victimTeam.IsEliminated) return; // 팀원이 아직 남아있으면 탈락 아님

            AliveTeamCount = CountAliveTeams();
            OnTeamEliminated?.Invoke(victim.teamId, AliveTeamCount);
            OnAliveTeamsChanged?.Invoke(AliveTeamCount, TotalTeamCount);
            Debug.Log($"[MatchManager] Team {victim.teamId + 1} eliminated. Remaining teams: {AliveTeamCount}");

            if (AliveTeamCount <= 1)
            {
                IsMatchEnded = true;
                BattleRoyaleTeam winner = FindLastAliveTeam();
                if (winner != null)
                {
                    OnMatchEnded?.Invoke(winner.teamId, winner.color);
                    Debug.Log($"[MatchManager] Match Over - Team {winner.teamId + 1} Wins!");
                }
                StartCoroutine(RestartRoutine());
            }
        }

        IEnumerator RestartRoutine()
        {
            yield return new WaitForSeconds(restartDelay);
            RestartMatch();
        }

        public void RestartMatch()
        {
            StopAllCoroutines();
            BeginMatch();
        }

        int CountAliveTeams()
        {
            int count = 0;
            foreach (var t in teams)
                if (!t.IsEliminated) count++;
            return count;
        }

        BattleRoyaleTeam FindLastAliveTeam()
        {
            foreach (var t in teams)
                if (!t.IsEliminated) return t;
            return null;
        }

        // ---- 조회 API ----

        public IReadOnlyList<BattleRoyaleTeam> GetTeams() => teams;

        public List<PlayerHealth> GetTeam(int teamId) => teamLookup.TryGetValue(teamId, out var t) ? t.members : null;

        // 생사 무관 전체 플레이어 (자기장 데미지 판정처럼 전원을 순회해야 하는 경우용)
        public List<PlayerHealth> GetAllPlayers()
        {
            var result = new List<PlayerHealth>();
            foreach (var team in teams)
                foreach (var m in team.members)
                    if (m != null) result.Add(m);
            return result;
        }

        // 내 팀이 아닌 모든 생존자 (일반 용도 - 매 프레임 호출하는 AI 탐지 경로는 GetNearbyEnemies를 사용할 것)
        public List<PlayerHealth> GetEnemies(int teamId)
        {
            var result = new List<PlayerHealth>();
            foreach (var team in teams)
            {
                if (team.teamId == teamId) continue;
                foreach (var m in team.members)
                    if (m != null && !m.IsDead) result.Add(m);
            }
            return result;
        }

        public int CountAlive(List<PlayerHealth> team)
        {
            if (team == null) return 0;
            int count = 0;
            foreach (var p in team)
                if (p != null && !p.IsDead) count++;
            return count;
        }

        public Vector3 GetTeamSpawnCenter(int teamId) => teamLookup.TryGetValue(teamId, out var t) ? t.spawnCenter : transform.position;

        public Color GetTeamColorOf(int teamId) => teamLookup.TryGetValue(teamId, out var t) ? t.color : Color.white;

        static Color GenerateTeamColor(int teamId, int totalTeams)
        {
            float hue = totalTeams > 0 ? (float)teamId / totalTeams : 0f;
            return Color.HSVToRGB(hue, 0.65f, 1f);
        }

        // ---- 공간 분할(그리드) 기반 근접 적 탐색 ----
        // 80명 규모에서 매 프레임 전원이 "적 전체"를 훑으면 최악 80x76 레이캐스트가 발생하므로,
        // 그리드는 여기서 한 번만(gridRebuildInterval 주기) 재구성하고, AIBrain은 자신의 셀 주변만 조회한다.

        Vector2Int WorldToCell(Vector3 pos) =>
            new Vector2Int(Mathf.FloorToInt(pos.x / gridCellSize), Mathf.FloorToInt(pos.z / gridCellSize));

        void RebuildSpatialGrid()
        {
            spatialGrid.Clear();
            foreach (var team in teams)
            {
                foreach (var p in team.members)
                {
                    if (p == null || p.IsDead) continue;
                    Vector2Int cell = WorldToCell(p.transform.position);
                    if (!spatialGrid.TryGetValue(cell, out var list))
                    {
                        list = new List<PlayerHealth>();
                        spatialGrid[cell] = list;
                    }
                    list.Add(p);
                }
            }
        }

        // results를 재사용 버퍼로 받아 GC 할당 없이 채운다. AIBrain이 detectInterval 주기로 호출.
        public void GetNearbyEnemies(Vector3 origin, int teamId, float radius, List<PlayerHealth> results)
        {
            results.Clear();
            int cellRadius = Mathf.CeilToInt(radius / gridCellSize);
            Vector2Int center = WorldToCell(origin);

            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dz = -cellRadius; dz <= cellRadius; dz++)
                {
                    Vector2Int cell = new Vector2Int(center.x + dx, center.y + dz);
                    if (!spatialGrid.TryGetValue(cell, out var list)) continue;

                    foreach (var p in list)
                        if (p.teamId != teamId) results.Add(p);
                }
            }
        }

        // ---- 정찰(Wander) 지점 ----
        // 고정된 "상대 팀 스폰"이 없는 배틀로얄 구조라, 현재 위치 주변 NavMesh 상의 임의 지점을 반환한다.
        public Vector3 GetWanderPoint(Vector3 origin, float radius)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas))
                return hit.position;

            return origin;
        }
    }
}
