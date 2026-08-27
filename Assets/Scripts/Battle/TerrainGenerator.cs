using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FPSManager.Battle
{
    // 배틀로얄용 랜덤 지형 생성. 지형/오브젝트 배치 완료 직후 NavMesh를 베이크하고 매치를 시작시킨다.
    public class TerrainGenerator : MonoBehaviour
    {
        [Header("지형 크기 및 노이즈")]
        public int xSize = 300;
        public int zSize = 300;

        public int xOffset;
        public int zOffset;

        [Tooltip("체크하면 Play할 때마다 xOffset/zOffset을 랜덤으로 새로 뽑아 지형/오브젝트 배치가 매번 달라진다. 끄면 위 xOffset/zOffset 값 그대로 고정된 지형이 재현된다.")]
        public bool randomizeSeedOnStart = true;

        public float noiseScale = 0.03f;
        public float heightMultiplier = 7;

        public int octavesCount = 1;
        public float lacunarity = 2f;
        public float persistance = 0.5f;

        [Header("머티리얼 및 텍스처 레이어")]
        public Material mat;
        public List<Layer> terrainLayers = new List<Layer>();

        [Header("Water")]
        public bool generateWater = false;
        public Material waterMat;
        [Range(0, 1)] public float waterHeight = 0.3f;

        [Header("Objects")]
        public List<SpawnObject> spawnObjects = new List<SpawnObject>();

        [Header("NavMesh")]
        public NavMeshSurface navSurface;

        [Header("연동")]
        public MatchManager matchManager;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private Mesh mesh;

        private GameObject waterObject;
        private List<GameObject> spawnedObjects = new List<GameObject>();

        void Awake()
        {
            if (navSurface == null) navSurface = GetComponent<NavMeshSurface>();
            if (matchManager == null) matchManager = GetComponent<MatchManager>();
        }

        void Start()
        {
            if (randomizeSeedOnStart)
            {
                xOffset = Random.Range(-100000, 100000);
                zOffset = Random.Range(-100000, 100000);
            }

            GenerateTerrain();
        }

        void Update() { }

        public void GenerateTerrain()
        {
            CreateMesh();
            GenerateMesh();
            GenerateTexture();
            SpawnObjects();

            if (generateWater)
                GenerateWater();
            else if (waterObject != null)
                DestroyImmediate(waterObject);

            if (navSurface != null)
            {
                navSurface.BuildNavMesh();
            }
            else
            {
                Debug.LogWarning("[TerrainGenerator] navSurface가 지정되지 않아 NavMesh를 베이크하지 못했습니다.");
            }

            if (matchManager != null)
            {
                matchManager.BeginMatch();
            }
            else
            {
                Debug.LogWarning("[TerrainGenerator] matchManager가 지정되지 않았습니다.");
            }
        }

        private void SpawnObjects()
        {
            foreach (var obj in spawnedObjects)
                if (obj != null) DestroyImmediate(obj);
            spawnedObjects.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.gameObject != waterObject)
                    DestroyImmediate(child.gameObject);
            }

            float minH = mesh.bounds.min.y;
            float maxH = mesh.bounds.max.y;
            Vector3[] vertices = mesh.vertices;

            List<Vector3> spawnedPositions = new List<Vector3>();

            for (int z = 0; z <= zSize; z++)
            {
                for (int x = 0; x <= xSize; x++)
                {
                    int index = z * (xSize + 1) + x;
                    Vector3 vertex = vertices[index];
                    float heightNormalized = Mathf.InverseLerp(minH, maxH, vertex.y);

                    foreach (var spawnObj in spawnObjects)
                    {
                        if (spawnObj.prefab == null) continue;
                        if (heightNormalized < spawnObj.minHeight || heightNormalized > spawnObj.maxHeight) continue;

                        int seed = (x + xOffset) * 73856093 ^ (z + zOffset) * 19349663 ^ spawnObj.prefab.name.GetHashCode();
                        Random.InitState(seed);

                        if (Random.value > spawnObj.spawnChance) continue;

                        Vector3 worldPos = transform.TransformPoint(vertex);

                        bool tooClose = false;
                        foreach (var pos in spawnedPositions)
                        {
                            if (Vector3.Distance(worldPos, pos) < spawnObj.minDistanceBetween)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (tooClose) continue;

#if UNITY_EDITOR
                        GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(spawnObj.prefab);
                        obj.transform.position = worldPos;
#else
                        GameObject obj = Instantiate(spawnObj.prefab, worldPos, Quaternion.identity);
#endif
                        obj.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                        float randomScale = Random.Range(spawnObj.minScale, spawnObj.maxScale);
                        obj.transform.localScale = Vector3.one * randomScale;
                        obj.transform.parent = transform;

                        if (spawnObj.isCover)
                        {
                            ApplyCoverSetup(obj);
                        }

                        spawnedObjects.Add(obj);
                        spawnedPositions.Add(worldPos);
                    }
                }
            }
        }

        // 엄폐물로 지정된 오브젝트에 Cover 태그(AIBrain.FindNearestCover가 탐색) + NavMeshObstacle(경로탐색 회피)을 부착한다.
        private void ApplyCoverSetup(GameObject obj)
        {
            obj.tag = "Cover";

            NavMeshObstacle obstacle = obj.GetComponent<NavMeshObstacle>();
            if (obstacle == null) obstacle = obj.AddComponent<NavMeshObstacle>();

            obstacle.carving = true;
            obstacle.shape = NavMeshObstacleShape.Box;

            Bounds localBounds = GetLocalRendererBounds(obj);
            obstacle.center = localBounds.center;
            obstacle.size = localBounds.size;
        }

        // 렌더러 기준 월드 바운드를 오브젝트 로컬 공간으로 환산 (NavMeshObstacle의 center/size는 로컬 기준)
        private static Bounds GetLocalRendererBounds(GameObject obj)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);

            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

            Vector3 scale = obj.transform.lossyScale;
            Vector3 center = obj.transform.InverseTransformPoint(worldBounds.center);
            Vector3 size = new Vector3(
                worldBounds.size.x / Mathf.Max(scale.x, 0.001f),
                worldBounds.size.y / Mathf.Max(scale.y, 0.001f),
                worldBounds.size.z / Mathf.Max(scale.z, 0.001f));

            return new Bounds(center, size);
        }

        private void GenerateWater()
        {
            if (waterObject != null)
                DestroyImmediate(waterObject);

            waterObject = new GameObject("Water");
            waterObject.transform.parent = transform;

            float minH = mesh.bounds.min.y;
            float maxH = mesh.bounds.max.y;
            float waterY = Mathf.Lerp(minH, maxH, waterHeight) + transform.position.y;

            waterObject.transform.position = new Vector3(
                transform.position.x + xSize / 2f,
                waterY,
                transform.position.z + zSize / 2f
            );
            waterObject.transform.localScale = new Vector3(xSize, 1, zSize);

            MeshFilter mf = waterObject.AddComponent<MeshFilter>();
            MeshRenderer mr = waterObject.AddComponent<MeshRenderer>();
            mf.mesh = CreatePlaneMesh();
            mr.material = waterMat;
        }

        private Mesh CreatePlaneMesh()
        {
            Mesh planeMesh = new Mesh();
            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0, -0.5f),
                new Vector3( 0.5f, 0, -0.5f),
                new Vector3(-0.5f, 0,  0.5f),
                new Vector3( 0.5f, 0,  0.5f)
            };
            int[] triangles = new int[] { 0, 2, 1, 2, 3, 1 };
            Vector2[] uvs = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0, 1),
                new Vector2(1, 1)
            };
            planeMesh.vertices = vertices;
            planeMesh.triangles = triangles;
            planeMesh.uv = uvs;
            planeMesh.RecalculateNormals();
            return planeMesh;
        }

        private void CreateMesh()
        {
            if (GetComponent<MeshFilter>() == null)
                meshFilter = gameObject.AddComponent<MeshFilter>();
            if (GetComponent<MeshRenderer>() == null)
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            if (GetComponent<MeshCollider>() == null)
                meshCollider = gameObject.AddComponent<MeshCollider>();

            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();

            mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // 65535 버텍스 제한 해제
            meshFilter.mesh = mesh;
            meshRenderer.material = mat;
        }

        private void GenerateMesh()
        {
            Vector3[] vertices = new Vector3[(xSize + 1) * (zSize + 1)];
            int i = 0;
            for (int z = 0; z <= zSize; z++)
            {
                for (int x = 0; x <= xSize; x++)
                {
                    float yPos = 0;
                    for (int o = 0; o < octavesCount; o++)
                    {
                        float frequency = Mathf.Pow(lacunarity, o);
                        float amplitude = Mathf.Pow(persistance, o);
                        yPos += Mathf.PerlinNoise((x + xOffset) * noiseScale * frequency, (z + zOffset) * noiseScale * frequency) * amplitude;
                    }
                    yPos *= heightMultiplier;
                    vertices[i] = new Vector3(x, yPos, z);
                    i++;
                }
            }

            int[] triangles = new int[xSize * zSize * 6];
            int vertex = 0;
            int triangleIndex = 0;
            for (int z = 0; z < zSize; z++)
            {
                for (int x = 0; x < xSize; x++)
                {
                    triangles[triangleIndex + 0] = vertex + 0;
                    triangles[triangleIndex + 1] = vertex + xSize + 1;
                    triangles[triangleIndex + 2] = vertex + 1;
                    triangles[triangleIndex + 3] = vertex + 1;
                    triangles[triangleIndex + 4] = vertex + xSize + 1;
                    triangles[triangleIndex + 5] = vertex + xSize + 2;
                    vertex++;
                    triangleIndex += 6;
                }
                vertex++;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            meshCollider.sharedMesh = mesh;
        }

        private void GenerateTexture()
        {
            if (mat == null) return;
            if (terrainLayers == null || terrainLayers.Count == 0) return;

            float minTerrainHeight = mesh.bounds.min.y + transform.position.y - 0.1f;
            float maxTerrainHeight = mesh.bounds.max.y + transform.position.y + 0.1f;

            if (mat.HasProperty("minTerrainHeight")) mat.SetFloat("minTerrainHeight", minTerrainHeight);
            if (mat.HasProperty("maxTerrainHeight")) mat.SetFloat("maxTerrainHeight", maxTerrainHeight);

            int layersCount = terrainLayers.Count;
            if (mat.HasProperty("numTextures")) mat.SetInt("numTextures", layersCount);

            float[] heights = new float[layersCount];
            int index = 0;
            foreach (Layer l in terrainLayers)
            {
                heights[index] = l.startHeight;
                index++;
            }
            if (mat.HasProperty("terrainHeights")) mat.SetFloatArray("terrainHeights", heights);

            Texture2DArray textures = new Texture2DArray(512, 512, layersCount, TextureFormat.RGBA32, true);
            for (int i = 0; i < layersCount; i++)
            {
                if (terrainLayers[i].texture == null) continue;
                textures.SetPixels(terrainLayers[i].texture.GetPixels(), i);
            }
            textures.Apply();
            if (mat.HasProperty("terrainTextures")) mat.SetTexture("terrainTextures", textures);
        }

        [System.Serializable]
        public class Layer
        {
            public Texture2D texture;
            [Range(0, 1)] public float startHeight;
        }

        [System.Serializable]
        public class SpawnObject
        {
            public GameObject prefab;
            [Range(0, 1)] public float minHeight = 0f;
            [Range(0, 1)] public float maxHeight = 1f;
            [Range(0, 1)] public float spawnChance = 0.05f;
            public float minScale = 0.8f;
            public float maxScale = 1.2f;
            public float minDistanceBetween = 1.5f;

            [Tooltip("체크하면 이 오브젝트에 Cover 태그와 NavMeshObstacle이 자동으로 붙어 엄폐물/이동 장애물로 취급된다.")]
            public bool isCover = false;
        }
    }
}
