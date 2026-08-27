using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FPSManager.Battle
{
    [System.Serializable]
    public class ZonePhase
    {
        public float waitDuration;      // 이 단계 시작 전 대기 시간 (원이 안 움직이는 구간)
        public float shrinkDuration;    // 대기 후 실제로 원이 줄어드는 데 걸리는 시간
        [Range(0f, 1f)] public float targetRadiusRatio; // 이전 원 반지름 대비 다음 원 반지름 비율
        public float damagePerSecond;   // 이 단계에서 원 밖에 있을 때 초당 데미지
    }

    // 배틀로얄 자기장(세이프존): 단계표대로 원이 축소되고, 밖에 있으면 지속 데미지를 준다.
    public class ZoneManager : MonoBehaviour
    {
        public static ZoneManager Instance { get; private set; }

        [Header("연동")]
        public TerrainGenerator terrainGenerator;
        public MatchManager matchManager;

        [Header("단계표 (PUBG 참고 초기값 - 실제 매치 진행시간 보고 조정 권장)")]
        public List<ZonePhase> phases = new List<ZonePhase>
        {
            new ZonePhase { waitDuration = 90f, shrinkDuration = 60f, targetRadiusRatio = 0.60f, damagePerSecond = 1f },
            new ZonePhase { waitDuration = 60f, shrinkDuration = 50f, targetRadiusRatio = 0.55f, damagePerSecond = 2f },
            new ZonePhase { waitDuration = 45f, shrinkDuration = 40f, targetRadiusRatio = 0.50f, damagePerSecond = 3f },
            new ZonePhase { waitDuration = 35f, shrinkDuration = 30f, targetRadiusRatio = 0.45f, damagePerSecond = 5f },
            new ZonePhase { waitDuration = 25f, shrinkDuration = 25f, targetRadiusRatio = 0.40f, damagePerSecond = 7f },
            new ZonePhase { waitDuration = 20f, shrinkDuration = 20f, targetRadiusRatio = 0.35f, damagePerSecond = 10f },
        };

        [Header("시각화")]
        [Tooltip("반투명 파란색 원기둥 벽 머티리얼. Surface Type=Transparent, Render Face=Both 로 설정해야 안/밖 양쪽에서 보인다.")]
        public Material zoneWallMaterial;
        public float wallHeightScale = 150f;
        public Color nextRingColor = new Color(1f, 1f, 1f, 0.9f);
        public float nextRingWidth = 1.5f;
        [Range(8, 128)] public int nextRingSegments = 72;

        public Vector3 CurrentCenter { get; private set; }
        public float CurrentRadius { get; private set; }
        public Vector3 NextCenter { get; private set; }
        public float NextRadius { get; private set; }
        public int CurrentPhaseIndex { get; private set; } = -1;
        public bool IsShrinking { get; private set; }
        public float PhaseTimeRemaining { get; private set; }
        public float CurrentDamagePerSecond { get; private set; }

        public event Action<int> OnPhaseChanged;

        GameObject wallObject;
        LineRenderer nextRingRenderer;
        Coroutine sequenceRoutine;

        void Awake()
        {
            Instance = this;
            if (terrainGenerator == null) terrainGenerator = GetComponent<TerrainGenerator>();
            if (matchManager == null) matchManager = GetComponent<MatchManager>();
        }

        void Start()
        {
            BuildVisuals();
        }

        void OnEnable()
        {
            if (matchManager != null) matchManager.OnMatchStarted += StartZoneSequence;
        }

        void OnDisable()
        {
            if (matchManager != null) matchManager.OnMatchStarted -= StartZoneSequence;
        }

        void Update()
        {
            ApplyZoneDamage();
            UpdateVisuals();
        }

        // MatchManager.OnMatchStarted 이벤트로 자동 호출됨 (TerrainGenerator.GenerateTerrain -> MatchManager.BeginMatch 이후)
        public void StartZoneSequence()
        {
            if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);

            Vector3 mapCenter;
            float mapRadius;
            if (terrainGenerator != null)
            {
                mapCenter = terrainGenerator.transform.position + new Vector3(terrainGenerator.xSize / 2f, 0f, terrainGenerator.zSize / 2f);
                mapRadius = Mathf.Sqrt(terrainGenerator.xSize * terrainGenerator.xSize + terrainGenerator.zSize * terrainGenerator.zSize) / 2f;
            }
            else
            {
                mapCenter = Vector3.zero;
                mapRadius = 150f;
            }

            CurrentCenter = mapCenter;
            CurrentRadius = mapRadius;
            NextCenter = mapCenter;
            NextRadius = mapRadius;
            CurrentPhaseIndex = -1;
            CurrentDamagePerSecond = 0f;
            IsShrinking = false;
            PhaseTimeRemaining = 0f;

            sequenceRoutine = StartCoroutine(RunSequence());
        }

        IEnumerator RunSequence()
        {
            for (int i = 0; i < phases.Count; i++)
            {
                CurrentPhaseIndex = i;
                ZonePhase phase = phases[i];
                PickNextCircle(phase.targetRadiusRatio);
                CurrentDamagePerSecond = phase.damagePerSecond;
                OnPhaseChanged?.Invoke(i);

                // 대기 구간 - 원 고정, 다음 원(흰 링)만 미리 표시
                IsShrinking = false;
                PhaseTimeRemaining = phase.waitDuration;
                while (PhaseTimeRemaining > 0f)
                {
                    PhaseTimeRemaining -= Time.deltaTime;
                    yield return null;
                }

                // 축소 구간 - Current를 Next로 선형 보간
                IsShrinking = true;
                Vector3 startCenter = CurrentCenter;
                float startRadius = CurrentRadius;
                float t = 0f;
                PhaseTimeRemaining = phase.shrinkDuration;
                while (t < 1f)
                {
                    float dt = Time.deltaTime / Mathf.Max(phase.shrinkDuration, 0.01f);
                    t += dt;
                    PhaseTimeRemaining -= Time.deltaTime;
                    float lerp = Mathf.Clamp01(t);
                    CurrentCenter = Vector3.Lerp(startCenter, NextCenter, lerp);
                    CurrentRadius = Mathf.Lerp(startRadius, NextRadius, lerp);
                    yield return null;
                }

                CurrentCenter = NextCenter;
                CurrentRadius = NextRadius;
            }

            IsShrinking = false; // 마지막 단계 이후 최종 원 유지
        }

        void PickNextCircle(float radiusRatio)
        {
            float radius = CurrentRadius * radiusRatio;
            float maxOffset = Mathf.Max(CurrentRadius - radius, 0f);
            Vector2 offset = UnityEngine.Random.insideUnitCircle * maxOffset;

            NextCenter = CurrentCenter + new Vector3(offset.x, 0f, offset.y);
            NextRadius = radius;
        }

        void ApplyZoneDamage()
        {
            if (CurrentPhaseIndex < 0 || CurrentDamagePerSecond <= 0f || matchManager == null) return;

            List<PlayerHealth> players = matchManager.GetAllPlayers();
            foreach (var p in players)
            {
                if (p == null || p.IsDead) continue;
                if (IsInsideZone(p.transform.position)) continue;

                p.ApplyDamage(CurrentDamagePerSecond * Time.deltaTime, null, false);
            }
        }

        public bool IsInsideZone(Vector3 worldPos)
        {
            Vector3 flatPos = new Vector3(worldPos.x, CurrentCenter.y, worldPos.z);
            return Vector3.Distance(flatPos, CurrentCenter) <= CurrentRadius;
        }

        // Search()의 로밍 목표점 등을 안전지대 쪽으로 눌러 담을 때 사용
        public Vector3 ClampInsideZone(Vector3 point)
        {
            Vector3 flat = new Vector3(point.x, CurrentCenter.y, point.z);
            Vector3 offset = flat - CurrentCenter;
            float dist = offset.magnitude;
            if (dist <= CurrentRadius || dist < 0.0001f) return point;

            Vector3 clampedFlat = CurrentCenter + offset.normalized * (CurrentRadius * 0.9f);
            return new Vector3(clampedFlat.x, point.y, clampedFlat.z);
        }

        // ---- 시각화 ----

        void BuildVisuals()
        {
            wallObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wallObject.name = "ZoneWall";
            var wallCollider = wallObject.GetComponent<Collider>();
            if (wallCollider != null) Destroy(wallCollider);
            if (zoneWallMaterial != null) wallObject.GetComponent<Renderer>().sharedMaterial = zoneWallMaterial;
            else Debug.LogWarning("[ZoneManager] zoneWallMaterial이 지정되지 않아 자기장 벽이 보이지 않습니다. Surface=Transparent, Render Face=Both인 머티리얼을 연결하세요.");
            wallObject.transform.SetParent(transform);

            var ringObj = new GameObject("NextZoneRing");
            ringObj.transform.SetParent(transform);
            nextRingRenderer = ringObj.AddComponent<LineRenderer>();
            nextRingRenderer.loop = true;
            nextRingRenderer.useWorldSpace = true;
            nextRingRenderer.widthMultiplier = nextRingWidth;
            nextRingRenderer.positionCount = nextRingSegments;
            nextRingRenderer.material = new Material(Shader.Find("Sprites/Default"));
            nextRingRenderer.startColor = nextRingColor;
            nextRingRenderer.endColor = nextRingColor;
            nextRingRenderer.enabled = false;
        }

        void UpdateVisuals()
        {
            if (wallObject != null)
            {
                wallObject.transform.position = CurrentCenter;
                float scaleXZ = CurrentRadius * 2f; // 기본 Cylinder 반지름 0.5 기준
                wallObject.transform.localScale = new Vector3(scaleXZ, wallHeightScale, scaleXZ);
            }

            if (nextRingRenderer != null)
            {
                bool show = CurrentPhaseIndex >= 0;
                nextRingRenderer.enabled = show;
                if (show)
                {
                    for (int i = 0; i < nextRingSegments; i++)
                    {
                        float angle = (float)i / nextRingSegments * Mathf.PI * 2f;
                        Vector3 point = NextCenter + new Vector3(Mathf.Cos(angle) * NextRadius, 0.5f, Mathf.Sin(angle) * NextRadius);
                        nextRingRenderer.SetPosition(i, point);
                    }
                }
            }
        }
    }
}
