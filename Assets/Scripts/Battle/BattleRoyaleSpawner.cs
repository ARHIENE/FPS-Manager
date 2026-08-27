using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FPSManager.Battle
{
    // N팀 배틀로얄 스폰 위치 산출: 팀 클러스터를 맵에 흩뿌리고, 각 클러스터 안에 팀원을 분산 배치한다.
    // 실제 GameObject 생성은 MatchManager가 담당하고, 이 클래스는 위치(Vector3)만 계산한다.
    public class BattleRoyaleSpawner : MonoBehaviour
    {
        [Header("맵 범위")]
        public Vector3 mapCenter = Vector3.zero;
        public float mapWidth = 300f;
        public float mapDepth = 300f;
        public LayerMask groundMask = ~0;

        [Header("클러스터 설정")]
        [Tooltip("팀 스폰 클러스터끼리 최소 이 거리 이상 떨어지도록 배치 시도")]
        public float minClusterDistance = 25f;
        [Tooltip("한 클러스터 안에서 팀원이 흩어지는 반경")]
        public float memberSpreadRadius = 5f;

        [Header("NavMesh 검증")]
        public float navSampleMaxDistance = 5f;
        public int maxPlacementAttempts = 40;

        public List<Vector3[]> GenerateClusterSpawns(int teamCount, int playersPerTeam)
        {
            var result = new List<Vector3[]>(teamCount);
            var clusterCenters = new List<Vector3>(teamCount);

            for (int t = 0; t < teamCount; t++)
            {
                Vector3 center = FindClusterCenter(clusterCenters);
                clusterCenters.Add(center);
                result.Add(GenerateMemberPositions(center, playersPerTeam));
            }

            return result;
        }

        Vector3 FindClusterCenter(List<Vector3> existingClusters)
        {
            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                Vector3 candidate = RandomGroundPoint();

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navSampleMaxDistance, NavMesh.AllAreas))
                    continue;

                bool tooClose = false;
                foreach (var existing in existingClusters)
                {
                    if (Vector3.Distance(hit.position, existing) < minClusterDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                return hit.position;
            }

            Debug.LogWarning("[BattleRoyaleSpawner] 최소 클러스터 거리 제약을 만족하는 위치를 찾지 못해 마지막 후보 위치로 대체합니다.");
            NavMesh.SamplePosition(RandomGroundPoint(), out NavMeshHit fallback, navSampleMaxDistance * 4f, NavMesh.AllAreas);
            return fallback.position;
        }

        Vector3[] GenerateMemberPositions(Vector3 clusterCenter, int count)
        {
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                positions[i] = FindMemberPosition(clusterCenter);
            }
            return positions;
        }

        Vector3 FindMemberPosition(Vector3 clusterCenter)
        {
            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * memberSpreadRadius;
                Vector3 candidate = clusterCenter + new Vector3(offset.x, 0f, offset.y);

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navSampleMaxDistance, NavMesh.AllAreas))
                    return hit.position;
            }

            return clusterCenter;
        }

        // 지형 위 표면 지점을 찾기 위해 맵 상공에서 아래로 레이캐스트 (TerrainGenerator의 MeshCollider를 사용)
        Vector3 RandomGroundPoint()
        {
            float x = mapCenter.x + Random.Range(-mapWidth / 2f, mapWidth / 2f);
            float z = mapCenter.z + Random.Range(-mapDepth / 2f, mapDepth / 2f);
            float rayStartY = mapCenter.y + 1000f;

            if (Physics.Raycast(new Vector3(x, rayStartY, z), Vector3.down, out RaycastHit hit, 5000f, groundMask))
                return hit.point;

            return new Vector3(x, mapCenter.y, z);
        }
    }
}
