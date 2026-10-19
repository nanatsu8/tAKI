using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Taki.Editor
{
    public static class TakiBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/Main.unity";
        static TMP_FontAsset font;
        static Material fish, cream, dark, fin, water, foam, bark, rock, leaf, leafGold;

        [MenuItem("tAKI/Prepare game assets")]
        public static void Prepare()
        {
            foreach (string folder in new[] { "Scenes", "Scripts", "Editor", "Prefabs", "Materials", "Settings", "Fonts", "Models", "Audio" })
                Directory.CreateDirectory(Root + "/" + folder);
            if (!Directory.Exists("Assets/TextMesh Pro/Resources"))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.textmeshpro");
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
            }
            ImportSources();
            AssetDatabase.Refresh();
            font = CreateFont();
            fish = Mat("Salmon", new Color(.96f, .46f, .38f));
            cream = Mat("Cream", new Color(1, .92f, .75f));
            dark = Mat("Ink", new Color(.025f, .08f, .1f));
            fin = Mat("Fins", new Color(.65f, .24f, .25f));
            water = Mat("Water", new Color(.055f, .39f, .44f));
            foam = Mat("Foam", new Color(.41f, .77f, .75f), true);
            bark = Mat("Wood", new Color(.33f, .18f, .12f));
            rock = Mat("Rock", new Color(.22f, .29f, .27f));
            leaf = Mat("Autumn", new Color(.85f, .29f, .10f));
            leafGold = Mat("Gold", new Color(.93f, .60f, .13f));

            var settings = AssetDatabase.LoadAssetAtPath<GameSettings>(Root + "/Settings/GameSettings.asset");
            bool newSettings = settings == null;
            if (newSettings)
            {
                settings = ScriptableObject.CreateInstance<GameSettings>();
                AssetDatabase.CreateAsset(settings, Root + "/Settings/GameSettings.asset");
            }
            if (settings.items == null || settings.items.Length != 3)
                settings.items = new ItemDefinition[3];
            string[] names = { "アケビ", "カキ", "イチジク" };
            Color[] colors = { new Color(.68f, .47f, .95f), new Color(1, .61f, .16f), new Color(1, .42f, .65f) };
            for (int i = 0; i < 3; i++)
            {
                string path = Root + "/Settings/Item" + (ItemKind)i + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null)
                {
                    item = ScriptableObject.CreateInstance<ItemDefinition>();
                    item.kind = (ItemKind)i;
                    item.displayName = names[i];
                    item.displayColor = colors[i];
                    AssetDatabase.CreateAsset(item, path);
                }
                settings.items[i] = item;
                if (item.prefab == null) item.prefab = CreateItem(item);
                EditorUtility.SetDirty(item);
            }
            if (settings.obstaclePrefab == null) settings.obstaclePrefab = CreateLog();
            EditorUtility.SetDirty(settings);
            if (!File.Exists(ScenePath)) CreateScene(settings);
            ConfigurePlayer();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("TAKI_PREPARED: " + ScenePath);
        }

        [MenuItem("tAKI/Build Windows")]
        public static void BuildWindows()
        {
            Prepare();
            VerifyLogic();
            string output = Path.GetFullPath("Builds/Windows/tAKI.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Windows build failed: " + report.summary.result);
            string notices = Path.Combine(Path.GetDirectoryName(output), "ThirdPartyNotices");
            Directory.CreateDirectory(notices);
            File.Copy(Root + "/Models/Kenney-License.txt", Path.Combine(notices, "Kenney-License.txt"), true);
            File.Copy(Root + "/Fonts/readme.txt", Path.Combine(notices, "Bansyu-retoromin-readme.txt"), true);
            Debug.Log("TAKI_BUILD_SUCCEEDED: " + output + " / " + report.summary.totalSize + " bytes");
        }

        [MenuItem("tAKI/Restore audio references")]
        public static void RestoreAudioReferences()
        {
            AssetDatabase.Refresh();
            string[] names = {
                "flow-基本BGM.mp3", "決定ボタンを押す33-アイテム獲得時.mp3",
                "決定ボタンを押す25-AKIブースト.mp3", "キャンセル4-障害物接触時.mp3",
                "歓声と拍手2-クリア時.mp3"
            };
            var clips = names.Select(name => AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/Provided/" + name)).ToArray();
            string[] missing = names.Where((name, index) => clips[index] == null).ToArray();
            if (missing.Length > 0)
                throw new FileNotFoundException("Place the missing audio in Assets/_Project/Audio/Provided/ (see AUDIO_SETUP.md):\n" +
                    string.Join("\n", missing));
            var audio = AssetDatabase.LoadAssetAtPath<GameAudioSettings>(Root + "/Resources/GameAudio.asset");
            if (audio == null) throw new FileNotFoundException("Restore Assets/_Project/Resources/GameAudio.asset from Git.");
            audio.backgroundMusic = clips[0];
            audio.pickup = clips[1];
            audio.akiBoost = clips[2];
            audio.hit = clips[3];
            audio.clear = clips[4];
            EditorUtility.SetDirty(audio);
            AssetDatabase.SaveAssets();
            Debug.Log("TAKI_AUDIO_RESTORED: BGM and all four effects are connected.");
        }

        [MenuItem("tAKI/Verify gameplay logic")]
        public static void VerifyLogic()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GameSettings>(Root + "/Settings/GameSettings.asset");
            Assert(settings != null, "Game settings exist");
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>(Root + "/Resources/GameFeel.asset");
            var feedbackMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Resources/FeedbackMaterial.mat");
            Assert(feel != null && feedbackMaterial != null && feedbackMaterial.shader != null &&
                feedbackMaterial.shader.name == "tAKI/Feedback" && !ShaderUtil.ShaderHasError(feedbackMaterial.shader),
                "Feedback settings and shader import correctly and have build references");
            Assert(GameFeelMath.Reserve(20, 20, 30) == 0 && GameFeelMath.Reserve(25, 20, 30) == .5f &&
                GameFeelMath.Reserve(30, 20, 30) == 1 && GameFeelMath.Reserve(100, 20, 30) == 1,
                "Momentum reserve displays only stored speed beyond the motion cap");
            Assert(GameFeelMath.Reserve(-10, 20, 30) == 0 && GameFeelMath.Reserve(20, 20, 20) == 0 &&
                GameFeelMath.Reserve(0, 0, 30) == 0,
                "Reserve handles descent and absent speed ranges");
            Assert(GameFeelMath.Reserve(22.5f, 20, 25) == .5f && GameFeelMath.Reserve(25, 20, 25) == 1,
                "Reserve follows a tuned stored cap instead of assuming the default of 30");
            Assert(!GameFeelMath.AtMaxSpeed(0, 0) && !GameFeelMath.AtMaxSpeed(-10, 20) &&
                GameFeelMath.AtMaxSpeed(20, 20), "Maximum-speed effects require actual upward motion");
            Assert(GameFeelMath.WaterIntensity(0) == 0 && GameFeelMath.WaterIntensity(1) == 1,
                "Water lines range from still to full speed");
            Assert(GameFeelMath.CrossedMilestone(4, 5, new[] { 5, 10, 20 }) == 5 &&
                GameFeelMath.CrossedMilestone(5, 5, new[] { 5, 10, 20 }) == 0 &&
                GameFeelMath.CrossedMilestone(9, 20, new[] { 20, 5, 10 }) == 20 &&
                GameFeelMath.CrossedMilestone(0, 5, null) == 0,
                "Combo milestones trigger on crossing, including skipped and unordered thresholds");
            var a = ScriptableObject.CreateInstance<ItemDefinition>();
            var k = ScriptableObject.CreateInstance<ItemDefinition>();
            var i = ScriptableObject.CreateInstance<ItemDefinition>();
            a.spawnWeight = 1; k.spawnWeight = 1; i.spawnWeight = 2;
            var items = new[] { a, k, i };
            Assert(ItemDefinition.Choose(items, .249f) == a, "Weighted A interval");
            Assert(ItemDefinition.Choose(items, .25f) == k, "Weighted K interval boundary");
            Assert(ItemDefinition.Choose(items, .5f) == i && ItemDefinition.Choose(items, 1) == i, "Weighted I interval and endpoint");
            a.spawnWeight = 0;
            Assert(ItemDefinition.Choose(items, 0) == k, "Disabled kind excluded");
            k.spawnWeight = i.spawnWeight = 0;
            Assert(ItemDefinition.Choose(items, .5f) == null, "All zero weights stop generation");
            Assert(ItemDefinition.Choose(null, .5f) == null, "Absent item list is safe");
            var collection = new CollectionState();
            Assert(!collection.Add(ItemKind.A) && !collection.Add(ItemKind.A) && collection.Mask == 1,
                "Duplicates preserve the set state");
            Assert(!collection.Add(ItemKind.I) && collection.Add(ItemKind.K) && collection.Mask == 0 && collection.CompletedSets == 1,
                "Unordered set completes once and resets");
            var protection = new InvulnerabilityState();
            var combo = new ComboState();
            combo.RecordPickup(-1);
            combo.RecordPickup(0);
            Assert(combo.Count == 0, "Falling and stationary pickups do not start a combo");
            combo.RecordPickup(.001f);
            combo.RecordPickup(20);
            Assert(combo.Count == 2, "Every rising pickup counts, including a tiny positive speed");
            combo.UpdateSpeed(.001f);
            Assert(combo.Count == 2, "Rising without a pickup preserves the combo");
            combo.UpdateSpeed(0);
            Assert(combo.Count == 0, "Stopping resets the combo before the next pickup");
            combo.RecordPickup(1);
            combo.UpdateSpeed(-.001f);
            Assert(combo.Count == 0, "Descent resets the combo without waiting for another pickup");
            combo.RecordPickup(1);
            combo.Reset();
            Assert(combo.Count == 0, "Restart clears the combo");
            protection.GrantBoost(10, .8f);
            Assert(protection.IsActive(10.79f) && protection.IsBoostActive(10.79f) && !protection.IsActive(10.8f),
                "AKI protection lasts 0.8 seconds and expires at the boundary");
            protection.GrantBoost(10.5f, .8f);
            Assert(protection.IsBoostActive(11.29f) && !protection.IsBoostActive(11.3f),
                "Another AKI refreshes the boost protection");
            protection.Reset();
            protection.GrantHit(20, 2);
            protection.GrantBoost(20.5f, .8f);
            Assert(protection.IsActive(21.5f) && !protection.IsBoostActive(21.5f),
                "AKI does not shorten existing hit protection or extend obstacle destruction past its boost window");
            protection.Reset();
            Assert(!protection.IsActive(0) && !protection.IsBoostActive(0), "Restart clears all protection");
            Assert(settings.ClampVelocity(100) == settings.maxRiseSpeed && settings.ClampVelocity(-100) == -settings.maxFallSpeed,
                "Independent rise and fall limits");
            Assert(settings.PickupVelocity(-10, 5) == settings.ClampStoredVelocity(5) &&
                settings.PickupVelocity(0, 5) == settings.ClampStoredVelocity(5),
                "Item pickup replaces downward or zero speed with its upward speed");
            Assert(settings.PickupVelocity(3, 5) == settings.ClampStoredVelocity(5) &&
                settings.PickupVelocity(10, 5) == settings.ClampStoredVelocity(10),
                "Item pickup takes the greater of current speed and item speed, without adding them");
            Assert(settings.PickupVelocity(0, float.MaxValue) == Mathf.Max(settings.maxStoredRiseSpeed, settings.maxRiseSpeed),
                "Item pickup obeys the stored speed cap");
            var velocitySettings = ScriptableObject.CreateInstance<GameSettings>();
            velocitySettings.maxRiseSpeed = 20;
            velocitySettings.maxStoredRiseSpeed = 30;
            velocitySettings.maxFallSpeed = 10;
            velocitySettings.gravity = 4;
            velocitySettings.verticalAssist = 3;
            Assert(velocitySettings.ClampStoredVelocity(100) == 30 && velocitySettings.ClampVelocity(30) == 20,
                "Stored upward speed can exceed the actual movement speed cap");
            float decayedSpeed = velocitySettings.StepStoredVelocity(30, 0, .5f, false);
            Assert(decayedSpeed == 28 && velocitySettings.ClampVelocity(decayedSpeed) == 20,
                "Gravity consumes stored speed while actual motion stays capped");
            Assert(velocitySettings.StepStoredVelocity(30, 1, 1, false) == 29 &&
                velocitySettings.StepStoredVelocity(30, -1, 1, false) == 23,
                "W and S affect the decay of stored speed");
            Assert(velocitySettings.PickupVelocity(25, 5) == 25 &&
                velocitySettings.ClampStoredVelocity(25 + 100) == 30,
                "Ordinary pickups preserve stored surplus and AKI adds up to the stored cap");
            Assert(velocitySettings.StepStoredVelocity(-10, 0, 1, false) == -10 &&
                velocitySettings.StepStoredVelocity(-10, 0, 1, true) == 0,
                "Stored velocity respects the fall cap and floor");
            Assert(velocitySettings.RiseSpeedFraction(0) == 0 && velocitySettings.RiseSpeedFraction(10) == .5f &&
                velocitySettings.RiseSpeedFraction(20) == 1,
                "HUD speed color ranges from zero to the actual rise cap");
            Object.DestroyImmediate(velocitySettings);
            var displaySettings = ScriptableObject.CreateInstance<GameSettings>();
            displaySettings.goalHeight = 500;
            displaySettings.displayedGoalMeters = 1000;
            Assert(displaySettings.DisplayedVerticalSpeed(20) == 40 && displaySettings.DisplayedVerticalSpeed(-10) == -20 &&
                displaySettings.DisplayedVerticalSpeed(0) == 0,
                "HUD velocity converts signed world speed to displayed meters per second");
            Assert(Mathf.Approximately(displaySettings.DisplayedVerticalSpeed(-10),
                displaySettings.DisplayedHeight(240) - displaySettings.DisplayedHeight(250)),
                "Displayed speed matches the change in displayed altitude per second");
            Object.DestroyImmediate(displaySettings);
            var speedSettings = ScriptableObject.CreateInstance<GameSettings>();
            speedSettings.itemFallSpeedMultiplierMin = 1;
            speedSettings.itemFallSpeedMultiplierMax = 3;
            Assert(Mathf.Approximately(speedSettings.ScaleItemFallSpeed(2.6f, 0), 2.6f) &&
                Mathf.Approximately(speedSettings.ScaleItemFallSpeed(2.6f, 1), 7.8f) &&
                Mathf.Approximately(speedSettings.ScaleItemFallSpeed(2.6f, .5f), 5.2f),
                "Item fall speed scales from x1 to x3 using the spawn sample");
            speedSettings.itemFallSpeedMultiplierMin = 3;
            speedSettings.itemFallSpeedMultiplierMax = 1;
            Assert(speedSettings.ScaleItemFallSpeed(4, 0) == 4 && speedSettings.ScaleItemFallSpeed(4, 1) == 12,
                "Reversed multiplier bounds are safely normalized");
            Assert(speedSettings.ScaleItemFallSpeed(0, .5f) == 0, "A zero base fall speed stays zero");
            Object.DestroyImmediate(speedSettings);
            Assert(settings.ObstacleDistance(0) > settings.ObstacleDistance(settings.goalHeight), "Obstacles become denser with height");
            var obstacleSettings = ScriptableObject.CreateInstance<GameSettings>();
            obstacleSettings.goalHeight = 100;
            obstacleSettings.earlyObstacleDistance = 10;
            obstacleSettings.middleObstacleDistance = 6;
            obstacleSettings.finalObstacleDistance = 3;
            var obstacleDistance = new ObstacleSpawnDistanceState();
            obstacleDistance.Reset(0, obstacleSettings);
            Assert(!obstacleDistance.TryAdvance(9.99f, obstacleSettings, out _) &&
                obstacleDistance.TryAdvance(10, obstacleSettings, out float firstObstacle) && firstObstacle == 10,
                "Obstacles spawn only when a new distance threshold is crossed");
            Assert(!obstacleDistance.TryAdvance(0, obstacleSettings, out _) &&
                !obstacleDistance.TryAdvance(10, obstacleSettings, out _),
                "Falling and returning to an already processed height never duplicate an obstacle");
            Assert(obstacleDistance.TryAdvance(30, obstacleSettings, out float secondObstacle) && secondObstacle == 20 &&
                obstacleDistance.TryAdvance(30, obstacleSettings, out float thirdObstacle) && thirdObstacle == 30 &&
                obstacleDistance.NextHeight == 36 && !obstacleDistance.TryAdvance(30, obstacleSettings, out _),
                "Fast ascent processes each height once and switches spacing at the middle section");
            Assert(obstacleSettings.ObstacleDistance(70) == 3, "Final-section obstacle distance is used at its boundary");
            obstacleDistance.Reset(0, obstacleSettings);
            Assert(obstacleDistance.NextHeight == 10, "Restart resets the obstacle height frontier");
            Object.DestroyImmediate(obstacleSettings);
            var distance = new ItemSpawnDistanceState();
            distance.Reset(0);
            Assert(distance.Advance(0, 6) == 0 && distance.Advance(5.99f, 6) == 0,
                "Waiting and movement below the distance threshold do not spawn items");
            Assert(distance.Advance(6, 6) == 1 && distance.Advance(6, 6) == 0,
                "Crossing the distance threshold spawns exactly once");
            Assert(distance.Advance(25, 6) == 3 && distance.Direction == 1,
                "Fast ascent consumes every crossed distance interval");
            Assert(distance.Advance(5, 6) == 3 && distance.Direction == -1 && distance.Advance(5, 6) == 0,
                "Descent spawns below while waiting does not add distance");
            Assert(distance.Advance(25, 6) == 3 && distance.Direction == 1,
                "Changing direction spawns on the new leading side");
            distance.Reset(10);
            Assert(distance.Advance(15.99f, 6) == 0 && distance.Advance(16, 6) == 1,
                "Recovery restarts distance tracking from the recovery height");
            distance.Reset(0);
            Assert(distance.Advance(1, 0) == 2, "Invalid distance spacing is safely clamped");
            var itemTimer = new ItemSpawnTimerState();
            Assert(itemTimer.Advance(.4f, 1) == 0 && itemTimer.Advance(.6f, 1) == 1,
                "Periodic item spawning triggers after its time interval without requiring movement");
            Assert(itemTimer.Advance(2.5f, 1) == 2 && itemTimer.Advance(.5f, 1) == 1,
                "Periodic items preserve partial time and process skipped intervals");
            itemTimer.Reset();
            Assert(itemTimer.Advance(0, 1) == 0 && itemTimer.Advance(.99f, 1) == 0,
                "Restart resets the periodic item timer");
            Assert(itemTimer.Advance(10, 0) == 0 && itemTimer.Advance(.1f, 1) == 0,
                "Zero interval disables periodic items without a re-enable burst");
            var viewportRays = new[]
            {
                new Ray(new Vector3(-10, 30, -20), Vector3.forward),
                new Ray(new Vector3(10, 30, -20), Vector3.forward),
                new Ray(new Vector3(-10, 55, -20), Vector3.forward),
                new Ray(new Vector3(10, 55, -20), Vector3.forward)
            };
            Assert(ItemSpawnPlacement.TryGetHeightBounds(viewportRays, out float bottom, out float top) && bottom == 30 && top == 55,
                "Viewport rays intersect the player's movement plane");
            Assert(ItemSpawnPlacement.GetHeight(bottom, top, 2, true) > top &&
                ItemSpawnPlacement.GetHeight(bottom, top, 2, false) < bottom,
                "Items spawn beyond the top or bottom edge according to travel direction");
            Assert(ItemSpawnPlacement.GetHeight(-8, 18, 2, false) > 18,
                "Descending near the floor supplies reachable items above the view");
            Assert(ItemSpawnPlacement.InitialHeights(0, -8, 18).SequenceEqual(new float[] { 2, 4, 6, 8, 10, 12, 14, 16 }),
                "Initial items fill the visible area at exactly two-unit intervals");
            Assert(!ItemSpawnPlacement.InitialHeights(0, -8, 2).Any(),
                "Initial placement does not spill outside a small visible area");
            Assert(settings.DisplayedHeight(0) == 0 && settings.DisplayedHeight(-1) == 0,
                "Displayed distance starts at zero");
            Assert(Mathf.Approximately(settings.DisplayedHeight(settings.goalHeight * .5f), settings.displayedGoalMeters * .5f),
                "Displayed distance scales independently of world units");
            Assert(settings.DisplayedHeight(settings.goalHeight) == settings.displayedGoalMeters &&
                settings.DisplayedHeight(settings.goalHeight + 10) == settings.displayedGoalMeters,
                "Displayed distance stops at the summit distance");
            Assert(settings.items.All(item => item != null && item.prefab != null), "All item prefabs connected");
            var audio = Resources.Load<GameAudioSettings>("GameAudio");
            Assert(audio != null && audio.backgroundMusic != null && audio.pickup != null && audio.akiBoost != null &&
                audio.hit != null && audio.clear != null, "BGM and all supplied sound effects are connected");
            Assert(font == null || font.HasCharacters("KurIAクリア！"), "Required clear glyphs available");
            Object.DestroyImmediate(a); Object.DestroyImmediate(k); Object.DestroyImmediate(i);
            Debug.Log("TAKI_LOGIC_CHECKS_PASSED");
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("TAKI CHECK FAILED: " + message);
            Debug.Log("PASS: " + message);
        }

        static void ImportSources()
        {
            string[] models = { "log", "rock_largeA", "rock_largeB", "tree_default_fall", "tree_oak_fall", "cliff_half_rock" };
            foreach (var model in models)
                CopyIfMissing("ArtSource/kenney_nature-kit/Models/FBX format/" + model + ".fbx", Root + "/Models/" + model + ".fbx");
            CopyIfMissing("ArtSource/kenney_nature-kit/License.txt", Root + "/Models/Kenney-License.txt");
            // A public checkout already contains the imported font and its license.
            // Original download folders are optional and intentionally not in Git.
            if (File.Exists(Root + "/Fonts/Bansyu-retoromin.otf") && File.Exists(Root + "/Fonts/readme.txt")) return;
            string fontSource = Directory.Exists("Fonts") ?
                Directory.GetFiles("Fonts", "*.otf", SearchOption.AllDirectories).FirstOrDefault() : null;
            if (fontSource == null)
                throw new FileNotFoundException("Restore Assets/_Project/Fonts from Git, or place the original font in Fonts/.");
            CopyIfMissing(fontSource, Root + "/Fonts/Bansyu-retoromin.otf");
            CopyIfMissing(Path.Combine(Path.GetDirectoryName(fontSource), "readme.txt"), Root + "/Fonts/readme.txt");
        }

        static void CopyIfMissing(string source, string destination)
        {
            if (!File.Exists(destination)) File.Copy(source, destination);
        }

        static TMP_FontAsset CreateFont()
        {
            FontEngine.InitializeFontEngine();
            const string path = Root + "/Fonts/GameFont.asset";
            var result = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            // The supplied font omits the grave accent; no game text uses it.
            string characters = new string(Enumerable.Range(32, 95).Where(c => c != 96).Select(c => (char)c).ToArray()) +
                "クリア！あきをあつめて、たきをのぼれ。いどうでスタートもういちどアケビカキイチジク";
            bool incomplete = result == null || result.material == null || result.atlasTextures == null ||
                result.atlasTextures.Length == 0 || result.atlasTextures[0] == null;
            if (incomplete)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Bansyu-retoromin.otf");
                var created = TMP_FontAsset.CreateFontAsset(source, 72, 7, GlyphRenderMode.SDFAA, 2048, 2048,
                    AtlasPopulationMode.Dynamic, true);
                if (!created.TryAddCharacters(characters, out string absent))
                    throw new Exception("Font missing game glyphs: " + absent);
                created.name = "GameFont";
                if (result == null)
                {
                    result = created;
                    AssetDatabase.CreateAsset(result, path);
                }
                else
                {
                    // Repair an interrupted initial generation without changing the font asset GUID.
                    EditorUtility.CopySerialized(created, result);
                    Object.DestroyImmediate(created);
                }
                foreach (var atlas in result.atlasTextures)
                {
                    atlas.name = "GameFont Atlas";
                    AssetDatabase.AddObjectToAsset(atlas, result);
                }
                result.material.name = "GameFont Material";
                AssetDatabase.AddObjectToAsset(result.material, result);
            }
            result.ReadFontAssetDefinition();
            if (!result.HasCharacters(characters) && !result.TryAddCharacters(characters, out string missing))
                throw new Exception("Font missing glyphs: " + missing);
            EditorUtility.SetDirty(result);
            EditorUtility.SetDirty(result.material);
            foreach (var atlas in result.atlasTextures) EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            return result;
        }

        static Material Mat(string name, Color color, bool unlit = false)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            material.name = name;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .15f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static Transform Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static FlowObject CreateItem(ItemDefinition item)
        {
            var root = new GameObject("Item_" + item.kind);
            root.transform.localScale = Vector3.one * 1.2f;
            root.layer = 8;
            var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var trigger = root.AddComponent<SphereCollider>(); trigger.radius = .72f; trigger.isTrigger = true;
            var flow = root.AddComponent<FlowObject>();
            flow.definition = item;
            flow.visual = Child("Visual", root.transform);
            var material = Mat("Fruit_" + item.kind, item.displayColor);
            Vector3 size = item.kind == ItemKind.A ? new Vector3(.7f, 1.15f, .65f) :
                item.kind == ItemKind.K ? new Vector3(.95f, .88f, .9f) : new Vector3(.8f, 1.1f, .75f);
            Primitive("Fruit", PrimitiveType.Sphere, flow.visual, Vector3.zero, size, material);
            Primitive("Stem", PrimitiveType.Cube, flow.visual, new Vector3(0, .55f, 0), new Vector3(.09f, .25f, .09f), bark);
            Primitive("Leaf", PrimitiveType.Sphere, flow.visual, new Vector3(.12f, .45f, 0), new Vector3(.4f, .1f, .25f), leafGold);
            WorldText(item.kind.ToString(), root.transform, new Vector3(0, 0, -.68f), 5.5f, Color.white);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Item" + item.kind + ".prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<FlowObject>();
        }

        static FlowObject CreateLog()
        {
            var root = new GameObject("FlowingLog"); root.layer = 8;
            var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var trigger = root.AddComponent<BoxCollider>(); trigger.size = new Vector3(2.3f, .6f, .75f); trigger.isTrigger = true;
            var flow = root.AddComponent<FlowObject>(); flow.obstacle = true;
            flow.visual = Child("Visual", root.transform);
            var model = Model("log", 2.5f, bark);
            model.transform.SetParent(flow.visual, false);
            Bounds bounds = BoundsOf(model);
            if (bounds.size.z > bounds.size.x) model.transform.localRotation = Quaternion.Euler(0, 90, 0);
            else if (bounds.size.y > bounds.size.x) model.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/FlowingLog.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<FlowObject>();
        }

        static GameObject Model(string name, float size, Material material)
        {
            var root = new GameObject(name);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/" + name + ".fbx");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(root.transform, false);
            Bounds bounds = BoundsOf(root);
            float factor = size / Mathf.Max(.001f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
            model.transform.localScale *= factor;
            model.transform.position -= BoundsOf(root).center;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    string original = materials[m] != null ? materials[m].name.ToLowerInvariant() : "";
                    materials[m] = name.StartsWith("tree") && (original.Contains("wood") || original.Contains("bark")) ? bark : material;
                }
                renderer.sharedMaterials = materials;
            }
            return root;
        }

        static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            Bounds result = renderers[0].bounds;
            foreach (var renderer in renderers) result.Encapsulate(renderer.bounds);
            return result;
        }

        static void CreateScene(GameSettings settings)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var controller = new GameObject("Game").AddComponent<GameController>();
            controller.settings = settings;
            controller.flowRoot = Child("FlowObjects", controller.transform);
            var player = new GameObject("Salmon").AddComponent<PlayerController>();
            player.game = controller;
            controller.player = player;
            player.visual = Child("Visual", player.transform);
            player.GetComponent<CapsuleCollider>().height = 1.5f;
            player.GetComponent<CapsuleCollider>().radius = .44f;
            var rigidbody = player.GetComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            Primitive("Body", PrimitiveType.Capsule, player.visual, Vector3.zero, new Vector3(.7f, .65f, .5f), fish);
            Primitive("Belly", PrimitiveType.Capsule, player.visual, new Vector3(0, -.05f, -.19f), new Vector3(.49f, .55f, .22f), cream);
            player.tail = Child("Tail", player.visual); player.tail.localPosition = new Vector3(0, -.68f, 0);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var tailPart = Primitive("TailFin", PrimitiveType.Cube, player.tail, new Vector3(sign * .16f, -.12f, 0), new Vector3(.3f, .55f, .13f), fin);
                tailPart.transform.localRotation = Quaternion.Euler(0, 0, sign * 35);
                Primitive("Eye", PrimitiveType.Sphere, player.visual, new Vector3(sign * .24f, .34f, -.24f), Vector3.one * .2f, cream);
                Primitive("Pupil", PrimitiveType.Sphere, player.visual, new Vector3(sign * .24f, .36f, -.33f), Vector3.one * .10f, dark);
                var sideFin = Primitive("SideFin", PrimitiveType.Cube, player.visual, new Vector3(sign * .4f, -.16f, 0), new Vector3(.35f, .35f, .1f), fin);
                sideFin.transform.localRotation = Quaternion.Euler(0, 0, sign * 30);
            }
            var playerPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(player.gameObject, Root + "/Prefabs/Salmon.prefab", InteractionMode.AutomatedAction);
            // The scene references stay on the instance; the prefab contains only the player itself.
            player.game = controller;
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 12.5f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 250;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .1f, .12f);
            camera.gameObject.AddComponent<AudioListener>();
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.5f; light.color = new Color(1, .85f, .66f);
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.52f, .64f, .65f);
            var world = new GameObject("Waterfall").AddComponent<WaterfallView>();
            world.game = controller; world.followCamera = camera;
            float height = settings.goalHeight;
            float width = settings.waterfallWidth;
            world.decorationHeight = height;
            world.decorationWidth = width;
            Primitive("Water", PrimitiveType.Cube, world.transform, new Vector3(0, height * .5f, 2), new Vector3(width, height + 50, .6f), water);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Primitive("Riverbank", PrimitiveType.Cube, world.transform, new Vector3(sign * (width * .5f + 2.5f), height * .5f, 2.5f), new Vector3(5, height + 35, 4), rock);
                Primitive("EdgeFoam", PrimitiveType.Cube, world.transform, new Vector3(sign * (width * .5f - .1f), height * .5f, 1.45f), new Vector3(.12f, height + 25, .08f), foam);
            }
            var previousRandom = UnityEngine.Random.state;
            UnityEngine.Random.InitState(2026);
            for (int n = 0; n < Mathf.CeilToInt(height / 7) + 3; n++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var chunk = Model(n % 2 == 0 ? "rock_largeA" : "rock_largeB", UnityEngine.Random.Range(3.5f, 6), rock);
                    chunk.transform.SetParent(world.transform, false);
                    chunk.transform.position = new Vector3(sign * (width * .5f + UnityEngine.Random.Range(1, 3)), n * 7 - 4, .3f);
                    chunk.transform.rotation = UnityEngine.Random.rotation;
                    if (n % 2 == 0)
                    {
                        var tree = Model(n % 4 == 0 ? "tree_default_fall" : "tree_oak_fall", 5.2f, n % 4 == 0 ? leaf : leafGold);
                        tree.transform.SetParent(world.transform, false);
                        tree.transform.position = new Vector3(sign * (width * .5f + UnityEngine.Random.Range(5, 9)), n * 7, .5f);
                    }
                }
            }
            world.waterStreaks = new Transform[45];
            for (int n = 0; n < world.waterStreaks.Length; n++)
                world.waterStreaks[n] = Primitive("WaterStreak", PrimitiveType.Cube, world.transform,
                    new Vector3(UnityEngine.Random.Range(-width * .475f, width * .475f), UnityEngine.Random.Range(-12, 26), 1.48f),
                    new Vector3(UnityEngine.Random.Range(.035f, .1f), UnityEngine.Random.Range(.7f, 3.4f), .03f), foam).transform;
            UnityEngine.Random.state = previousRandom;
            world.goalMarker = Child("sAKI Summit", world.transform);
            world.goalMarker.position = Vector3.up * height;
            Primitive("Finish", PrimitiveType.Cube, world.goalMarker, new Vector3(0, 0, .7f), new Vector3(width, .25f, .5f), leafGold);
            WorldText("sAKI", world.goalMarker, new Vector3(0, 1.5f, 0), 12, new Color(1, .85f, .5f));
            world.followCamera.transform.position = world.cameraOffset;
            world.followCamera.transform.LookAt(Vector3.up * world.viewAhead);

            var particles = new GameObject("PickupSplash").AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main; main.playOnAwake = false; main.startLifetime = .5f;
            main.startSpeed = 3; main.startSize = .13f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission; emission.enabled = false;
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = foam;
            controller.pickupParticles = particles;
            controller.audioSource = controller.gameObject.AddComponent<AudioSource>();
            controller.audioSource.playOnAwake = false;
            controller.pickupSound = Tone("Pickup", 660, .13f);
            controller.boostSound = Tone("Boost", 880, .35f);
            controller.hitSound = Tone("Hit", 110, .17f);
            controller.clearSound = Tone("Clear", 1046, .65f);
            controller.hud = CreateHud(controller);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static TextMeshPro WorldText(string value, Transform parent, Vector3 position, float size, Color color)
        {
            var text = new GameObject(value).AddComponent<TextMeshPro>();
            text.transform.SetParent(parent, false); text.transform.localPosition = position;
            text.font = font; text.fontSize = size; text.text = value; text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = new Vector2(12, 4);
            return text;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }

        static Image Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var rect = Rect(name, parent, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }

        static TextMeshProUGUI Text(string value, Transform parent, Vector2 anchor, Vector2 position, Vector2 size,
            float fontSize, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var rect = Rect(value, parent, anchor, position, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = fontSize; text.color = color;
            text.alignment = alignment; text.raycastTarget = false;
            return text;
        }

        static GameHud CreateHud(GameController game)
        {
            Color paper = new Color(1, .92f, .76f);
            Color muted = new Color(.62f, .78f, .74f);
            Color panel = new Color(.025f, .10f, .12f, .92f);
            var canvas = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            var hud = canvas.gameObject.AddComponent<GameHud>(); hud.game = game;
            Text("tAKI", canvas.transform, new Vector2(0, 1), new Vector2(52, -33), new Vector2(320, 100), 83, paper, TextAlignmentOptions.Left);
            Text("AUTUMN ASCENT", canvas.transform, new Vector2(0, 1), new Vector2(56, -130), new Vector2(300, 30), 16, muted, TextAlignmentOptions.Left);
            Text("あきをあつめて、\nたきをのぼれ。", canvas.transform, new Vector2(0, 1), new Vector2(56, -180), new Vector2(300, 85), 22, paper, TextAlignmentOptions.TopLeft);
            var goalPanel = Panel("Altitude", canvas.transform, Vector2.one, new Vector2(-48, -48), new Vector2(270, 122), panel);
            Text("sAKI", goalPanel.transform, new Vector2(1, 1), new Vector2(-22, -10), new Vector2(220, 34), 26, paper, TextAlignmentOptions.Right);
            hud.altitude = Text($"000 / {game.settings.displayedGoalMeters:000} m", goalPanel.transform, new Vector2(1, 1), new Vector2(-22, -48), new Vector2(220, 32), 24, muted, TextAlignmentOptions.Right);
            var bar = Panel("Progress", goalPanel.transform, new Vector2(.5f, 0), new Vector2(0, 17), new Vector2(224, 5), new Color(.15f, .28f, .28f));
            hud.progressFill = Panel("Fill", bar.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Color(1, .73f, .28f));
            hud.progressFill.rectTransform.anchorMin = Vector2.zero; hud.progressFill.rectTransform.anchorMax = Vector2.one;
            hud.progressFill.rectTransform.offsetMin = hud.progressFill.rectTransform.offsetMax = Vector2.zero;
            hud.timeLabel = Text("00.0 s", canvas.transform, new Vector2(1, 1), new Vector2(-52, -185), new Vector2(260, 35), 23, muted, TextAlignmentOptions.Right);
            Text("COLLECT / A + K + I", canvas.transform, Vector2.zero, new Vector2(56, 166), new Vector2(320, 30), 17, muted, TextAlignmentOptions.Left);
            hud.collectionCards = new Image[3]; hud.collectionLetters = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                var card = Panel("Card" + (ItemKind)i, canvas.transform, Vector2.zero, new Vector2(54 + i * 100, 65), new Vector2(86, 86), panel);
                hud.collectionCards[i] = card;
                hud.collectionLetters[i] = Text(((ItemKind)i).ToString(), card.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 80), 51, game.settings.items[i].displayColor);
            }
            Text("3 TYPES = AKI BOOST", canvas.transform, Vector2.zero, new Vector2(56, 25), new Vector2(330, 27), 15, muted, TextAlignmentOptions.Left);
            Text("W\nA  S  D", canvas.transform, new Vector2(1, 0), new Vector2(-54, 67), new Vector2(200, 85), 30, paper, TextAlignmentOptions.Right);
            Text("いどう", canvas.transform, new Vector2(1, 0), new Vector2(-54, 28), new Vector2(200, 30), 18, muted, TextAlignmentOptions.Right);
            hud.readyPrompt = Text("WASDでスタート", canvas.transform, new Vector2(.5f, 0), new Vector2(0, 115), new Vector2(520, 62), 34, paper);
            hud.notification = Text("AKI BOOST!", canvas.transform, new Vector2(.5f, .5f), new Vector2(0, 100), new Vector2(600, 90), 49, paper);
            hud.notification.gameObject.SetActive(false);
            var overlay = Panel("Clear", canvas.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(2400, 1600), new Color(.025f, .08f, .095f, .94f));
            hud.clearPanel = overlay.gameObject;
            hud.clearTitle = Text("KurIA", overlay.transform, new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(800, 180), 136, paper);
            hud.clearLabel = Text("クリア！", overlay.transform, new Vector2(.5f, .5f), new Vector2(0, -5), new Vector2(800, 90), 55, new Color(1, .73f, .28f));
            hud.clearTime = Text("0.0 s", overlay.transform, new Vector2(.5f, .5f), new Vector2(0, -100), new Vector2(800, 60), 27, muted);
            Text("WASDでもういちど", overlay.transform, new Vector2(.5f, .5f), new Vector2(0, -205), new Vector2(800, 60), 25, paper);
            overlay.gameObject.SetActive(false);
            return hud;
        }

        static AudioClip Tone(string name, float frequency, float duration)
        {
            string path = Root + "/Audio/" + name + ".wav";
            if (!File.Exists(path))
            {
                const int rate = 22050;
                int count = (int)(rate * duration);
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                    writer.Write((short)2); writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                    for (int n = 0; n < count; n++)
                    {
                        float t = n / (float)rate;
                        float envelope = Mathf.Min(1, t * 100) * Mathf.Pow(1 - t / duration, 2);
                        double wave = Math.Sin(2 * Math.PI * frequency * t * (1 + .1f * t));
                        writer.Write((short)(wave * envelope * 11000));
                    }
                }
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "tAKI Jam";
            PlayerSettings.productName = "tAKI";
            PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            tags.FindProperty("layers").GetArrayElementAtIndex(8).stringValue = "FlowObject";
            tags.ApplyModifiedProperties();
        }
    }
}
