using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSManager.Battle
{
    // 관전 카메라: 자유 비행 + 플레이어 1인칭/3인칭 관전 기능
    public class SpectatorCamera : MonoBehaviour
    {
        public static SpectatorCamera Instance { get; private set; }

        [Header("자유비행 설정")]
        public float flySpeed = 16f;
        public float boostMultiplier = 2.5f;
        public float lookSensitivity = 1.8f;

        public PlayerHealth SpectatedPlayer { get; private set; }
        public bool IsPossessing => possessedCamera != null;

        private Camera cam;
        private float yaw;
        private float pitch;
        private Camera possessedCamera;

        private int selectedTeamIndex = -1;
        private int selectedMemberIndex;

        void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x;
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ReturnToFreeFly();
            }

            if (possessedCamera != null)
            {
                HandlePossessInput();
                return;
            }

            HandleLook();
            HandleMove();
            HandlePossessInput();
        }

        void HandleLook()
        {
            if (Mouse.current == null) return;
            Vector2 delta = Mouse.current.delta.ReadValue();
            if (Mouse.current.rightButton.isPressed || Mouse.current.leftButton.isPressed)
            {
                yaw += delta.x * lookSensitivity * 0.1f;
                pitch -= delta.y * lookSensitivity * 0.1f;
                pitch = Mathf.Clamp(pitch, -85f, 85f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
        }

        void HandleMove()
        {
            if (Keyboard.current == null) return;

            Vector3 move = Vector3.zero;
            if (Keyboard.current.wKey.isPressed) move += transform.forward;
            if (Keyboard.current.sKey.isPressed) move -= transform.forward;
            if (Keyboard.current.aKey.isPressed) move -= transform.right;
            if (Keyboard.current.dKey.isPressed) move += transform.right;
            if (Keyboard.current.eKey.isPressed) move += Vector3.up;
            if (Keyboard.current.qKey.isPressed) move -= Vector3.up;

            float speed = flySpeed * (Keyboard.current.leftShiftKey.isPressed ? boostMultiplier : 1f);
            if (move.sqrMagnitude > 0.001f)
            {
                transform.position += move.normalized * speed * Time.deltaTime;
            }
        }

        void HandlePossessInput()
        {
            if (Keyboard.current == null || MatchManager.Instance == null) return;

            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                CycleTeam(1);
            }

            if (possessedCamera != null)
            {
                if (Keyboard.current.rightArrowKey.wasPressedThisFrame) CycleMember(1);
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame) CycleMember(-1);
            }
        }

        // Tab: 다음 팀으로 이동 (해당 팀의 생존자를 우선 빙의)
        void CycleTeam(int direction)
        {
            var teams = MatchManager.Instance.GetTeams();
            if (teams.Count == 0) return;

            selectedTeamIndex = (selectedTeamIndex + direction + teams.Count) % teams.Count;
            selectedMemberIndex = 0;
            PossessBestInTeam();
        }

        // 좌/우 화살표: 현재 팀 안에서 팀원 순환
        void CycleMember(int direction)
        {
            var teams = MatchManager.Instance.GetTeams();
            if (selectedTeamIndex < 0 || selectedTeamIndex >= teams.Count) return;

            var members = teams[selectedTeamIndex].members;
            if (members.Count == 0) return;

            selectedMemberIndex = (selectedMemberIndex + direction + members.Count) % members.Count;
            PossessMember(members[selectedMemberIndex]);
        }

        void PossessBestInTeam()
        {
            var teams = MatchManager.Instance.GetTeams();
            if (selectedTeamIndex < 0 || selectedTeamIndex >= teams.Count) return;

            var members = teams[selectedTeamIndex].members;
            if (members.Count == 0) return;

            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] != null && !members[i].IsDead)
                {
                    selectedMemberIndex = i;
                    PossessMember(members[i]);
                    return;
                }
            }

            selectedMemberIndex = 0;
            PossessMember(members[0]);
        }

        void PossessMember(PlayerHealth target)
        {
            if (target == null) return;

            Camera targetCam = target.GetComponentInChildren<Camera>(true);
            if (targetCam == null) return;

            if (possessedCamera != null) possessedCamera.enabled = false;

            possessedCamera = targetCam;
            possessedCamera.enabled = true;
            cam.enabled = false;
            SpectatedPlayer = target;
        }

        public void ReturnToFreeFly()
        {
            if (possessedCamera != null)
            {
                possessedCamera.enabled = false;
                possessedCamera = null;
            }
            SpectatedPlayer = null;
            selectedTeamIndex = -1;
            if (cam != null) cam.enabled = true;
        }
    }
}
