using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Taki
{
    [DefaultExecutionOrder(20)]
    public sealed class GameHudEffects : MonoBehaviour
    {
        public Image ReserveFill { get; private set; }
        public int FloatingPickupCount { get; private set; }
        public bool GatheringLetters => gatherAge < .5f;
        GameController game;
        GameFeel feel;
        GameHud hud;
        RectTransform root, reserveRoot;
        Image speedGlow, reserveTrack;
        TMP_Text milestoneLabel;
        readonly System.Random random = new System.Random(2718);
        readonly FloatingLetter[] floating = new FloatingLetter[16];
        readonly TMP_Text[] gathering = new TMP_Text[3];
        readonly Vector2[] gatherFrom = new Vector2[3];
        readonly float[] cardFlash = new float[3];
        readonly Vector3[] cardScale = new Vector3[3];
        readonly Image[] sparks = new Image[12];
        WaterLine[] lines;
        Vector3 speedScale;
        float speedPunch, reserveFlash, milestoneAge = 10, gatherAge = 10;
        float waterIntensity;
        int nextFloating;
        Camera viewCamera;

        sealed class FloatingLetter
        {
            public TMP_Text label;
            public Vector3 worldPosition;
            public Vector2 screenPosition;
            public bool anchored;
            public Color color;
            public float age = 10;
        }
        sealed class WaterLine
        {
            public Image image;
            public float x, y, length, speed;
        }

        public void Initialize(GameController owner, GameFeel feedback)
        {
            if (game != null) return;
            game = owner; feel = feedback; hud = game.hud;
            viewCamera = Camera.main;
            root = Rect("ScreenFeedback", transform, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.SetAsFirstSibling();
            speedScale = hud.speedLabel.rectTransform.localScale;
            CreateWaterLines();
            speedGlow = Image("SpeedGlow", root, new Vector2(330, 65), feel.Settings.boostColor, true);
            reserveRoot = Rect("MomentumReserve", root, new Vector2(220, 6));
            reserveTrack = reserveRoot.gameObject.AddComponent<Image>();
            reserveTrack.raycastTarget = false; reserveTrack.color = new Color(.15f, .28f, .28f, .8f);
            ReserveFill = Image("Fill", reserveRoot, Vector2.zero, feel.Settings.boostColor);
            ReserveFill.rectTransform.anchorMin = Vector2.zero;
            ReserveFill.rectTransform.anchorMax = Vector2.one;
            ReserveFill.rectTransform.offsetMin = ReserveFill.rectTransform.offsetMax = Vector2.zero;
            for (int i = 0; i < floating.Length; i++)
                floating[i] = new FloatingLetter { label = Label("PickupLetter", 42) };
            for (int i = 0; i < gathering.Length; i++) gathering[i] = Label("GatherAKI", 58);
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i] = Image("BoostSpark", root, new Vector2(5, 55), feel.Settings.boostColor, true);
                sparks[i].gameObject.SetActive(false);
            }
            milestoneLabel = Label("ComboMilestone", 44);
            for (int i = 0; i < cardScale.Length && i < hud.collectionCards.Length; i++)
                cardScale[i] = hud.collectionCards[i].rectTransform.localScale;
        }

        void CreateWaterLines()
        {
            lines = new WaterLine[Mathf.Clamp(feel.Settings.waterLineCount, 0, 64)];
            for (int i = 0; i < lines.Length; i++)
            {
                float edge = Mathf.Lerp(.025f, .17f, Sample());
                lines[i] = new WaterLine {
                    image = Image("WaterSpeedLine", root, new Vector2(3, 100), Color.white, true),
                    x = i % 2 == 0 ? edge : 1 - edge, y = Sample() * 1.4f - .2f,
                    length = Mathf.Lerp(50, 140, Sample()), speed = Mathf.Lerp(.5f, 1.1f, Sample())
                };
            }
        }

        float Sample() => (float)random.NextDouble();

        TMP_Text Label(string name, float size)
        {
            var text = Instantiate(hud.notification, root);
            text.name = name;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(.5f, .5f);
            text.rectTransform.sizeDelta = new Vector2(600, 100);
            text.rectTransform.localScale = Vector3.one;
            text.fontSize = size; text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.gameObject.SetActive(false);
            return text;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            return rect;
        }

        Image Image(string name, Transform parent, Vector2 size, Color color, bool soft = false)
        {
            var image = Rect(name, parent, size).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            if (soft) image.sprite = feel.SoftSprite;
            return image;
        }

        Vector2 ScreenToOverlay(Vector2 screen)
        {
            var canvas = hud.GetComponentInParent<Canvas>();
            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, uiCamera, out Vector2 point);
            return point;
        }

        Vector2 OverlayPosition(RectTransform rect)
        {
            var canvas = hud.GetComponentInParent<Canvas>();
            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return ScreenToOverlay(RectTransformUtility.WorldToScreenPoint(uiCamera, rect.TransformPoint(rect.rect.center)));
        }

        public void OnPickup(ItemDefinition item, Vector3 position, bool accelerated, bool completed, int milestone)
        {
            var letter = floating[nextFloating++ % floating.Length];
            letter.worldPosition = position + new Vector3(0, .6f, -.9f);
            letter.anchored = false;
            letter.color = item.displayColor;
            letter.age = 0;
            letter.label.text = item.kind.ToString();
            FloatingPickupCount++;
            reserveFlash = 1;
            if (accelerated) speedPunch = 1;
            int index = (int)item.kind;
            if (index >= 0 && index < cardFlash.Length) cardFlash[index] = 1;
            if (completed)
            {
                gatherAge = 0;
                for (int i = 0; i < gathering.Length; i++)
                {
                    cardFlash[i] = 1;
                    gatherFrom[i] = i < hud.collectionCards.Length ? OverlayPosition(hud.collectionCards[i].rectTransform) : Vector2.zero;
                    gathering[i].text = ((ItemKind)i).ToString();
                    gathering[i].color = i < game.settings.items.Length ? game.settings.items[i].displayColor : feel.Settings.boostColor;
                }
            }
            if (milestone > 0)
            {
                milestoneAge = 0;
                milestoneLabel.text = $"{milestone} COMBO!";
            }
        }

        void LateUpdate()
        {
            if (game == null) return;
            bool playing = game.State == GameState.Playing;
            root.gameObject.SetActive(playing);
            if (!playing)
            {
                hud.speedLabel.rectTransform.localScale = speedScale;
                for (int i = 0; i < floating.Length; i++) floating[i].age = 10;
                gatherAge = milestoneAge = 10;
                speedPunch = reserveFlash = waterIntensity = 0;
                for (int i = 0; i < cardFlash.Length; i++)
                {
                    cardFlash[i] = 0;
                    if (i < hud.collectionCards.Length) hud.collectionCards[i].rectTransform.localScale = cardScale[i];
                }
                return;
            }
            float dt = Time.deltaTime;
            float speed = game.player.ActualVerticalSpeed;
            bool max = GameFeelMath.AtMaxSpeed(speed, game.settings.maxRiseSpeed);
            if (max) hud.speedLabel.text += "  ^";
            hud.speedLabel.rectTransform.localScale = speedScale * (1 + feel.Settings.speedPunchScale * speedPunch);
            speedPunch = Mathf.MoveTowards(speedPunch, 0, dt / Mathf.Max(.05f, feel.Settings.speedPunchSeconds));
            Vector2 speedCenter = OverlayPosition(hud.speedLabel.rectTransform);
            speedGlow.rectTransform.anchoredPosition = speedCenter;
            speedGlow.color = new Color(hud.speedMaxColor.r, hud.speedMaxColor.g, hud.speedMaxColor.b,
                max ? .22f + .08f * Mathf.Sin(Time.time * 9) : 0);
            reserveRoot.anchoredPosition = speedCenter + Vector2.down * (hud.speedLabel.rectTransform.rect.height * .5f + 12);
            ReserveFill.rectTransform.anchorMax = new Vector2(feel.Reserve, 1);
            ReserveFill.color = Color.Lerp(feel.Settings.boostColor, Color.white, reserveFlash);
            reserveTrack.color = Color.Lerp(new Color(.15f, .28f, .28f, .8f), feel.Settings.boostColor, reserveFlash * .55f);
            reserveRoot.localScale = Vector3.one * (1 + reserveFlash * .07f);
            reserveFlash = Mathf.MoveTowards(reserveFlash, 0, dt * 5);
            UpdateWater(dt, speed);
            UpdateFloating(dt);
            UpdateGathering(dt);
            UpdateMilestone(dt);
            for (int i = 0; i < cardFlash.Length && i < hud.collectionCards.Length; i++)
            {
                hud.collectionCards[i].color = Color.Lerp(hud.collectionCards[i].color, Color.white, cardFlash[i] * .75f);
                hud.collectionCards[i].rectTransform.localScale = cardScale[i] * (1 + .16f * cardFlash[i]);
                cardFlash[i] = Mathf.MoveTowards(cardFlash[i], 0, dt * 4);
            }
            if (game.Combo.Count >= 5)
                hud.comboLabel.color = Color.Lerp(hud.comboLabel.color, new Color(1, .35f, .15f),
                    Mathf.Clamp01(game.Combo.Count / 20f) * .6f);
            if (milestoneAge < .65f)
                hud.comboLabel.rectTransform.localScale *= 1 + .3f * (1 - milestoneAge / .65f);
        }

        void UpdateWater(float dt, float speed)
        {
            float target = GameFeelMath.WaterIntensity(game.settings.RiseSpeedFraction(speed));
            if (game.player.IsAkiInvulnerable) target = Mathf.Max(target, .85f);
            waterIntensity = Mathf.Lerp(waterIntensity, target, 1 - Mathf.Exp(-10 * dt));
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                float strength = waterIntensity * Mathf.Clamp01(waterIntensity * lines.Length - i + 1);
                line.y -= dt * line.speed * Mathf.Lerp(.3f, 2, waterIntensity);
                if (line.y < -.2f) line.y += 1.4f;
                line.image.rectTransform.anchoredPosition = new Vector2((line.x - .5f) * root.rect.width, (line.y - .5f) * root.rect.height);
                line.image.rectTransform.sizeDelta = new Vector2(3, line.length * Mathf.Lerp(.4f, 1.7f, waterIntensity));
                Color color = game.player.IsAkiInvulnerable ? feel.Settings.boostColor : new Color(.7f, .95f, 1);
                color.a = strength * feel.Settings.waterLineAlpha;
                line.image.color = color;
            }
        }

        void UpdateFloating(float dt)
        {
            if (viewCamera == null) viewCamera = Camera.main;
            foreach (var letter in floating)
            {
                letter.age += dt;
                bool visible = letter.age < .45f && viewCamera != null;
                letter.label.gameObject.SetActive(visible);
                if (!visible) continue;
                if (!letter.anchored)
                {
                    var screen = viewCamera.WorldToScreenPoint(letter.worldPosition);
                    if (screen.z <= 0) { letter.label.gameObject.SetActive(false); continue; }
                    letter.screenPosition = ScreenToOverlay(screen);
                    letter.anchored = true;
                }
                float t = letter.age / .45f;
                letter.label.rectTransform.anchoredPosition = letter.screenPosition + Vector2.up * (20 + 65 * t);
                letter.label.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, .9f, t);
                Color color = Color.Lerp(letter.color, Color.white, Mathf.Max(0, 1 - t * 3));
                color.a = 1 - t * t;
                letter.label.color = color;
            }
        }

        void UpdateGathering(float dt)
        {
            gatherAge += dt;
            var center = new Vector2(0, 170);
            for (int i = 0; i < gathering.Length; i++)
            {
                gathering[i].gameObject.SetActive(gatherAge < .5f);
                if (gatherAge >= .5f) continue;
                float join = Mathf.SmoothStep(0, 1, Mathf.Clamp01(gatherAge / .18f));
                float burst = Mathf.Clamp01((gatherAge - .18f) / .32f);
                Vector2 target = center + Vector2.right * ((i - 1) * 48);
                gathering[i].rectTransform.anchoredPosition = Vector2.Lerp(gatherFrom[i], target, join) +
                    new Vector2((i - 1) * 80, 45) * burst;
                gathering[i].rectTransform.localScale = Vector3.one * (1 + burst * .8f);
                Color color = Color.Lerp(game.settings.items[i].displayColor, Color.white, burst);
                color.a = 1 - burst;
                gathering[i].color = color;
            }
            float sparkProgress = Mathf.Clamp01((gatherAge - .18f) / .36f);
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i].gameObject.SetActive(gatherAge >= .18f && sparkProgress < 1);
                if (!sparks[i].gameObject.activeSelf) continue;
                float angle = i * Mathf.PI * 2 / sparks.Length;
                sparks[i].rectTransform.anchoredPosition = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Lerp(30, 170, sparkProgress);
                sparks[i].rectTransform.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg - 90);
                Color color = feel.Settings.boostColor; color.a = (1 - sparkProgress) * .8f;
                sparks[i].color = color;
            }
        }

        void UpdateMilestone(float dt)
        {
            milestoneAge += dt;
            milestoneLabel.gameObject.SetActive(milestoneAge < .8f);
            if (milestoneAge >= .8f) return;
            float t = milestoneAge / .8f;
            milestoneLabel.rectTransform.anchoredPosition = new Vector2(0, 270 + 40 * t);
            milestoneLabel.rectTransform.localScale = Vector3.one * (1 + .35f * Mathf.Exp(-t * 8));
            Color color = feel.Settings.boostColor; color.a = Mathf.Min(1, (1 - t) * 3);
            milestoneLabel.color = color;
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }

        void OnDisable()
        {
            if (root != null) root.gameObject.SetActive(false);
            if (hud == null) return;
            hud.speedLabel.rectTransform.localScale = speedScale;
            for (int i = 0; i < cardScale.Length && i < hud.collectionCards.Length; i++)
                hud.collectionCards[i].rectTransform.localScale = cardScale[i];
        }
    }
}
