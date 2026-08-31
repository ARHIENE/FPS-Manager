using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

namespace FPSManager.Battle
{
    // 5v5 배틀 아레나 생성 및 NavMesh 베이크
    [RequireComponent(typeof(NavMeshSurface))]
    public class ArenaGenerator : MonoBehaviour
    {
        [Header("엄폐물 설정")]
        public GameObject coverPrefab;
        public int coverCount = 24;

        [Header("맵 범위 설정")]
        // 104(2배)로 키웠다가 NavMesh 베이크가 지나치게 오래 걸려서(실측: 15분 넘게 브릿지 응답 없음)
        // 원래 크기로 되돌림 - 거점을 진영 끝에 두는 설계(공격팀이 맵을 가로질러야 함)는 그대로 유지.
        public float mapWidth = 52f;
        public float mapDepth = 52f;

        [Header("팀 스폰 보호 구역")]
        // 설치/해체 모드로 바뀌면서 스폰이 씬 고정 Transform이 아니라 MatchManager가 사이트 위치
        // 기준으로 계산하는 방식으로 바뀜(공격팀은 사이트 반대편 끝) - 여기 값도 그와 일치시켜서
        // 엄폐물이 스폰 위치에 안 겹치게 한다.
        public Vector3 teamASpawnCenter = new Vector3(0, 0, -20f);
        public Vector3 teamBSpawnCenter = new Vector3(0, 0, 20f);
        public float spawnProtectRadius = 8f;

        [Header("엄폐물 크기")]
        public Vector3 minSize = new Vector3(2.5f, 1.6f, 1.2f);
        public Vector3 maxSize = new Vector3(5.0f, 2.4f, 2.2f);

        [Header("연동")]
        public MatchManager matchManager;

        private NavMeshSurface navSurface;
        private readonly List<GameObject> spawnedCovers = new List<GameObject>();

        void Awake()
        {
            navSurface = GetComponent<NavMeshSurface>();
            if (matchManager == null)
            {
                matchManager = GetComponent<MatchManager>();
            }
        }

        void Start()
        {
            GenerateArenaAndStart();
        }

        public void GenerateArenaAndStart()
        {
            ClearCovers();
            GenerateCover();

            if (navSurface != null)
            {
                navSurface.BuildNavMesh();
            }

            if (matchManager != null)
            {
                matchManager.BeginMatch();
            }
            else
            {
                Debug.LogWarning("[ArenaGenerator] matchManager is missing.");
            }
        }

        void ClearCovers()
        {
            foreach (var c in spawnedCovers)
            {
                if (c != null) Destroy(c);
            }
            spawnedCovers.Clear();
        }

        void GenerateCover()
        {
            if (coverPrefab == null)
            {
                Debug.LogWarning("[ArenaGenerator] coverPrefab이 지정되지 않았습니다.");
                return;
            }

            // 맵 중심(0,0,0) 기준 180도 회전 대칭 배치: 한쪽에 배치한 엄폐물을 반대쪽에도 동일하게 미러링
            int pairCount = coverCount / 2;
            int placed = 0;
            int attempts = 0;
            int maxAttempts = pairCount * 25;

            while (placed < pairCount && attempts < maxAttempts)
            {
                attempts++;
                Vector3 pos = GetRandomPosition();
                if (IsOverlappingSpawn(pos)) continue;

                Vector3 size = new Vector3(
                    Random.Range(minSize.x, maxSize.x),
                    Random.Range(minSize.y, maxSize.y),
                    Random.Range(minSize.z, maxSize.z));
                float rotY = Random.Range(0, 4) * 90f;

                SpawnCover(pos, size, rotY);
                SpawnCover(MirrorPosition180(pos), size, rotY + 180f);
                placed++;
            }

            // Center large tactical cover (이미 중심에 있어 대칭)
            Vector3 centerSize = new Vector3(Random.Range(4f, 6f), 2.2f, Random.Range(4f, 6f));
            SpawnCover(new Vector3(0, centerSize.y / 2f, 0), centerSize, 0f);
        }

        static Vector3 MirrorPosition180(Vector3 pos)
        {
            return new Vector3(-pos.x, pos.y, -pos.z);
        }

        bool IsOverlappingSpawn(Vector3 pos)
        {
            if (Vector3.Distance(pos, teamASpawnCenter) < spawnProtectRadius) return true;
            if (Vector3.Distance(pos, teamBSpawnCenter) < spawnProtectRadius) return true;
            return false;
        }

        Vector3 GetRandomPosition()
        {
            float x = Random.Range(-mapWidth / 2f + 3f, mapWidth / 2f - 3f);
            float z = Random.Range(-mapDepth / 2f + 5f, mapDepth / 2f - 5f);
            return new Vector3(x, 1f, z);
        }

        void SpawnCover(Vector3 position, Vector3 size, float rotY)
        {
            GameObject cover = Instantiate(coverPrefab, position, Quaternion.Euler(0, rotY, 0), transform);
            cover.transform.localScale = size;
            cover.name = "Cover";
            spawnedCovers.Add(cover);
        }
    }
}

