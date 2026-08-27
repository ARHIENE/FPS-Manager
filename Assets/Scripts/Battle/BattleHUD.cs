using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPSManager.Battle
{
    public class BattleHUD : MonoBehaviour
    {
        private class KillFeedItem
        {
            public string text;
            public Color color;
            public float time;
        }

        private MatchManager match;
        private SpectatorCamera spectator;
        private ZoneManager zone;
        private readonly List<KillFeedItem> killFeed = new List<KillFeedItem>();

        private GUIStyle headerStyle;
        private GUIStyle scoreStyle;
        private GUIStyle killFeedStyle;
        private GUIStyle bannerStyle;
        private GUIStyle subBannerStyle;
        private GUIStyle infoStyle;
        private GUIStyle boxStyle;
        private Texture2D whiteTex;

        private string roundBannerText = "";
        private Color roundBannerColor = Color.white;
        private float roundBannerTime = 0f;

        void Awake()
        {
            match = MatchManager.Instance != null ? MatchManager.Instance : FindAnyObjectByType<MatchManager>();
            spectator = SpectatorCamera.Instance != null ? SpectatorCamera.Instance : FindAnyObjectByType<SpectatorCamera>();
            zone = ZoneManager.Instance != null ? ZoneManager.Instance : FindAnyObjectByType<ZoneManager>();

            whiteTex = new Texture2D(1, 1);
            whiteTex.SetPixel(0, 0, Color.white);
            whiteTex.Apply();

            if (match != null)
            {
                match.OnKillFeedEntry += (msg, col, isHeadshot) =>
                {
                    killFeed.Insert(0, new KillFeedItem { text = msg, color = col, time = Time.time });
                    if (killFeed.Count > 6) killFeed.RemoveAt(killFeed.Count - 1);
                };

                match.OnTeamEliminated += (teamId, remaining) =>
                {
                    string msg = $"☠ TEAM {teamId + 1} 탈락 (생존 {remaining}팀)";
                    killFeed.Insert(0, new KillFeedItem { text = msg, color = Color.white, time = Time.time });
                    if (killFeed.Count > 6) killFeed.RemoveAt(killFeed.Count - 1);
                };

                match.OnMatchEnded += (winningTeamId, winnerCol) =>
                {
                    roundBannerText = $"TEAM {winningTeamId + 1} WINS!";
                    roundBannerColor = winnerCol;
                    roundBannerTime = Time.time;
                };

                match.OnMatchStarted += () =>
                {
                    roundBannerText = "";
                };
            }
        }

        void InitStyles()
        {
            if (headerStyle != null) return;

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            scoreStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.yellow }
            };

            killFeedStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            bannerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 38,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            subBannerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Normal,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f, 0.9f) }
            };

            infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f, 0.85f) }
            };

            boxStyle = new GUIStyle(GUI.skin.box);
        }

        void OnGUI()
        {
            InitStyles();

            if (match == null) match = MatchManager.Instance;
            if (spectator == null) spectator = SpectatorCamera.Instance;
            if (zone == null) zone = ZoneManager.Instance;

            DrawTopHeader();
            DrawZonePanel();
            DrawKillFeed();
            DrawBanner();
            DrawControlsHint();
            DrawSpectatedPlayerCard();
        }

        void DrawTopHeader()
        {
            float screenW = Screen.width;
            float panelW = 320f;
            float panelH = 70f;
            Rect panelRect = new Rect((screenW - panelW) / 2f, 15f, panelW, panelH);

            DrawSolidRect(panelRect, new Color(0.08f, 0.08f, 0.12f, 0.85f));

            int aliveTeams = match != null ? match.AliveTeamCount : 0;
            int totalTeams = match != null ? match.TotalTeamCount : 0;

            GUI.color = Color.white;
            GUI.Label(new Rect(panelRect.x, panelRect.y + 8f, panelW, 25f), "BATTLE ROYALE", headerStyle);
            GUI.Label(new Rect(panelRect.x, panelRect.y + 34f, panelW, 30f), $"{aliveTeams} / {totalTeams} TEAMS LEFT", scoreStyle);
        }

        void DrawZonePanel()
        {
            if (zone == null) return;

            float panelW = 220f;
            float panelH = 58f;
            Rect rect = new Rect(20f, 15f, panelW, panelH);

            DrawSolidRect(rect, new Color(0.08f, 0.08f, 0.12f, 0.8f));

            string phaseText = zone.CurrentPhaseIndex >= 0 ? $"자기장 PHASE {zone.CurrentPhaseIndex + 1}" : "자기장 대기 중";
            string stateLabel = zone.IsShrinking ? "축소 중" : "다음 축소까지";
            int seconds = Mathf.Max(0, Mathf.CeilToInt(zone.PhaseTimeRemaining));

            GUI.color = new Color(0.4f, 0.75f, 1f);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, panelW - 20f, 22f), phaseText, infoStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 28f, panelW - 20f, 22f), $"{stateLabel}: {seconds}s", infoStyle);
        }

        void DrawKillFeed()
        {
            float x = Screen.width - 340f;
            float y = 15f;

            for (int i = 0; i < killFeed.Count; i++)
            {
                var item = killFeed[i];
                float age = Time.time - item.time;
                if (age > 6f) continue;

                float alpha = age > 4.5f ? Mathf.Clamp01((6f - age) / 1.5f) : 1f;
                Rect itemRect = new Rect(x, y + i * 28f, 320f, 24f);

                Color bg = new Color(0.08f, 0.08f, 0.1f, 0.75f * alpha);
                DrawSolidRect(itemRect, bg);

                killFeedStyle.normal.textColor = new Color(item.color.r, item.color.g, item.color.b, alpha);
                GUI.Label(new Rect(itemRect.x + 5f, itemRect.y + 2f, itemRect.width - 10f, itemRect.height), item.text, killFeedStyle);
            }
        }

        void DrawBanner()
        {
            if (string.IsNullOrEmpty(roundBannerText)) return;

            float bannerW = 600f;
            float bannerH = 110f;
            Rect bannerRect = new Rect((Screen.width - bannerW) / 2f, Screen.height * 0.35f, bannerW, bannerH);

            DrawSolidRect(bannerRect, new Color(0.05f, 0.05f, 0.08f, 0.92f));

            bannerStyle.normal.textColor = roundBannerColor;
            GUI.Label(new Rect(bannerRect.x, bannerRect.y + 15f, bannerW, 45f), roundBannerText, bannerStyle);
            GUI.Label(new Rect(bannerRect.x, bannerRect.y + 65f, bannerW, 30f), "Press [SPACE] to Restart Immediately", subBannerStyle);
        }

        void DrawControlsHint()
        {
            float panelW = 300f;
            float panelH = 120f;
            Rect rect = new Rect(20f, Screen.height - panelH - 20f, panelW, panelH);

            DrawSolidRect(rect, new Color(0.08f, 0.08f, 0.12f, 0.75f));

            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, panelW - 20f, 20f), "<b>SPECTATOR CONTROLS</b>", infoStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 30f, panelW - 20f, 18f), "WASD + Mouse : Fly Camera", infoStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 50f, panelW - 20f, 18f), "Q / E / Shift : Down / Up / Fast", infoStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 70f, panelW - 20f, 18f), "Tab: Cycle Teams | ←/→: Cycle Members", infoStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 90f, panelW - 20f, 18f), "ESC: Free Fly", infoStyle);
        }

        void DrawSpectatedPlayerCard()
        {
            if (spectator == null || !spectator.IsPossessing || spectator.SpectatedPlayer == null) return;

            PlayerHealth p = spectator.SpectatedPlayer;
            bool inDanger = zone != null && zone.CurrentPhaseIndex >= 0 && !zone.IsInsideZone(p.transform.position);

            float cardW = 280f;
            float cardH = inDanger ? 100f : 80f;
            Rect rect = new Rect(Screen.width - cardW - 20f, Screen.height - cardH - 20f, cardW, cardH);

            DrawSolidRect(rect, new Color(0.08f, 0.08f, 0.12f, 0.85f));

            Color teamCol = match != null ? match.GetTeamColorOf(p.teamId) : Color.white;
            GUI.color = teamCol;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, cardW - 24f, 22f), $"SPECTATING: {p.name} (TEAM {p.teamId + 1})", headerStyle);

            GUI.color = Color.white;
            float hpPct = Mathf.Clamp01(p.CurrentHealth / p.maxHealth);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 32f, cardW - 24f, 18f), $"HP: {Mathf.CeilToInt(p.CurrentHealth)} / {p.maxHealth}", infoStyle);

            // HP Bar Background
            Rect hpBg = new Rect(rect.x + 12f, rect.y + 54f, cardW - 24f, 14f);
            DrawSolidRect(hpBg, new Color(0.2f, 0.2f, 0.2f, 1f));

            // HP Bar Fill
            Rect hpFill = new Rect(rect.x + 12f, rect.y + 54f, (cardW - 24f) * hpPct, 14f);
            DrawSolidRect(hpFill, teamCol);

            if (inDanger)
            {
                GUI.color = new Color(1f, 0.35f, 0.35f);
                GUI.Label(new Rect(rect.x + 12f, rect.y + 76f, cardW - 24f, 20f), "⚠ 자기장 피해 중", infoStyle);
                GUI.color = Color.white;
            }
        }

        void DrawSolidRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, whiteTex);
            GUI.color = prev;
        }
    }
}
