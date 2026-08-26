using System.Collections;
using UnityEngine;

namespace FPSManager.Battle
{
    // 1VS1 Game의 GunController.cs를 Photon 제거 + 팀 기반 피격 판정으로 포팅.
    // 원본은 헤드샷만 즉사 데미지였지만, 이 프로젝트는 부위 상관없이 데미지가 들어가도록 변경.
    // 레이캐스트 정렬 -> 어느 부위(머리/몸통)에 먼저 맞았는지 판정하는 로직만 원본 그대로 재사용.
    [RequireComponent(typeof(PlayerHealth))]
    public class WeaponController : MonoBehaviour
    {
        [Header("총 설정")]
        public float fireRate = 0.14f;
        public float range = 80f;
        public LayerMask hitLayer = ~0;

        [Header("부위별 데미지")]
        public float headDamage = 50f;
        public float bodyDamage = 25f;

        [Header("조준 기준점")]
        public Transform aimOrigin;
        public Transform firePoint;

        // AIBrain이 매 프레임 갱신하는 발사 의도.
        [HideInInspector] public bool triggerPressed;

        private PlayerHealth myHealth;
        private PlayerMovement myMovement;
        private HumanoidBattleAnimator battleAnimator;
        private AudioSource audioSource;
        private static AudioClip gunshotClip;
        private float nextFireTime;

        void Awake()
        {
            myHealth = GetComponent<PlayerHealth>();
            myMovement = GetComponent<PlayerMovement>();
            battleAnimator = GetComponent<HumanoidBattleAnimator>();

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.spatialBlend = 0.85f;
                audioSource.minDistance = 3f;
                audioSource.maxDistance = 60f;
                audioSource.playOnAwake = false;
            }

            if (gunshotClip == null)
            {
                gunshotClip = GenerateGunshotAudioClip();
            }

            if (aimOrigin == null)
            {
                Transform found = transform.Find("AimPivot");
                if (found != null) aimOrigin = found;
            }
            if (firePoint == null)
            {
                Transform found = transform.Find("FirePoint");
                if (found != null) firePoint = found;
            }
        }

        void Update()
        {
            if (triggerPressed && Time.time >= nextFireTime)
            {
                nextFireTime = Time.time + fireRate;
                Shoot();
            }
        }

        void Shoot()
        {
            if (aimOrigin == null) aimOrigin = transform;

            if (battleAnimator != null)
            {
                battleAnimator.TriggerRecoil();
            }

            // Play procedural gunshot sound
            if (audioSource != null && gunshotClip != null)
            {
                audioSource.pitch = Random.Range(0.92f, 1.08f);
                audioSource.PlayOneShot(gunshotClip, 0.45f);
            }

            float spread = myMovement != null ? myMovement.GetCurrentSpread() : 0f;

            Vector3 shootDir = aimOrigin.forward;
            shootDir += new Vector3(
                Random.Range(-spread, spread) * 0.015f,
                Random.Range(-spread, spread) * 0.015f,
                0);
            shootDir.Normalize();

            Ray ray = new Ray(aimOrigin.position, shootDir);
            Physics.SyncTransforms();

            RaycastHit[] hits = Physics.RaycastAll(ray, range, hitLayer);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            Vector3 endPoint = ray.origin + ray.direction * range;

            RaycastHit targetHeadHit = default;
            bool hitHead = false;
            PlayerHealth headTarget = null;

            RaycastHit targetBodyHit = default;
            bool hitBody = false;
            PlayerHealth bodyTarget = null;

            RaycastHit blockingObstacle = default;
            bool hitObstacle = false;

            foreach (var hit in hits)
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform))
                    continue;

                // Ignore UI / Trigger colliders
                if (hit.collider.isTrigger) continue;

                PlayerHealth targetHealth = hit.collider.GetComponentInParent<PlayerHealth>();
                if (targetHealth != null)
                {
                    if (targetHealth != myHealth && targetHealth.teamId != myHealth.teamId && !targetHealth.IsDead)
                    {
                        if (hit.collider.CompareTag("Head"))
                        {
                            if (!hitHead)
                            {
                                targetHeadHit = hit;
                                headTarget = targetHealth;
                                hitHead = true;
                            }
                        }
                        else if (!hitBody)
                        {
                            targetBodyHit = hit;
                            bodyTarget = targetHealth;
                            hitBody = true;
                        }
                    }
                }
                else if (!hitObstacle)
                {
                    blockingObstacle = hit;
                    hitObstacle = true;
                }
            }

            float obstacleDist = hitObstacle ? blockingObstacle.distance : range;
            float headDist = hitHead ? targetHeadHit.distance : range;
            float bodyDist = hitBody ? targetBodyHit.distance : range;

            if (hitHead && headDist < obstacleDist)
            {
                endPoint = targetHeadHit.point;
                headTarget.ApplyDamage(headDamage, myHealth, true);
                SpawnHitSparks(endPoint, targetHeadHit.normal, Color.yellow);
            }
            else if (hitBody && bodyDist < obstacleDist)
            {
                endPoint = targetBodyHit.point;
                bodyTarget.ApplyDamage(bodyDamage, myHealth, false);
                SpawnHitSparks(endPoint, targetBodyHit.normal, Color.red);
            }
            else if (hitObstacle)
            {
                endPoint = blockingObstacle.point;
                SpawnBulletHole(blockingObstacle.point, blockingObstacle.normal);
                SpawnHitSparks(endPoint, blockingObstacle.normal, Color.white);
            }

            Vector3 startPoint = firePoint != null ? firePoint.position : aimOrigin.position;
            ShowTracer(startPoint, endPoint);
            ShowMuzzleFlash(startPoint);
        }

        void ShowMuzzleFlash(Vector3 position)
        {
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(flash.GetComponent<Collider>());
            flash.name = "MuzzleFlash";
            flash.transform.position = position;
            flash.transform.localScale = Vector3.one * 0.15f;

            Renderer rend = flash.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            rend.material.color = new Color(1f, 0.85f, 0.4f, 1f);

            Destroy(flash, 0.05f);
        }

        void SpawnHitSparks(Vector3 point, Vector3 normal, Color sparkColor)
        {
            GameObject sparks = new GameObject("HitSpark");
            sparks.transform.position = point;
            sparks.transform.rotation = Quaternion.LookRotation(normal);

            LineRenderer lr = sparks.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            lr.startColor = sparkColor;
            lr.endColor = new Color(sparkColor.r, sparkColor.g, sparkColor.b, 0f);
            lr.startWidth = 0.03f;
            lr.endWidth = 0f;
            lr.positionCount = 2;
            lr.SetPosition(0, point);
            lr.SetPosition(1, point + (normal + Random.insideUnitSphere * 0.5f).normalized * 0.35f);

            Destroy(sparks, 0.1f);
        }

        void ShowTracer(Vector3 start, Vector3 end)
        {
            GameObject tracerGO = new GameObject("BulletTracer");
            LineRenderer lr = tracerGO.AddComponent<LineRenderer>();

            lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            Color laserColor = myHealth != null && myHealth.teamId == 0 ? new Color(0.25f, 0.65f, 1f) : new Color(1f, 0.35f, 0.2f);
            lr.startColor = laserColor;
            lr.endColor = new Color(laserColor.r, laserColor.g, laserColor.b, 0.15f);
            lr.startWidth = 0.05f;
            lr.endWidth = 0.02f;
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);

            StartCoroutine(FadeAndDestroyTracer(tracerGO, lr));
        }

        IEnumerator FadeAndDestroyTracer(GameObject obj, LineRenderer lr)
        {
            float duration = 0.10f;
            float elapsed = 0f;
            Color startCol = lr.startColor;
            Color endCol = lr.endColor;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                if (lr != null)
                {
                    lr.startColor = Color.Lerp(startCol, new Color(startCol.r, startCol.g, startCol.b, 0f), t);
                    lr.endColor = Color.Lerp(endCol, new Color(endCol.r, endCol.g, endCol.b, 0f), t);
                }
                yield return null;
            }

            Destroy(obj);
        }

        void SpawnBulletHole(Vector3 point, Vector3 normal)
        {
            GameObject hole = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(hole.GetComponent<Collider>());
            hole.name = "BulletHole";
            hole.transform.position = point + normal * 0.005f;
            hole.transform.rotation = Quaternion.LookRotation(-normal);
            hole.transform.localScale = Vector3.one * 0.12f;

            Renderer r = hole.GetComponent<Renderer>();
            r.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            r.material.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);

            Destroy(hole, 4.0f);
        }

        // Generates crisp procedural gunshot sound buffer
        static AudioClip GenerateGunshotAudioClip()
        {
            int sampleRate = 44100;
            int lengthSamples = (int)(sampleRate * 0.18f);
            float[] samples = new float[lengthSamples];

            for (int i = 0; i < lengthSamples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Exp(-t * 35f);
                float sub = Mathf.Sin(2f * Mathf.PI * (120f - t * 400f) * t) * Mathf.Exp(-t * 20f);
                float noise = (Random.value * 2f - 1f) * envelope;
                samples[i] = Mathf.Clamp(noise * 0.7f + sub * 0.5f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("ProceduralGunshot", lengthSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

