using UnityEngine;

namespace FPSManager.Battle
{
    // 캐릭터 모델의 모든 렌더러에 팀 색상을 입힌다. 머티리얼 인스턴스를 만들지 않고
    // MaterialPropertyBlock을 쓰기 때문에 80명 규모로 스폰돼도 머티리얼이 늘어나지 않는다.
    public static class TeamColorApplier
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit / Unlit
        static readonly int ColorId = Shader.PropertyToID("_Color"); // Standard / Built-in

        public static void Apply(GameObject target, Color color)
        {
            if (target == null) return;

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var block = new MaterialPropertyBlock();

            foreach (var renderer in renderers)
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                renderer.SetPropertyBlock(block);
            }

            var anim = target.GetComponent<HumanoidBattleAnimator>();
            if (anim != null)
            {
                anim.SetTeamColor(color);
            }
        }
    }
}
