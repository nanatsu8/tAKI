using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Taki
{
    public sealed class VolumeDragControl : MonoBehaviour
    {
        public RectTransform Track { get; private set; }
        GameController game;
        RectTransform handle;
        Image fill;
        TMP_Text label;
        Vector2 pressedAt;
        bool dragging;
        bool hasDragged;

        public static VolumeDragControl Create(Transform parent, GameController game, TMP_FontAsset font)
        {
            var root = Rect("VolumeControl", parent, new Vector2(440, 100));
            root.anchorMin = root.anchorMax = new Vector2(.5f, 0);
            root.pivot = new Vector2(.5f, 0);
            root.anchoredPosition = new Vector2(0, 690);
            Graphic(root, new Color(.025f, .10f, .12f, .94f));
            var control = root.gameObject.AddComponent<VolumeDragControl>();
            control.game = game;
            var title = Rect("VolumeLabel", root, new Vector2(380, 32));
            title.anchoredPosition = new Vector2(0, 24);
            control.label = title.gameObject.AddComponent<TextMeshProUGUI>();
            control.label.font = font;
            control.label.fontSize = 23;
            control.label.alignment = TextAlignmentOptions.Center;
            control.label.color = new Color(1, .92f, .76f);
            control.label.raycastTarget = false;
            control.Track = Rect("DragTrack", root, new Vector2(360, 32));
            control.Track.anchoredPosition = new Vector2(0, -17);
            var background = Rect("TrackBackground", control.Track, new Vector2(360, 8));
            Graphic(background, new Color(.15f, .28f, .28f));
            var fillRect = Rect("Fill", background, Vector2.zero);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            control.fill = Graphic(fillRect, new Color(1, .73f, .28f));
            control.handle = Rect("Handle", control.Track, new Vector2(16, 28));
            Graphic(control.handle, new Color(1, .92f, .76f));
            control.Refresh();
            return control;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            return rect;
        }

        static Image Graphic(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        void Update()
        {
            if (game == null || game.State != GameState.Ready || game.Audio == null) { dragging = false; return; }
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 position = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
            {
                dragging = RectTransformUtility.RectangleContainsScreenPoint(Track, position, null);
                pressedAt = position;
                hasDragged = false;
            }
            // A click, the keyboard and the mouse wheel never change the volume.
            if (dragging && (position - pressedAt).sqrMagnitude >= 9) hasDragged = true;
            if (dragging && hasDragged && mouse.leftButton.isPressed &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(Track, position, null, out Vector2 local))
            {
                game.Audio.SetMasterVolume(Mathf.InverseLerp(Track.rect.xMin, Track.rect.xMax, local.x));
                Refresh();
            }
            if (!mouse.leftButton.isPressed) dragging = false;
        }

        void LateUpdate()
        {
            if (game != null && game.State != GameState.Ready) gameObject.SetActive(false);
        }

        void OnDisable() => dragging = false;

        public void Refresh()
        {
            float value = game.Audio.MasterVolume;
            fill.rectTransform.anchorMax = new Vector2(value, 1);
            handle.anchoredPosition = new Vector2(Mathf.Lerp(Track.rect.xMin, Track.rect.xMax, value), 0);
            label.text = $"VOLUME  {Mathf.RoundToInt(value * 100)}%";
        }
    }
}
