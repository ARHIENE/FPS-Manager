using UnityEngine;

namespace FPSManager.Battle
{
    // 선수 개인 성향 스탯. 1차 범위로 피격 반응 결정에 쓰이는 클러치/포지셔닝만 구현.
    // 에임/반응속도/팀워크 등 나머지는 매니저 메타 루프의 스탯 시스템이 붙을 때 이 컴포넌트에 확장한다.
    public class PlayerCombatStats : MonoBehaviour
    {
        [Header("전투 성향 (0~1)")]
        [Tooltip("높을수록 불리한 상황에서도 반격/역공격을 선호")]
        [Range(0f, 1f)] public float clutch = 0.5f;

        [Tooltip("높을수록 엄폐물을 정교하게 활용")]
        [Range(0f, 1f)] public float positioning = 0.5f;
    }
}
