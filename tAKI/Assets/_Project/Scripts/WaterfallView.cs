using UnityEngine;

namespace Taki
{
    public sealed class WaterfallView : MonoBehaviour
    {
        public GameController game;
        public Camera followCamera;
        public Transform[] waterStreaks;
        public Transform goalMarker;
        public Vector3 cameraOffset = new Vector3(0, 10, -26);
        public float viewAhead = 5.5f;
        [Tooltip("Summit height used when placing the scene's rocks and trees (world units).")]
        [Min(1)] public float decorationHeight = 180;
        [Tooltip("Waterfall width used when placing the scene's water streaks (world units).")]
        [Min(2)] public float decorationWidth = 12;
        float baseOrthographicSize, baseFieldOfView;
        bool cameraInitialized;

        void Start()
        {
            baseOrthographicSize = followCamera.orthographicSize;
            baseFieldOfView = followCamera.fieldOfView;
            cameraInitialized = true;
            followCamera.transform.position = game.player.transform.position + cameraOffset;
            followCamera.transform.LookAt(game.player.transform.position + Vector3.up * viewAhead);
            goalMarker.position = new Vector3(0, game.settings.goalHeight, 0);
            float halfWidth = game.settings.waterfallWidth * .5f;
            foreach (Transform part in transform)
            {
                var scale = part.localScale;
                var position = part.position;
                if (part.name == "Water" || part.name == "Riverbank" || part.name == "EdgeFoam")
                {
                    scale.y = game.settings.goalHeight + (part.name == "Water" ? 50 : part.name == "Riverbank" ? 35 : 25);
                    position.y = game.settings.goalHeight * .5f;
                    if (part.name == "Water") scale.x = game.settings.waterfallWidth;
                    if (part.name == "Riverbank") position.x = Mathf.Sign(position.x) * (halfWidth + 2.5f);
                    if (part.name == "EdgeFoam") position.x = Mathf.Sign(position.x) * (halfWidth - .1f);
                    part.localScale = scale; part.position = position;
                }
                if (!part.name.StartsWith("rock_") && !part.name.StartsWith("tree_")) continue;
                position.y *= game.settings.goalHeight / Mathf.Max(1, decorationHeight);
                part.position = position;
                var renderers = part.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                // Keep decorative geometry out of the playable water, including after width changes.
                if (position.x > 0 && bounds.min.x < halfWidth + .15f)
                    part.position += Vector3.right * (halfWidth + .15f - bounds.min.x);
                if (position.x < 0 && bounds.max.x > -halfWidth - .15f)
                    part.position += Vector3.left * (bounds.max.x + halfWidth + .15f);
            }
            var finish = goalMarker.Find("Finish");
            if (finish != null) finish.localScale = new Vector3(game.settings.waterfallWidth, .25f, .5f);
            foreach (var streak in waterStreaks)
            {
                var position = streak.position;
                position.x *= game.settings.waterfallWidth / Mathf.Max(2, decorationWidth);
                streak.position = position;
            }
        }

        void Update()
        {
            if (game.State != GameState.Playing) return;
            float y = game.player.transform.position.y;
            for (int i = 0; i < waterStreaks.Length; i++)
            {
                var streak = waterStreaks[i];
                var pos = streak.position;
                pos.y -= (4 + i % 4) * Time.deltaTime;
                if (pos.y < y - 14) pos.y = y + 23 + i % 3;
                streak.position = pos;
            }
        }

        void LateUpdate()
        {
            Vector3 target = new Vector3(0, Mathf.Max(0, game.player.transform.position.y), 0);
            followCamera.transform.position = Vector3.Lerp(followCamera.transform.position, target + cameraOffset,
                1 - Mathf.Exp(-9 * Time.deltaTime));
            followCamera.transform.rotation = Quaternion.LookRotation(new Vector3(0, viewAhead - cameraOffset.y, -cameraOffset.z));
            float zoom = game.State == GameState.Playing && game.Feedback != null ?
                game.Feedback.BoostPulse * game.Feedback.Settings.boostZoomOut : 0;
            float smoothing = 1 - Mathf.Exp(-12 * Time.deltaTime);
            followCamera.orthographicSize = Mathf.Lerp(followCamera.orthographicSize, baseOrthographicSize * (1 + zoom), smoothing);
            followCamera.fieldOfView = Mathf.Lerp(followCamera.fieldOfView, baseFieldOfView * (1 + zoom), smoothing);
        }

        void OnDisable()
        {
            if (!cameraInitialized || followCamera == null) return;
            followCamera.orthographicSize = baseOrthographicSize;
            followCamera.fieldOfView = baseFieldOfView;
        }
    }
}
