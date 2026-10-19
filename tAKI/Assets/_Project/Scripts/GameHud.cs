using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Taki
{
    public sealed class GameHud : MonoBehaviour
    {
        public GameController game;
        public TMP_Text altitude;
        public TMP_Text timeLabel;
        public TMP_Text speedLabel;
        public Color speedLowColor = Color.white;
        public Color speedMaxColor = new Color(1, .15f, .12f);
        public TMP_Text comboLabel;
        public Color comboColor = new Color(1, .8f, .3f);
        public TMP_Text readyPrompt;
        public TMP_Text notification;
        public TMP_Text clearTime;
        public TMP_Text clearTitle;
        public TMP_Text clearLabel;
        public GameObject clearPanel;
        public Image progressFill;
        public Image[] collectionCards;
        public TMP_Text[] collectionLetters;
        public VolumeDragControl VolumeControl { get; private set; }
        float notificationUntil;
        int previousCombo;
        float comboPulse;
        Vector3 comboScale;
        bool comboScaleCaptured;

        void Update()
        {
            EnsureFeedbackLabels();
            if (VolumeControl == null && game.Audio != null && game.Audio.IsConfigured)
                VolumeControl = VolumeDragControl.Create(transform, game, readyPrompt.font);
            if (VolumeControl != null) VolumeControl.gameObject.SetActive(game.State == GameState.Ready);
            float height = Mathf.Max(0, game.player.transform.position.y);
            // Floor the current distance so rounding cannot show the summit distance early.
            int meters = Mathf.FloorToInt(game.settings.DisplayedHeight(height));
            altitude.text = $"{meters:000} / {game.settings.displayedGoalMeters:000} m";
            timeLabel.text = $"{game.Elapsed:00.0} s";
            float speed = game.player.ActualVerticalSpeed;
            speedLabel.text = $"UP {game.settings.DisplayedVerticalSpeed(speed):0.0} m/s";
            speedLabel.color = Color.Lerp(speedLowColor, speedMaxColor, game.settings.RiseSpeedFraction(speed));
            int combo = game.Combo.Count;
            if (combo > previousCombo) comboPulse = 1;
            else if (combo == 0) comboPulse = 0;
            previousCombo = combo;
            comboLabel.text = $"{combo} COMBO";
            comboLabel.color = Color.Lerp(comboColor, Color.white, comboPulse);
            comboLabel.rectTransform.localScale = comboScale * (1 + .22f * comboPulse);
            comboLabel.gameObject.SetActive(game.State == GameState.Playing && combo > 0);
            comboPulse = Mathf.MoveTowards(comboPulse, 0, Time.unscaledDeltaTime * 4);
            progressFill.rectTransform.anchorMax = new Vector2(game.Progress, 1);
            readyPrompt.gameObject.SetActive(game.State == GameState.Ready);
            clearPanel.SetActive(game.State == GameState.Clear);
            if (game.State == GameState.Clear) clearTime.text = $"{game.Elapsed:0.0} s   /   {game.Collection.CompletedSets} AKI";
            notification.gameObject.SetActive(game.State == GameState.Playing && Time.time < notificationUntil);
            for (int i = 0; i < collectionCards.Length; i++)
            {
                bool has = game.Collection.Has((ItemKind)i);
                Color color = i < game.settings.items.Length ? game.settings.items[i].displayColor : Color.white;
                collectionCards[i].color = has ? color : new Color(.06f, .15f, .18f, .9f);
                collectionLetters[i].color = has ? new Color(.03f, .12f, .14f) : color;
            }
        }

        public void EnsureFeedbackLabels()
        {
            EnsureSpeedLabel();
            EnsureComboLabel();
        }

        void EnsureSpeedLabel()
        {
            if (speedLabel != null) return;
            var existing = timeLabel.transform.parent.Find("RiseSpeed");
            if (existing != null) speedLabel = existing.GetComponent<TMP_Text>();
            if (speedLabel != null) return;
            speedLabel = Instantiate(timeLabel, timeLabel.transform.parent);
            speedLabel.name = "RiseSpeed";
            speedLabel.rectTransform.anchoredPosition += Vector2.down *
                (Mathf.Max(32, timeLabel.rectTransform.sizeDelta.y) + 12);
            speedLabel.rectTransform.sizeDelta = new Vector2(Mathf.Max(300, timeLabel.rectTransform.sizeDelta.x),
                timeLabel.rectTransform.sizeDelta.y);
            // Keep the speed beside the other HUD labels and underneath the clear overlay.
            speedLabel.transform.SetSiblingIndex(timeLabel.transform.GetSiblingIndex() + 1);
            speedLabel.raycastTarget = false;
        }

        void EnsureComboLabel()
        {
            if (comboLabel == null)
            {
                var existing = timeLabel.transform.parent.Find("Combo");
                if (existing != null) comboLabel = existing.GetComponent<TMP_Text>();
                if (comboLabel == null)
                {
                    comboLabel = Instantiate(speedLabel, speedLabel.transform.parent);
                    comboLabel.name = "Combo";
                    comboLabel.rectTransform.anchoredPosition += Vector2.down *
                        (Mathf.Max(32, speedLabel.rectTransform.sizeDelta.y) + 28);
                    comboLabel.transform.SetSiblingIndex(speedLabel.transform.GetSiblingIndex() + 1);
                    comboLabel.raycastTarget = false;
                }
            }
            if (comboScaleCaptured) return;
            comboScale = comboLabel.rectTransform.localScale;
            comboScaleCaptured = true;
        }

        public void Notify(string message, Color color)
        {
            notification.text = message;
            notification.color = color;
            notificationUntil = Time.time + .85f;
        }
    }
}
