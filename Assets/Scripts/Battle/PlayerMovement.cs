using UnityEngine;
using UnityEngine.AI;

namespace FPSManager.Battle
{
    // 1VS1 Game의 FPSController.cs를 Photon 제거 + NavMeshAgent 기반으로 포팅.
    // 위치 이동은 NavMeshAgent(및 AIBrain)가 담당하고, 이 스크립트는
    // 조준 회전 적용과 이동 속도 기반 명중률 스프레드 계산만 담당한다.
    [RequireComponent(typeof(NavMeshAgent))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("조준 설정")]
        public Transform aimPivot;
        public float turnSpeed = 720f;
        public float pitchSpeed = 720f;

        [Header("정확도 설정 (FPSController와 동일한 개념)")]
        public float maxSpread = 3f;
        public float spreadRecoverySpeed = 5f;

        private NavMeshAgent agent;
        private float currentSpread;
        private float currentPitch;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;

            if (aimPivot == null)
            {
                Transform found = transform.Find("AimPivot");
                if (found != null) aimPivot = found;
            }
        }

        void Update()
        {
            UpdateSpread();
        }

        void UpdateSpread()
        {
            float speedRatio = agent.speed > 0.01f ? agent.velocity.magnitude / agent.speed : 0f;
            float targetSpread = Mathf.Lerp(0f, maxSpread, speedRatio);
            currentSpread = Mathf.MoveTowards(currentSpread, targetSpread, spreadRecoverySpeed * Time.deltaTime * 4f);
        }

        // 교전 중: 몸(수평)과 조준 피벗(수직)을 목표 지점으로 회전
        public void AimAt(Vector3 worldPoint)
        {
            Vector3 toTarget = worldPoint - transform.position;
            Vector3 flatDir = new Vector3(toTarget.x, 0f, toTarget.z);
            if (flatDir.sqrMagnitude > 0.0001f)
            {
                Quaternion desiredYaw = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredYaw, turnSpeed * Time.deltaTime);
            }

            if (aimPivot != null)
            {
                Vector3 aimDir = worldPoint - aimPivot.position;
                float horizontalDist = new Vector2(aimDir.x, aimDir.z).magnitude;
                float desiredPitch = -Mathf.Atan2(aimDir.y, Mathf.Max(horizontalDist, 0.001f)) * Mathf.Rad2Deg;
                currentPitch = Mathf.MoveTowards(currentPitch, desiredPitch, pitchSpeed * Time.deltaTime);
                aimPivot.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
            }
        }

        // 교전 대상이 없을 때: 이동 방향을 바라보도록
        public void FaceMoveDirection()
        {
            Vector3 vel = agent.velocity;
            vel.y = 0f;
            if (vel.sqrMagnitude > 0.05f)
            {
                Quaternion desiredYaw = Quaternion.LookRotation(vel.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredYaw, turnSpeed * Time.deltaTime);
            }

            if (aimPivot != null)
            {
                currentPitch = Mathf.MoveTowards(currentPitch, 0f, pitchSpeed * Time.deltaTime);
                aimPivot.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
            }
        }

        public float GetCurrentSpread() => currentSpread;
        public bool IsRunning() => agent.speed > 0.01f && agent.velocity.magnitude / agent.speed > 0.6f;
        public bool IsCrouching() => false;
    }
}
