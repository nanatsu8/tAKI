using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Taki
{
    public enum GameState { Ready, Playing, Clear }

    [DefaultExecutionOrder(10)]
    public sealed class GameController : MonoBehaviour
    {
        public GameSettings settings;
        public PlayerController player;
        public GameHud hud;
        public Transform flowRoot;
        public ParticleSystem pickupParticles;
        public AudioSource audioSource;
        public AudioClip pickupSound;
        public AudioClip boostSound;
        public AudioClip hitSound;
        public AudioClip clearSound;
        public GameAudioSettings audioSettings;
        public GameFeelSettings feelSettings;
        public GameFeel Feedback { get; private set; }
        public GameAudioController Audio { get; private set; }
        public GameState State { get; private set; }
        public CollectionState Collection { get; } = new CollectionState();
        public ComboState Combo { get; } = new ComboState();
        public float Elapsed { get; private set; }
        public int PickupCount { get; private set; }
        public int HitCount { get; private set; }
        public float FlowDespawnHeight { get; private set; } = -19;
        public float Progress => Mathf.Clamp01(player.transform.position.y / settings.goalHeight);
        readonly ItemSpawnDistanceState itemSpawnDistance = new ItemSpawnDistanceState();
        readonly ItemSpawnTimerState itemSpawnTimer = new ItemSpawnTimerState();
        readonly ObstacleSpawnDistanceState obstacleSpawnDistance = new ObstacleSpawnDistanceState();
        readonly Ray[] viewportRays = new Ray[4];
        Camera spawnCamera;
        float visibleBottom;
        float visibleTop;
        bool initialItemsPending;
        float clearAt;
        bool releasedAfterClear;

        void Start()
        {
            State = GameState.Ready;
            Audio = GetComponent<GameAudioController>();
            if (Audio == null) Audio = gameObject.AddComponent<GameAudioController>();
            Audio.Initialize(audioSettings != null ? audioSettings : Resources.Load<GameAudioSettings>("GameAudio"));
            spawnCamera = Camera.main;
            Feedback = GetComponent<GameFeel>();
            if (Feedback == null) Feedback = gameObject.AddComponent<GameFeel>();
            Feedback.Initialize(this, feelSettings != null ? feelSettings : Resources.Load<GameFeelSettings>("GameFeel"));
            Application.targetFrameRate = 60;
            var args = System.Environment.GetCommandLineArgs();
            if (!RuntimeVerification.Started && (System.Array.IndexOf(args, "-takiSmoke") >= 0 ||
                System.Array.IndexOf(args, "-takiPlaythrough") >= 0))
                new GameObject("RuntimeVerification").AddComponent<RuntimeVerification>().game = this;
        }

        void Update()
        {
            if (State == GameState.Ready && player.AnyMovementPressed) Begin();
            else if (State == GameState.Playing)
            {
                Elapsed += Time.deltaTime;
                if (player.transform.position.y >= settings.goalHeight) { Complete(); return; }
            }
            else if (State == GameState.Clear)
            {
                if (!player.AnyMovementPressed) releasedAfterClear = true;
                if (releasedAfterClear && Time.time - clearAt > 1.2f && player.AnyMovementPressed)
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        // This runs after WaterfallView's camera follow, so spawning uses the view
        // that will actually be rendered, even during fast movement or a reversal.
        void LateUpdate()
        {
            if (State != GameState.Playing || !UpdateVisibleBounds()) return;
            if (initialItemsPending)
            {
                initialItemsPending = false;
                SpawnInitialItems();
            }
            else UpdateItemSpawns();
            UpdateTimedItemSpawns();
            UpdateObstacleSpawns();
        }

        bool UpdateVisibleBounds()
        {
            if (spawnCamera == null) spawnCamera = Camera.main;
            if (spawnCamera == null) return false;
            viewportRays[0] = spawnCamera.ViewportPointToRay(new Vector3(0, 0, 0));
            viewportRays[1] = spawnCamera.ViewportPointToRay(new Vector3(1, 0, 0));
            viewportRays[2] = spawnCamera.ViewportPointToRay(new Vector3(0, 1, 0));
            viewportRays[3] = spawnCamera.ViewportPointToRay(new Vector3(1, 1, 0));
            if (!ItemSpawnPlacement.TryGetHeightBounds(viewportRays, out visibleBottom, out visibleTop)) return false;
            FlowDespawnHeight = Mathf.Min(player.transform.position.y - 19,
                visibleBottom - Mathf.Max(2, settings.itemOffscreenMargin) - 3 * Mathf.Max(.5f, settings.itemSpawnDistance));
            return true;
        }

        public void Begin()
        {
            if (State != GameState.Ready) return;
            State = GameState.Playing;
            Combo.Reset();
            obstacleSpawnDistance.Reset(player.transform.position.y, settings);
            itemSpawnDistance.Reset(player.transform.position.y);
            itemSpawnTimer.Reset();
            initialItemsPending = true;
        }

        void SpawnInitialItems()
        {
            Vector3 position = player.transform.position;
            itemSpawnDistance.Reset(position.y);
            foreach (float height in ItemSpawnPlacement.InitialHeights(position.y, visibleBottom, visibleTop))
            {
                float x = Mathf.Clamp(position.x + Random.Range(-.8f, .8f),
                    -settings.waterfallWidth * .4f, settings.waterfallWidth * .4f);
                SpawnItem(height, x);
            }
        }

        void UpdateItemSpawns()
        {
            float height = player.transform.position.y;
            float spacing = Mathf.Max(.5f, settings.itemSpawnDistance);
            int count = itemSpawnDistance.Advance(height, spacing);
            for (int i = 0; i < count; i++)
            {
                float spawnHeight = ItemSpawnPlacement.GetHeight(visibleBottom, visibleTop,
                    settings.itemOffscreenMargin, itemSpawnDistance.Direction > 0, i * spacing);
                SpawnItem(spawnHeight,
                    Random.Range(-settings.waterfallWidth * .4f, settings.waterfallWidth * .4f));
            }

            // Recovery is independent of the distance and periodic spawn schedules.
            if (player.Body.velocity.y <= 0 && !HasUpcomingItem(height)) SeedItems();
        }

        void UpdateTimedItemSpawns()
        {
            int count = itemSpawnTimer.Advance(Time.deltaTime, settings.itemTimedSpawnInterval);
            float spacing = Mathf.Max(.5f, settings.itemSpawnDistance);
            for (int i = 0; i < count; i++)
            {
                float height = ItemSpawnPlacement.GetHeight(visibleBottom, visibleTop,
                    settings.itemOffscreenMargin, true, i * spacing);
                SpawnItem(height,
                    Random.Range(-settings.waterfallWidth * .4f, settings.waterfallWidth * .4f));
            }
        }

        void UpdateObstacleSpawns()
        {
            float height = player.transform.position.y;
            float firstThreshold = obstacleSpawnDistance.NextHeight;
            float firstSpawnHeight = Mathf.Max(visibleTop + 2, height + settings.spawnAhead + 1);
            while (obstacleSpawnDistance.TryAdvance(height, settings, out float threshold))
            {
                // Keep every skipped row above the view, separated by its world-distance interval.
                SpawnObstacle(firstSpawnHeight + threshold - firstThreshold,
                    Random.Range(-settings.waterfallWidth * .37f, settings.waterfallWidth * .37f));
            }
        }

        bool HasUpcomingItem(float height)
        {
            foreach (Transform child in flowRoot)
            {
                if (!child.gameObject.activeInHierarchy || !child.TryGetComponent<FlowObject>(out var flow) ||
                    flow.obstacle || flow.Used || flow.definition == null) continue;
                if (child.position.y >= height - .75f) return true;
                // Below-screen items are useful only when the player can catch them
                // before reaching the floor. Otherwise supply a new batch above.
                float closingSpeed = -player.Body.velocity.y - flow.fallSpeed;
                if (closingSpeed > 0 && child.position.y >= 0 &&
                    (height - child.position.y) / closingSpeed < child.position.y / Mathf.Max(.01f, flow.fallSpeed)) return true;
            }
            return false;
        }

        void SeedItems()
        {
            Vector3 position = player.transform.position;
            itemSpawnDistance.Reset(position.y);
            for (int i = 0; i < 3; i++)
            {
                float x = Mathf.Clamp(position.x + Random.Range(-.8f, .8f),
                    -settings.waterfallWidth * .4f, settings.waterfallWidth * .4f);
                float y = ItemSpawnPlacement.GetHeight(visibleBottom, visibleTop,
                    settings.itemOffscreenMargin, true, i * Mathf.Max(.5f, settings.itemSpawnDistance));
                SpawnItem(y, x);
            }
        }

        public FlowObject SpawnItem(float y, float x, ItemDefinition forcedDefinition = null)
        {
            var item = forcedDefinition != null ? forcedDefinition : ItemDefinition.Choose(settings.items, Random.value);
            if (item == null || item.prefab == null) return null;
            var flow = Instantiate(item.prefab, new Vector3(x, y, 0), Quaternion.identity, flowRoot);
            flow.Initialize(this, item);
            return flow;
        }

        public FlowObject SpawnObstacle(float y, float x)
        {
            if (settings.obstaclePrefab == null) return null;
            var flow = Instantiate(settings.obstaclePrefab, new Vector3(x, y, 0), Quaternion.identity, flowRoot);
            flow.Initialize(this, null);
            return flow;
        }

        public void Collect(ItemDefinition item, Vector3 position)
        {
            if (State != GameState.Playing) return;
            PickupCount++;
            float previousSpeed = player.ActualVerticalSpeed;
            int previousCombo = Combo.Count;
            // Judge the approach, before this item's impulse or the AKI bonus changes speed.
            Combo.RecordPickup(player.Body.velocity.y);
            player.ApplyPickup(item.upwardImpulse);
            bool completed = Collection.Add(item.kind);
            if (completed) player.ActivateAkiBoost();
            if (completed) hud.Notify("AKI BOOST!", Feedback.Settings.boostColor);
            Feedback.OnPickup(item, position, previousSpeed, previousCombo, completed);
            if (Audio.IsConfigured) Audio.PlayPickup(completed);
            else Play(completed ? boostSound : pickupSound, completed ? 1 : 1 + (int)item.kind * .12f);
        }

        public bool TryHit()
        {
            if (!player.Hit()) return false;
            HitCount++;
            hud.Notify("OUCH!", new Color(1, .5f, .4f));
            if (Audio.IsConfigured) Audio.PlayHit();
            else Play(hitSound);
            return true;
        }

        public void Complete()
        {
            if (State != GameState.Playing) return;
            State = GameState.Clear;
            clearAt = Time.time;
            player.StopMotion();
            if (Audio.IsConfigured) Audio.PlayClear();
            else Play(clearSound);
        }

        void Play(AudioClip clip, float pitch = 1)
        {
            if (audioSource == null || clip == null) return;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, .45f);
        }
    }

    public static class ItemSpawnPlacement
    {
        public static IEnumerable<float> InitialHeights(float playerHeight, float bottom, float top)
        {
            // Only the initial batch fills the visible area, at the requested fixed spacing.
            float firstHeight = Mathf.Max(playerHeight + 2, bottom + 1);
            for (float height = firstHeight; height <= top - 1; height += 2)
                yield return height;
        }

        public static bool TryGetHeightBounds(Ray[] rays, out float bottom, out float top)
        {
            bottom = float.PositiveInfinity;
            top = float.NegativeInfinity;
            if (rays == null || rays.Length == 0) return false;
            var plane = new Plane(Vector3.forward, 0);
            foreach (var ray in rays)
            {
                if (!plane.Raycast(ray, out float distance)) return false;
                float y = ray.GetPoint(distance).y;
                bottom = Mathf.Min(bottom, y);
                top = Mathf.Max(top, y);
            }
            return true;
        }

        public static float GetHeight(float bottom, float top, float margin, bool above, float offset = 0)
        {
            float clearance = Mathf.Max(2, margin) + Mathf.Max(0, offset);
            float below = bottom - clearance;
            // The player cannot descend below zero, so never put a new item there.
            return above || below < .75f ? top + clearance : below;
        }
    }
}
