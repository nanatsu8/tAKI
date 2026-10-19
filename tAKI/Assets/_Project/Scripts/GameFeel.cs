using UnityEngine;

namespace Taki
{
    [DefaultExecutionOrder(15)]
    public sealed class GameFeel : MonoBehaviour
    {
        public GameFeelSettings Settings { get; private set; }
        public GameHudEffects HudEffects { get; private set; }
        public float BoostPulse => Mathf.Clamp01(1 - (Time.time - boostAt) / Mathf.Max(.05f, Settings.boostPunchSeconds));
        public float Reserve => GameFeelMath.Reserve(game.player.InternalVerticalSpeed,
            game.settings.maxRiseSpeed, game.settings.maxStoredRiseSpeed);
        public Sprite SoftSprite { get; private set; }
        public TrailRenderer BoostTrail { get; private set; }
        public LineRenderer BoostAura { get; private set; }
        public int PickupFeedbackCount { get; private set; }
        public int LastMilestone { get; private set; }
        readonly System.Random random = new System.Random(1729);
        readonly Ring[] rings = new Ring[10];
        GameController game;
        ParticleSystem splash;
        Transform worldRoot;
        Material lineMaterial, particleMaterial;
        Texture2D softTexture;
        Renderer[] fishRenderers;
        Color[] fishColors;
        MaterialPropertyBlock[] originalBlocks;
        MaterialPropertyBlock tint;
        bool ownsSettings;
        float boostAt = float.NegativeInfinity;
        float pickupAt = float.NegativeInfinity;
        float jetRemainder;
        int nextRing;
        bool tintApplied;

        sealed class Ring
        {
            public LineRenderer line;
            public Vector3 position;
            public Color color;
            public float age, duration, radius;
        }

        public void Initialize(GameController owner, GameFeelSettings settings)
        {
            if (game != null) return;
            game = owner;
            Settings = settings;
            if (Settings == null)
            {
                Settings = ScriptableObject.CreateInstance<GameFeelSettings>();
                ownsSettings = true;
            }
            worldRoot = new GameObject("GameFeelWorld").transform;
            worldRoot.SetParent(transform, false);
            // The resource material keeps the shader referenced in Windows builds.
            var source = Resources.Load<Material>("FeedbackMaterial");
            lineMaterial = source != null ? new Material(source) : new Material(Shader.Find("tAKI/Feedback"));
            lineMaterial.name = "Feedback lines (runtime)";
            softTexture = CreateSoftTexture();
            SoftSprite = Sprite.Create(softTexture, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f));
            particleMaterial = new Material(lineMaterial) { name = "Feedback droplets (runtime)", mainTexture = softTexture };
            CreateParticles();
            for (int i = 0; i < rings.Length; i++)
                rings[i] = new Ring { line = CreateLine("PickupRing", worldRoot, 48, .065f) };
            BoostAura = CreateLine("AKI protection", worldRoot, 64, .09f);
            var trailObject = new GameObject("AKI wake");
            trailObject.transform.SetParent(worldRoot, false);
            BoostTrail = trailObject.AddComponent<TrailRenderer>();
            BoostTrail.sharedMaterial = lineMaterial;
            BoostTrail.minVertexDistance = .06f;
            BoostTrail.widthCurve = AnimationCurve.Linear(0, .65f, 1, 0);
            BoostTrail.colorGradient = FadeGradient(Settings.boostColor);
            BoostTrail.emitting = false;
            BoostTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            BoostTrail.receiveShadows = false;
            fishRenderers = game.player.visual.GetComponentsInChildren<Renderer>(true);
            fishColors = new Color[fishRenderers.Length];
            originalBlocks = new MaterialPropertyBlock[fishRenderers.Length];
            tint = new MaterialPropertyBlock();
            for (int i = 0; i < fishRenderers.Length; i++)
            {
                var material = fishRenderers[i].sharedMaterial;
                fishColors[i] = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
                originalBlocks[i] = new MaterialPropertyBlock();
                fishRenderers[i].GetPropertyBlock(originalBlocks[i]);
            }
            game.hud.EnsureFeedbackLabels();
            HudEffects = game.hud.GetComponent<GameHudEffects>();
            if (HudEffects == null) HudEffects = game.hud.gameObject.AddComponent<GameHudEffects>();
            HudEffects.Initialize(game, this);
        }

        public void OnPickup(ItemDefinition item, Vector3 position, float previousSpeed, int previousCombo, bool completed)
        {
            if (!isActiveAndEnabled || game.State != GameState.Playing) return;
            PickupFeedbackCount++;
            pickupAt = Time.time;
            Emit(position, item.displayColor, Mathf.Clamp(Settings.pickupParticles, 0, 64), false);
            Emit(game.player.transform.position, Color.white, 10, true);
            PlayRing(position, item.displayColor, Settings.pickupSeconds, Settings.pickupRingRadius);
            int milestone = GameFeelMath.CrossedMilestone(previousCombo, game.Combo.Count, Settings.comboMilestones);
            if (milestone > 0)
            {
                LastMilestone = milestone;
                Emit(game.player.transform.position, Settings.boostColor, 8 + Mathf.Clamp(milestone, 5, 24), false);
                PlayRing(game.player.transform.position, Settings.boostColor, .4f, 1.5f + Mathf.Clamp(milestone, 5, 24) * .04f);
            }
            if (completed)
            {
                boostAt = Time.time;
                BoostTrail.colorGradient = FadeGradient(Settings.boostColor);
                Emit(game.player.transform.position, Settings.boostColor, 40, true);
                PlayRing(game.player.transform.position, Settings.boostColor, .45f, 3);
            }
            HudEffects.OnPickup(item, position, game.player.ActualVerticalSpeed > previousSpeed + .001f, completed, milestone);
        }

        void CreateParticles()
        {
            splash = new GameObject("Colored droplets").AddComponent<ParticleSystem>();
            splash.transform.SetParent(worldRoot, false);
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = splash.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 640;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.18f, .38f);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(.07f, .19f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = splash.emission; emission.enabled = false;
            var shape = splash.shape; shape.enabled = false;
            var color = splash.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(Color.white);
            var size = splash.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var renderer = splash.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            splash.useAutoRandomSeed = false;
            splash.randomSeed = 1729;
            splash.Play();
        }

        static Gradient FadeGradient(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) },
                new[] { new GradientAlphaKey(.85f, 0), new GradientAlphaKey(0, 1) });
            return gradient;
        }

        void Emit(Vector3 position, Color color, int count, bool downward)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = Sample() * Mathf.PI * 2;
                float speed = Mathf.Lerp(2.5f, 7, Sample());
                var velocity = downward ? new Vector3((Sample() - .5f) * 4, -speed, 0) :
                    new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * speed;
                splash.Emit(new ParticleSystem.EmitParams { position = position + new Vector3(0, -.15f, -.8f),
                    velocity = velocity, startColor = Color.Lerp(color, Color.white, Sample() * .35f) }, 1);
            }
        }

        float Sample() => (float)random.NextDouble();

        LineRenderer CreateLine(string name, Transform parent, int points, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = lineMaterial;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        void PlayRing(Vector3 position, Color color, float duration, float radius)
        {
            var ring = rings[nextRing++ % rings.Length];
            ring.position = position;
            ring.color = color;
            ring.age = 0;
            ring.duration = Mathf.Max(.05f, duration);
            ring.radius = Mathf.Max(0, radius);
            ring.line.enabled = true;
        }

        static void DrawCircle(LineRenderer line, Vector3 position, float radius)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2 / line.positionCount;
                line.SetPosition(i, position + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -.85f));
            }
        }

        void LateUpdate()
        {
            if (game == null) return;
            bool playing = game.State == GameState.Playing;
            bool boost = playing && game.player.IsAkiInvulnerable;
            foreach (var ring in rings)
            {
                if (!ring.line.enabled) continue;
                ring.age += Time.deltaTime;
                float t = Mathf.Clamp01(ring.age / ring.duration);
                ring.line.enabled = playing && t < 1;
                if (!ring.line.enabled) continue;
                var color = ring.color; color.a = (1 - t) * .8f;
                ring.line.startColor = ring.line.endColor = color;
                DrawCircle(ring.line, ring.position, Mathf.Lerp(.15f, ring.radius, 1 - (1 - t) * (1 - t)));
            }
            BoostTrail.transform.position = game.player.transform.position + new Vector3(0, -.25f, -.7f);
            BoostTrail.time = Mathf.Max(0, Settings.boostTrailSeconds);
            BoostTrail.emitting = boost && Settings.boostTrailSeconds > 0;
            BoostAura.enabled = boost;
            if (boost)
            {
                var color = Settings.boostColor; color.a = .6f + .2f * Mathf.Sin(Time.time * 20);
                BoostAura.startColor = BoostAura.endColor = color;
                DrawCircle(BoostAura, game.player.transform.position, 1.05f + .06f * Mathf.Sin(Time.time * 15));
                jetRemainder += Time.deltaTime * 45;
                int count = Mathf.FloorToInt(jetRemainder);
                jetRemainder -= count;
                if (count > 0) Emit(game.player.transform.position, Settings.boostColor, Mathf.Min(count, 24), true);
            }
            else jetRemainder = 0;
            if (!playing)
            {
                BoostTrail.Clear();
                if (splash.particleCount > 0) splash.Clear();
            }
            float flash = playing ? Mathf.Clamp01(1 - (Time.time - pickupAt) / Mathf.Max(.05f, Settings.pickupSeconds)) : 0;
            if (boost || flash > 0)
            {
                for (int i = 0; i < fishRenderers.Length; i++)
                {
                    fishRenderers[i].GetPropertyBlock(tint);
                    tint.SetColor("_BaseColor", Color.Lerp(fishColors[i], Color.white, boost ? .65f : flash * .35f));
                    fishRenderers[i].SetPropertyBlock(tint);
                }
                tintApplied = true;
            }
            else RestoreFishColors();
        }

        void RestoreFishColors()
        {
            if (!tintApplied || fishRenderers == null) return;
            for (int i = 0; i < fishRenderers.Length; i++)
                if (fishRenderers[i] != null) fishRenderers[i].SetPropertyBlock(originalBlocks[i]);
            tintApplied = false;
        }

        static Texture2D CreateSoftTexture()
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Feedback soft dot", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                    pixels[y * 32 + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - radius), 1.5f));
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        void OnDisable()
        {
            RestoreFishColors();
            if (BoostTrail != null) { BoostTrail.emitting = false; BoostTrail.Clear(); }
            if (BoostAura != null) BoostAura.enabled = false;
            if (worldRoot != null) worldRoot.gameObject.SetActive(false);
            if (HudEffects != null) HudEffects.enabled = false;
        }

        void OnEnable()
        {
            if (worldRoot != null) worldRoot.gameObject.SetActive(true);
            if (HudEffects != null) HudEffects.enabled = true;
        }

        void OnDestroy()
        {
            if (HudEffects != null) Destroy(HudEffects);
            if (lineMaterial != null) Destroy(lineMaterial);
            if (particleMaterial != null) Destroy(particleMaterial);
            if (SoftSprite != null) Destroy(SoftSprite);
            if (softTexture != null) Destroy(softTexture);
            if (ownsSettings && Settings != null) Destroy(Settings);
        }
    }
}
