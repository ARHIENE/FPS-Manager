using UnityEngine;

namespace FPSManager.Battle
{
    // 피격 시 취할 행동
    public enum CombatReaction
    {
        Fight,          // 반격 (교전 유지)
        TakeCover,      // 엄폐 이탈 (근처 엄폐물로 후퇴)
        Retreat,        // 완전 후퇴/이탈
        PeekAndFight    // 역공격 (엄폐 후 재교전)
    }

    // 피격 이벤트에 대한 Utility AI 스코어링. AIBrain이 매 피격마다 컨텍스트를 채워 Evaluate를 호출한다.
    public static class CombatReactionEvaluator
    {
        public struct Context
        {
            public float healthPct;        // 0~1, 남은 체력 비율
            public bool attackerKnown;      // 공격자 위치를 파악했는지 (라인오브사이트 확보)
            public bool coverAvailable;     // 근처에 쓸만한 엄폐물이 있는지
            public float coverDistance;     // 가장 가까운 엄폐물까지 거리 (없으면 큰 값)
            public float clutch;            // 0~1, 높을수록 공격적 성향
            public float positioning;       // 0~1, 높을수록 엄폐 활용이 정교함
            public int nearbyEnemyCount;    // 교전 중인 적 인원수 (1v1 프로토타입에서는 1로 고정)
        }

        public static CombatReaction Evaluate(Context ctx)
        {
            float fight = ScoreFight(ctx);
            float takeCover = ScoreTakeCover(ctx);
            float retreat = ScoreRetreat(ctx);
            float peek = ScorePeekAndFight(ctx);

            return PickWeightedTop(
                (CombatReaction.Fight, fight),
                (CombatReaction.TakeCover, takeCover),
                (CombatReaction.Retreat, retreat),
                (CombatReaction.PeekAndFight, peek));
        }

        static float ScoreFight(Context ctx)
        {
            float score = ctx.healthPct * 1.0f;
            score += ctx.attackerKnown ? 0.6f : -0.4f;
            score += ctx.clutch * 0.5f;
            score -= Mathf.Max(0, ctx.nearbyEnemyCount - 1) * 0.5f;
            return score;
        }

        static float ScoreTakeCover(Context ctx)
        {
            float score = (1f - ctx.healthPct) * 0.6f;
            score += ctx.attackerKnown ? 0.1f : 0.5f;
            score += ctx.coverAvailable ? 0.7f : -0.5f;
            score += ctx.positioning * 0.5f;
            score -= ctx.clutch * 0.2f;
            score -= Mathf.Clamp01(ctx.coverDistance / 20f) * 0.3f;
            return score;
        }

        static float ScoreRetreat(Context ctx)
        {
            float score = (1f - ctx.healthPct) * 0.8f;
            score += Mathf.Max(0, ctx.nearbyEnemyCount - 1) * 0.5f;
            score += (1f - ctx.clutch) * 0.6f;
            return score;
        }

        static float ScorePeekAndFight(Context ctx)
        {
            float score = ctx.healthPct * 0.5f;
            score += ctx.coverAvailable ? 0.5f : -0.3f;
            score += ctx.clutch * 0.7f;
            score += ctx.positioning * 0.3f;
            score += ctx.attackerKnown ? 0.3f : 0f;
            return score;
        }

        // 최고점만 고르면 예측 가능해지므로, 상위 1~2개 후보 사이에서 점수 비례 확률로 흔들림을 준다.
        static CombatReaction PickWeightedTop(params (CombatReaction reaction, float score)[] candidates)
        {
            System.Array.Sort(candidates, (a, b) => b.score.CompareTo(a.score));

            float s1 = candidates[0].score;
            float s2 = candidates[1].score;

            float min = Mathf.Min(0f, s2);
            float w1 = (s1 - min) + 0.01f;
            float w2 = (s2 - min) + 0.01f;

            float roll = Random.value * (w1 + w2);
            return roll < w1 ? candidates[0].reaction : candidates[1].reaction;
        }
    }
}
