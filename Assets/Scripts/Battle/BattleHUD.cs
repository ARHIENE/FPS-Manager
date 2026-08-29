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
            match = MatchManager.Instance != null ? MatchManager.Instance : FindFirstObjectByType<MatchManager>();
            spectator = SpectatorCamera.Instance != null ? SpectatorCamera.Instance : FindFirstObjectByType<SpectatorCamera>();

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

                match.OnRoundFinished += (winnerMsg, winnerCol) =>
                {
                    roundBannerText = winnerMsg;
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

            DrawTopHeader();
            DrawKillFeed();
            DrawBanner();
            DrawControlsHint();
            DrawSpectatedPlayerCard();
        }

        void DrawTopHeader()
        {
            float screenW = Screen.width;
            float panelW = 480f;
            float panelH = 70f;
            Rect panelRect = new Rect((screenW - panelW) / 2f, 15f, panelW, panelH);

            DrawSolidRect(panelRect, new Color(0.08f, 0.08f, 0.12f, 0.85f));

            int aliveBlue = match != null ? match.CountAlive(match.GetTeam(0)) : 0;
            int aliveRed = match != null ? match.CountAlive(match.GetTeam(1)) : 0;
            int scoreBlue = match != null ? match.ScoreTeamA : 0;
            int scoreRed = match != null ? match.ScoreTeamB : 0;
            int round = match != null ? match.CurrentRound : 1;

            // Team Blue Side
            GUI.color = new Color(0.3f, 0.7f, 1f);
            GUI.Label(new Rect(panelRect.x + 10f, panelRect.y + 10f, 160f, 25f), $"TEAM BLUE ({aliveBlue}/5)", headerStyle);
            GUI.Label(new Rect(panelRect.x + 10f, panelRect.y + 35f, 160f, 30f), scoreBlue.ToString(), scoreStyle);

            // Center Round Info
            GUI.color = Color.white;
            GUI.Label(new Rect(panelRect.x + 180f, panelRect.y + 12f, 120f, 22f), $"ROUND {round}", headerStyle);
            GUI.Label(new Rect(panelRect.x + 180f, panelRect.y + 38f, 120f, 25f), "VS", headerStyle);

            // Team Red Side
            GUI.color = new Color(1f, 0.4f, 0.2f);
            GUI.Label(new Rect(panelRect.x + panelW - 170f, panelRect.y + 10f, 160f, 25f), $"TEAM RED ({aliveRed}/5)", headerStyle);
            GUI.Label(new Rect(panelRect.x + panelW - 170f, panelRect.y + 35f, 160f, 30f), scoreRed.ToString(), scoreStyle);

            GUI.color = Color.white;
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
            GUI.Label(new Rect(bannerRect.x, bannerRect.y + 65f, bannerW, 30f), "Press [SPACE] or [R] to Restart Immediately", subBannerStyle);
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
            GUI.Label(new Rect(rect.x + 10f, rect.y + 70f, panelW - 20f, 18f), "1-5: Team Blue / 6-0: Team Red", infoStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 90f, panelW - 20f, 18f), "ESC: Free Fly | R: Restart Match", infoStyle);
        }

        void DrawSpectatedPlayerCard()
        {
            if (spectator == null || !spectator.IsPossessing || spectator.SpectatedPlayer == null) return;

            PlayerHealth p = spectator.SpectatedPlayer;
            float cardW = 280f;
            float cardH = 80f;
            Rect rect = new Rect(Screen.width - cardW - 20f, Screen.height - cardH - 20f, cardW, cardH);

            DrawSolidRect(rect, new Color(0.08f, 0.08f, 0.12f, 0.85f));

            Color teamCol = p.teamId == 0 ? new Color(0.3f, 0.7f, 1f) : new Color(1f, 0.4f, 0.2f);
            GUI.color = teamCol;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, cardW - 24f, 22f), $"SPECTATING: {p.name}", headerStyle);

            GUI.color = Color.white;
            float hpPct = Mathf.Clamp01(p.CurrentHealth / p.maxHealth);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 32f, cardW - 24f, 18f), $"HP: {Mathf.CeilToInt(p.CurrentHealth)} / {p.maxHealth}", infoStyle);

            // HP Bar Background
            Rect hpBg = new Rect(rect.x + 12f, rect.y + 54f, cardW - 24f, 14f);
            DrawSolidRect(hpBg, new Color(0.2f, 0.2f, 0.2f, 1f));

            // HP Bar Fill
            Rect hpFill = new Rect(rect.x + 12f, rect.y + 54f, (cardW - 24f) * hpPct, 14f);
            DrawSolidRect(hpFill, teamCol);
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
