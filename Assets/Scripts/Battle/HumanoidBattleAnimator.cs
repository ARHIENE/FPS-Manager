using UnityEngine;
using UnityEngine.AI;

namespace FPSManager.Battle
{
    /// <summary>
    /// Animates Kevin Iglesias dummy models procedurally:
    /// - Tactical weapon holding stance (arms holding rifle)
    /// - Upper-body / spine aiming pitch towards target
    /// - Running / walking leg stride cycle based on NavMeshAgent velocity
    /// - Weapon recoil impulse on firing
    /// - Hit flinch and collapse death animation
    /// </summary>
    public class HumanoidBattleAnimator : MonoBehaviour
    {
        private NavMeshAgent agent;
        private PlayerHealth health;
        private PlayerMovement movement;
        private WeaponController weapon;

        // Bones
        private Transform spine;
        private Transform chest;
        private Transform head;
        private Transform shoulderR, upperArmR, forearmR, handR;
        private Transform shoulderL, upperArmL, forearmL, handL;
        private Transform thighL, shinL, footL;
        private Transform thighR, shinR, footR;

        // Base local rotations for rest pose
        private Quaternion initSpineRot, initChestRot, initHeadRot;
        private Quaternion initUpperArmR, initForearmR, initHandR;
        private Quaternion initUpperArmL, initForearmL, initHandL;
        private Quaternion initThighL, initShinL;
        private Quaternion initThighR, initShinR;

        private float walkCycle;
        private float recoilAmount;
        private float deathProgress;
        private Vector3 deathFallDir;

        private GameObject overheadUI;
        private RectTransform healthBarFill;
        private UnityEngine.UI.Image fillImageComponent;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<PlayerHealth>();
            movement = GetComponent<PlayerMovement>();
            weapon = GetComponent<WeaponController>();

            LocateBones();
            CreateOverheadHealthBar();
        }

        public void SetTeamColor(Color color)
        {
            if (fillImageComponent != null)
            {
                fillImageComponent.color = color;
            }
        }

        void LocateBones()
        {
            Transform bRoot = transform.Find("Rig/B-root");
            if (bRoot == null) bRoot = transform.Find("HumanDummy/Rig/B-root");
            if (bRoot == null)
            {
                // Recursive search if not in standard path
                bRoot = FindChildRecursive(transform, "B-root");
            }

            if (bRoot == null) return;

            Transform hips = FindChildRecursive(bRoot, "B-hips");
            spine = FindChildRecursive(hips, "B-spine");
            chest = FindChildRecursive(spine, "B-chest");
            head = FindChildRecursive(chest, "B-head");

            shoulderR = FindChildRecursive(chest, "B-shoulder.R");
            upperArmR = FindChildRecursive(shoulderR, "B-upperArm.R");
            forearmR = FindChildRecursive(upperArmR, "B-forearm.R");
            handR = FindChildRecursive(forearmR, "B-hand.R");

            shoulderL = FindChildRecursive(chest, "B-shoulder.L");
            upperArmL = FindChildRecursive(shoulderL, "B-upperArm.L");
            forearmL = FindChildRecursive(upperArmL, "B-forearm.L");
            handL = FindChildRecursive(forearmL, "B-hand.L");

            thighL = FindChildRecursive(hips, "B-thigh.L");
            shinL = FindChildRecursive(thighL, "B-shin.L");
            footL = FindChildRecursive(shinL, "B-foot.L");

            thighR = FindChildRecursive(hips, "B-thigh.R");
            shinR = FindChildRecursive(thighR, "B-shin.R");
            footR = FindChildRecursive(shinR, "B-foot.R");

            // Cache initial rotations
            if (spine != null) initSpineRot = spine.localRotation;
            if (chest != null) initChestRot = chest.localRotation;
            if (head != null) initHeadRot = head.localRotation;

            if (upperArmR != null) initUpperArmR = upperArmR.localRotation;
            if (forearmR != null) initForearmR = forearmR.localRotation;
            if (handR != null) initHandR = handR.localRotation;

            if (upperArmL != null) initUpperArmL = upperArmL.localRotation;
            if (forearmL != null) initForearmL = forearmL.localRotation;
            if (handL != null) initHandL = handL.localRotation;

            if (thighL != null) initThighL = thighL.localRotation;
            if (shinL != null) initShinL = shinL.localRotation;
            if (thighR != null) initThighR = thighR.localRotation;
            if (shinR != null) initShinR = shinR.localRotation;
        }

        Transform FindChildRecursive(Transform parent, string childName)
        {
            if (parent == null) return null;
            if (parent.name == childName) return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChildRecursive(parent.GetChild(i), childName);
                if (found != null) return found;
            }
            return null;
        }

        public void TriggerRecoil()
        {
            recoilAmount = Mathf.Min(recoilAmount + 6f, 15f);
        }

        void Update()
        {
            recoilAmount = Mathf.MoveTowards(recoilAmount, 0f, 30f * Time.deltaTime);

            if (health != null && health.IsDead)
            {
                if (deathProgress < 1f)
                {
                    if (deathProgress == 0f)
                    {
                        deathFallDir = -transform.forward * 0.8f + (Random.value > 0.5f ? transform.right : -transform.right) * 0.3f;
                    }
                    deathProgress = Mathf.MoveTowards(deathProgress, 1f, 2.0f * Time.deltaTime);
                }
                if (overheadUI != null) overheadUI.SetActive(false);
            }
            else
            {
                UpdateHealthBar();
            }
        }

        void LateUpdate()
        {
            if (spine == null) return;

            if (health != null && health.IsDead)
            {
                ApplyDeathPose();
                return;
            }

            ApplyAimAndWeaponPose();
            ApplyLegAnimation();
        }

        void ApplyAimAndWeaponPose()
        {
            float speed = agent != null ? agent.velocity.magnitude : 0f;
            float isMoving = Mathf.Clamp01(speed / 3f);

            // Breathing / slight idle sway
            float sway = Mathf.Sin(Time.time * 2.5f) * 1.5f;

            // Upper arms holding rifle forward in tactical stance
            // Right arm (trigger arm): brought forward and across chest
            if (upperArmR != null)
            {
                Quaternion aimHoldR = Quaternion.Euler(60f - recoilAmount, -25f, -15f + sway);
                upperArmR.localRotation = initUpperArmR * aimHoldR;
            }
            if (forearmR != null)
            {
                Quaternion bendR = Quaternion.Euler(0f, 0f, 45f + recoilAmount * 0.5f);
                forearmR.localRotation = initForearmR * bendR;
            }
            if (handR != null)
            {
                Quaternion gripR = Quaternion.Euler(0f, 15f, -10f);
                handR.localRotation = initHandR * gripR;
            }

            // Left arm (supporting foregrip): extended forward to hold rifle
            if (upperArmL != null)
            {
                Quaternion aimHoldL = Quaternion.Euler(50f, 35f, 20f);
                upperArmL.localRotation = initUpperArmL * aimHoldL;
            }
            if (forearmL != null)
            {
                Quaternion bendL = Quaternion.Euler(0f, 0f, -60f);
                forearmL.localRotation = initForearmL * bendL;
            }
            if (handL != null)
            {
                Quaternion gripL = Quaternion.Euler(0f, -20f, 15f);
                handL.localRotation = initHandL * gripL;
            }

            // Spine & Chest pitch (aiming up/down)
            if (movement != null && movement.aimPivot != null)
            {
                float aimPitch = movement.aimPivot.localEulerAngles.x;
                if (aimPitch > 180f) aimPitch -= 360f;
                aimPitch = Mathf.Clamp(aimPitch, -50f, 50f);

                if (spine != null)
                    spine.localRotation = initSpineRot * Quaternion.Euler(aimPitch * 0.4f, 0f, 0f);
                if (chest != null)
                    chest.localRotation = initChestRot * Quaternion.Euler(aimPitch * 0.5f, 0f, 0f);
                if (head != null)
                    head.localRotation = initHeadRot * Quaternion.Euler(aimPitch * 0.1f, 0f, 0f);
            }
        }

        void ApplyLegAnimation()
        {
            float speed = agent != null ? agent.velocity.magnitude : 0f;
            if (speed > 0.1f)
            {
                walkCycle += Time.deltaTime * speed * 3.5f;
                float legAngle = Mathf.Sin(walkCycle) * 32f * Mathf.Clamp01(speed / 3f);
                float kneeBendL = Mathf.Max(0f, -Mathf.Sin(walkCycle)) * 45f;
                float kneeBendR = Mathf.Max(0f, Mathf.Sin(walkCycle)) * 45f;

                if (thighL != null) thighL.localRotation = initThighL * Quaternion.Euler(legAngle, 0f, 0f);
                if (shinL != null) shinL.localRotation = initShinL * Quaternion.Euler(-kneeBendL, 0f, 0f);

                if (thighR != null) thighR.localRotation = initThighR * Quaternion.Euler(-legAngle, 0f, 0f);
                if (shinR != null) shinR.localRotation = initShinR * Quaternion.Euler(-kneeBendR, 0f, 0f);
            }
            else
            {
                // Return legs to neutral standing stance
                if (thighL != null) thighL.localRotation = Quaternion.Slerp(thighL.localRotation, initThighL, Time.deltaTime * 10f);
                if (shinL != null) shinL.localRotation = Quaternion.Slerp(shinL.localRotation, initShinL, Time.deltaTime * 10f);
                if (thighR != null) thighR.localRotation = Quaternion.Slerp(thighR.localRotation, initThighR, Time.deltaTime * 10f);
                if (shinR != null) shinR.localRotation = Quaternion.Slerp(shinR.localRotation, initShinR, Time.deltaTime * 10f);
            }
        }

        void ApplyDeathPose()
        {
            float t = deathProgress;
            float ease = Mathf.SmoothStep(0f, 1f, t);

            // Collapse to ground backwards
            transform.position += deathFallDir * (Time.deltaTime * (1f - t) * 2f);
            Quaternion fallRot = Quaternion.Euler(-80f, transform.eulerAngles.y, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, fallRot, t * 0.15f);

            if (spine != null) spine.localRotation = Quaternion.Slerp(initSpineRot, initSpineRot * Quaternion.Euler(-30f, 10f, 0f), ease);
            if (upperArmR != null) upperArmR.localRotation = Quaternion.Slerp(upperArmR.localRotation, initUpperArmR * Quaternion.Euler(-20f, 30f, 0f), ease);
            if (upperArmL != null) upperArmL.localRotation = Quaternion.Slerp(upperArmL.localRotation, initUpperArmL * Quaternion.Euler(-20f, -30f, 0f), ease);
            if (thighL != null) thighL.localRotation = Quaternion.Slerp(thighL.localRotation, initThighL * Quaternion.Euler(20f, 15f, 0f), ease);
            if (thighR != null) thighR.localRotation = Quaternion.Slerp(thighR.localRotation, initThighR * Quaternion.Euler(-10f, -15f, 0f), ease);
        }

        void CreateOverheadHealthBar()
        {
            overheadUI = new GameObject("OverheadHealthUI");
            overheadUI.transform.SetParent(transform, false);
            overheadUI.transform.localPosition = new Vector3(0, 2.1f, 0);

            Canvas canvas = overheadUI.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            overheadUI.AddComponent<UnityEngine.UI.CanvasScaler>();

            RectTransform canvasRt = overheadUI.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(1.2f, 0.2f);
            canvasRt.localScale = Vector3.one * 0.8f;

            // Background bar
            GameObject bgObj = new GameObject("Bg");
            bgObj.transform.SetParent(overheadUI.transform, false);
            UnityEngine.UI.Image bgImg = bgObj.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            RectTransform bgRt = bgObj.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            // Fill bar
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(bgObj.transform, false);
            UnityEngine.UI.Image fillImg = fillObj.AddComponent<UnityEngine.UI.Image>();
            fillImg.color = health != null && health.teamId == 0 ? new Color(0.2f, 0.6f, 1f) : new Color(1f, 0.35f, 0.2f);
            fillImageComponent = fillImg;
            healthBarFill = fillObj.GetComponent<RectTransform>();
            healthBarFill.anchorMin = new Vector2(0, 0);
            healthBarFill.anchorMax = new Vector2(1, 1);
            healthBarFill.offsetMin = new Vector2(0.02f, 0.02f);
            healthBarFill.offsetMax = new Vector2(-0.02f, -0.02f);
            healthBarFill.pivot = new Vector2(0, 0.5f);
        }

        void UpdateHealthBar()
        {
            if (overheadUI == null || health == null) return;

            // Billboard towards active spectator camera
            Camera cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam != null)
            {
                overheadUI.transform.rotation = Quaternion.LookRotation(overheadUI.transform.position - cam.transform.position);
            }

            if (healthBarFill != null)
            {
                float pct = Mathf.Clamp01(health.CurrentHealth / health.maxHealth);
                healthBarFill.anchorMax = new Vector2(pct, 1f);
            }
        }
    }
}
