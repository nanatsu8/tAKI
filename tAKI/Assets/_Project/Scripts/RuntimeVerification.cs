using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Taki
{
    // Runs only when the executable is explicitly launched with -takiSmoke.
    public sealed class RuntimeVerification : MonoBehaviour
    {
        public GameController game;
        public static bool Started { get; private set; }
        Keyboard keyboard;
        Mouse mouse;
        string output;
        bool failed;

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;
        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) failed = true;
        }

        IEnumerator Start()
        {
            Started = true;
            DontDestroyOnLoad(gameObject);
            output = Path.Combine(Application.dataPath, "..", "Verification");
            bool playthrough = Array.IndexOf(Environment.GetCommandLineArgs(), "-takiPlaythrough") >= 0;
            if (playthrough) output = Path.Combine(output, "Playthrough");
            Directory.CreateDirectory(output);
            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            yield return null;
            Require(game.State == GameState.Ready, "Starts in the playable scene, waiting for WASD");
            Require(game.Feedback != null && game.Feedback.HudEffects != null && game.Feedback.BoostTrail != null,
                "Gameplay feedback initializes automatically in the existing scene");
            yield return Screenshot("01-ready.png");
            if (playthrough) { yield return Playthrough(); yield break; }
            Require(game.Audio != null && game.Audio.IsConfigured && game.Audio.Settings.backgroundMusic.name == "flow-基本BGM",
                "Supplied BGM is configured in the ready screen");
            Require(game.hud.VolumeControl != null && game.hud.VolumeControl.gameObject.activeSelf,
                "The ready screen shows volume controls");
            yield return CheckVolumeDrag();
            Keys(Key.W, Key.D);
            yield return new WaitForSeconds(.25f);
            Require(game.State == GameState.Playing && game.player.transform.position.x > .5f,
                "Input System WASD starts play and moves right");
            Require(!game.hud.VolumeControl.gameObject.activeSelf, "Starting play hides the volume bar");
            Keys();
            ClearFlow();

            game.player.Body.position = new Vector3(0, 15, 0);
            game.player.SetVerticalSpeed(100);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Require(game.player.Body.velocity.y <= game.settings.maxRiseSpeed, "Rise speed is clamped");
            Require(game.player.InternalVerticalSpeed <= Mathf.Max(game.settings.maxStoredRiseSpeed, game.settings.maxRiseSpeed),
                "Stored upward velocity obeys its own cap");
            Require(game.hud.speedLabel != null && game.hud.speedLabel.text.Contains("m/s"),
                "HUD shows actual vertical speed in displayed meters per second");
            game.player.SetVerticalSpeed(-100);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Require(game.player.Body.velocity.y >= -game.settings.maxFallSpeed, "Fall speed is clamped separately");
            yield return null;
            Require(game.player.ActualVerticalSpeed < 0 && game.hud.speedLabel.text.Contains("-"),
                "HUD preserves the negative sign while descending");

            game.player.SetVerticalSpeed(-game.settings.maxFallSpeed);
            game.player.Body.velocity = new Vector3(2, game.player.Body.velocity.y, 0);
            game.Collection.Reset();
            int before = game.PickupCount;
            int feedbackBefore = game.Feedback.PickupFeedbackCount;
            int floatingBefore = game.Feedback.HudEffects.FloatingPickupCount;
            var first = game.SpawnItem(game.player.transform.position.y, 0, game.settings.items[0]);
            Require(first.fallSpeed >= game.settings.ScaleItemFallSpeed(game.settings.items[0].fallSpeed, 0) &&
                first.fallSpeed <= game.settings.ScaleItemFallSpeed(game.settings.items[0].fallSpeed, 1),
                "Spawned item receives a fall speed within the configured multiplier range");
            first.Interact(game.player);
            first.Interact(game.player);
            Require(game.PickupCount == before + 1, "A pickup only awards once");
            Require(game.Feedback.PickupFeedbackCount == feedbackBefore + 1 &&
                game.Feedback.HudEffects.FloatingPickupCount == floatingBefore + 1,
                "Duplicate contact does not replay pickup effects or create another floating letter");
            Require(game.Combo.Count == 0, "A descending pickup does not count its own upward impulse as a combo");
            Require(Mathf.Approximately(game.player.Body.velocity.y, game.settings.ClampVelocity(game.settings.items[0].upwardImpulse)) &&
                game.player.Body.velocity.x == 2 && game.player.Body.velocity.z == 0,
                "Pickup replaces descent with the item speed while preserving horizontal speed and fixed Z");
            float fasterRise = game.settings.ClampStoredVelocity(Mathf.Max(game.settings.maxRiseSpeed + 1,
                game.settings.items[0].upwardImpulse + 1));
            game.player.SetVerticalSpeed(fasterRise);
            var risingPickup = game.SpawnItem(game.player.transform.position.y, 0, game.settings.items[0]);
            risingPickup.Interact(game.player);
            risingPickup.Interact(game.player);
            Require(game.Combo.Count == 1, "A rising pickup increments combo once even with duplicate contact");
            Require(game.Collection.Mask == 1 && Mathf.Approximately(game.player.InternalVerticalSpeed, fasterRise) &&
                Mathf.Approximately(game.player.Body.velocity.y, game.settings.ClampVelocity(fasterRise)),
                "Duplicate A preserves stored surplus and capped motion without completing the set");
            game.SpawnItem(game.player.transform.position.y, 0, game.settings.items[1]).Interact(game.player);
            game.SpawnItem(game.player.transform.position.y, 0, game.settings.items[2]).Interact(game.player);
            Require(game.Collection.Mask == 0 && game.Collection.CompletedSets == 1, "AKI boosts once and resets collection");
            Require(game.Combo.Count == 3, "AKI completion counts its item once and preserves the combo");
            Require(game.player.Body.velocity.y <= game.settings.maxRiseSpeed, "Set bonus obeys the speed cap");
            Require(game.player.IsAkiInvulnerable, "AKI completion enables boost protection");
            Require(game.Feedback.BoostPulse > 0 && game.Feedback.HudEffects.GatheringLetters,
                "AKI triggers camera punch and gathering letters together");
            int protectedHits = game.HitCount;
            Vector3 protectedVelocity = game.player.Body.velocity;
            float protectedStoredVelocity = game.player.InternalVerticalSpeed;
            var protectedLog = game.SpawnObstacle(game.player.transform.position.y, 0);
            protectedLog.Interact(game.player);
            protectedLog.Interact(game.player);
            var secondProtectedLog = game.SpawnObstacle(game.player.transform.position.y, 0);
            secondProtectedLog.Interact(game.player);
            Require(protectedLog.Used && !protectedLog.gameObject.activeSelf &&
                secondProtectedLog.Used && !secondProtectedLog.gameObject.activeSelf,
                "AKI destroys touched obstacles immediately, including multiple obstacles in one frame");
            Require(game.HitCount == protectedHits && game.player.Body.velocity == protectedVelocity &&
                game.player.InternalVerticalSpeed == protectedStoredVelocity,
                "AKI obstacle contacts do not apply a hit or reduce velocity");
            Require(game.Combo.Count == 3, "AKI obstacle protection preserves the combo");
            yield return null;
            Require(game.hud.comboLabel != null && game.hud.comboLabel.gameObject.activeSelf &&
                game.hud.comboLabel.text == "3 COMBO", "HUD displays the current combo during ascent");
            Require(game.Feedback.BoostAura.enabled && game.Feedback.BoostTrail.emitting,
                "AKI protection enables the visible aura and wake");
            ClearFlow();
            game.enabled = false;
            yield return new WaitForSeconds(Mathf.Max(0, game.player.InvulnerableUntil - Time.time) + .05f);
            game.enabled = true;
            Require(!game.player.IsAkiInvulnerable, "Boost protection expires after its configured duration");

            ClearFlow();
            game.enabled = false;
            game.Collection.Add(ItemKind.K);
            int hits = game.HitCount;
            var repeatLog = game.SpawnObstacle(game.player.transform.position.y, 0);
            repeatLog.Interact(game.player);
            Require(game.HitCount == hits + 1, "Obstacle causes a hit");
            Require(game.Combo.Count == 0, "A hit resets combo immediately when it stops upward motion");
            repeatLog.Interact(game.player);
            Require(game.HitCount == hits + 1, "Invulnerability rejects another contact with the same obstacle");
            Require(game.Collection.Has(ItemKind.K), "Hit preserves collected letters");
            Require(Mathf.Abs(game.player.Body.velocity.y + game.settings.hitFallSpeed) < .01f, "Hit removes upward momentum");
            Require(game.player.InternalVerticalSpeed == game.settings.ClampStoredVelocity(-game.settings.hitFallSpeed),
                "Hit removes stored upward momentum as well as actual speed");
            var ignoredLog = game.SpawnObstacle(game.player.transform.position.y, 0);
            ignoredLog.Interact(game.player);
            Require(!ignoredLog.Used && ignoredLog.gameObject.activeSelf,
                "Ordinary hit protection does not destroy obstacles");
            ClearFlow();
            yield return new WaitForSeconds(Mathf.Max(0, game.player.InvulnerableUntil - Time.time) + .05f);
            game.player.SetVerticalSpeed(game.settings.maxRiseSpeed);
            repeatLog.gameObject.SetActive(true);
            repeatLog.Interact(game.player);
            Require(game.HitCount == hits + 2 && !repeatLog.Used && repeatLog.gameObject.activeSelf,
                "The same obstacle causes another hit after invulnerability expires");
            Require(Mathf.Abs(game.player.Body.velocity.y + Mathf.Min(game.settings.hitFallSpeed, game.settings.maxFallSpeed)) < .01f,
                "Repeated obstacle contact applies the speed penalty again");
            ClearFlow();
            yield return new WaitForSeconds(Mathf.Max(0, game.player.InvulnerableUntil - Time.time) + .05f);
            game.enabled = true;

            ClearFlow();
            game.player.Body.position = new Vector3(0, 30, 0);
            game.player.SetVerticalSpeed(game.settings.maxRiseSpeed);
            before = game.PickupCount;
            game.SpawnItem(31.1f, 0, game.settings.items[0]);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.15f);
            Require(game.PickupCount > before, "Fast ascent collects objects along the movement path");
            ClearFlow();
            game.player.Body.position = new Vector3(0, 45, 0);
            game.player.SetVerticalSpeed(game.settings.maxRiseSpeed);
            hits = game.HitCount;
            game.SpawnObstacle(46.1f, 0);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.15f);
            Require(game.HitCount == hits + 1, "Fast ascent detects a moving obstacle");
            ClearFlow();

            game.player.Body.position = new Vector3(0, 70, 0);
            game.player.SetVerticalSpeed(3);
            Keys(Key.W);
            yield return new WaitForSeconds(.3f);
            yield return Screenshot("02-playing.png");
            Keys();
            game.player.Body.position = new Vector3(0, game.settings.goalHeight - .05f, 0);
            game.player.SetVerticalSpeed(game.settings.maxRiseSpeed);
            yield return new WaitForSeconds(.2f);
            Require(game.State == GameState.Clear, "Crossing the summit at speed clears the stage");
            Require(game.hud.clearTitle.text == "KurIA" && game.hud.clearLabel.text == "クリア！",
                "Clear labels preserve the exact spelling");
            Require(game.hud.clearTitle.rectTransform.anchoredPosition.y > game.hud.clearLabel.rectTransform.anchoredPosition.y,
                "KurIA appears above the Japanese clear label");
            Require(!game.TryHit(), "No damage after clear");
            float stoppedTime = game.Elapsed;
            yield return new WaitForSeconds(.3f);
            Require(game.Elapsed == stoppedTime && game.player.Body.velocity == Vector3.zero && game.player.InternalVerticalSpeed == 0,
                "Clear freezes play and resets actual and stored velocity");
            Require(!game.hud.comboLabel.gameObject.activeSelf, "Clear hides the combo display");
            Require(!game.Feedback.BoostAura.enabled && !game.Feedback.BoostTrail.emitting &&
                game.Feedback.BoostTrail.positionCount == 0,
                "Clear removes protection effects and any remaining wake");
            yield return Screenshot("03-clear.png");
            yield return new WaitForSeconds(1.3f);
            Require(game.Audio.IsMusicPlaying, "BGM continues playing after clear");
            var retryMusic = game.Audio.MusicSource;
            float retryVolume = game.Audio.MasterVolume;
            Keys(Key.W);
            yield return new WaitForSeconds(.2f);
            game = FindObjectOfType<GameController>();
            Require(game != null && game.Elapsed < 1 && game.player.transform.position.y < 5,
                "Release and press WASD restarts the stage without an extra key");
            Require(game != null && !game.player.IsAkiInvulnerable && game.player.InvulnerableUntil == 0,
                "Restart clears AKI and hit protection");
            Require(game != null && game.Combo.Count == 0, "Retry starts with no combo");
            Require(game != null && Mathf.Approximately(game.Audio.MasterVolume, retryVolume) &&
                game.Audio.MusicSource == retryMusic && game.Audio.IsMusicPlaying,
                "Retry preserves volume and reuses the same uninterrupted BGM source");
            Finish();
        }

        IEnumerator Playthrough()
        {
            Time.timeScale = 4;
            Keys(Key.W);
            float deadline = Time.realtimeSinceStartup + 75;
            float nextLog = 10;
            bool captured = false;
            while (game.State != GameState.Clear && Time.realtimeSinceStartup < deadline)
            {
                Vector3 position = game.player.transform.position;
                float vy = game.player.Body.velocity.y;
                float targetX = 0;
                float bestScore = float.MaxValue;
                foreach (Transform child in game.flowRoot)
                {
                    var flow = child.GetComponent<FlowObject>();
                    if (flow == null || flow.obstacle || flow.Used || !child.gameObject.activeSelf) continue;
                    float dy = child.position.y - position.y;
                    if (dy < -.5f) continue;
                    float eta = Mathf.Max(0, dy) / Mathf.Max(.5f, flow.fallSpeed + vy);
                    float distance = Mathf.Abs(child.position.x - position.x);
                    if (distance / game.settings.horizontalSpeed > eta + .22f) continue;
                    float score = eta + distance * .04f;
                    if (score < bestScore) { bestScore = score; targetX = child.position.x; }
                }
                foreach (Transform child in game.flowRoot)
                {
                    var flow = child.GetComponent<FlowObject>();
                    if (flow == null || !flow.obstacle || flow.Used || !child.gameObject.activeSelf) continue;
                    float dy = child.position.y - position.y;
                    float eta = dy / Mathf.Max(.5f, flow.fallSpeed + vy);
                    if (dy > -.5f && eta < .65f && Mathf.Abs(position.x - child.position.x) < 1.9f)
                    {
                        float direction = position.x < child.position.x ? -1 : 1;
                        targetX = Mathf.Clamp(child.position.x + direction * 2.1f, -4.9f, 4.9f);
                        if (Mathf.Abs(targetX - child.position.x) < 1.7f)
                            targetX = Mathf.Clamp(child.position.x - direction * 2.1f, -4.9f, 4.9f);
                    }
                }
                if (targetX > position.x + .2f) Keys(Key.W, Key.D);
                else if (targetX < position.x - .2f) Keys(Key.W, Key.A);
                else Keys(Key.W);
                if (game.Elapsed >= nextLog)
                {
                    Debug.Log($"PLAYTHROUGH t={game.Elapsed:0}s height={position.y:0.0} pickups={game.PickupCount} hits={game.HitCount}");
                    nextLog += 10;
                }
                if (!captured && game.Progress > .45f)
                {
                    captured = true;
                    yield return Screenshot("02-playing.png");
                }
                yield return null;
            }
            Keys();
            Time.timeScale = 1;
            Require(game.State == GameState.Clear, "Full stage completed using only simulated WASD, without teleporting or forcing pickups");
            Debug.Log($"PLAYTHROUGH_RESULT time={game.Elapsed:0.0}s pickups={game.PickupCount} hits={game.HitCount} sets={game.Collection.CompletedSets}");
            yield return Screenshot("03-clear.png");
            Finish();
        }

        IEnumerator CheckVolumeDrag()
        {
            mouse = InputSystem.AddDevice<Mouse>();
            mouse.MakeCurrent();
            game.Audio.SetMasterVolume(.5f);
            game.hud.VolumeControl.Refresh();
            var track = game.hud.VolumeControl.Track;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(null, track.TransformPoint(new Vector3(0, 0, 0)));
            Vector2 quarter = RectTransformUtility.WorldToScreenPoint(null,
                track.TransformPoint(new Vector3(Mathf.Lerp(track.rect.xMin, track.rect.xMax, .25f), 0, 0)));
            Vector2 threeQuarters = RectTransformUtility.WorldToScreenPoint(null,
                track.TransformPoint(new Vector3(Mathf.Lerp(track.rect.xMin, track.rect.xMax, .75f), 0, 0)));
            Pointer(quarter, true);
            yield return null;
            Pointer(quarter, false);
            yield return null;
            Require(Mathf.Approximately(game.Audio.MasterVolume, .5f), "Clicking the volume track does not change volume");
            Pointer(center, true);
            yield return null;
            Pointer(threeQuarters, true);
            yield return null;
            Pointer(threeQuarters, false);
            yield return null;
            Require(Mathf.Abs(game.Audio.MasterVolume - .75f) < .02f, "Dragging the volume track changes volume");
        }

        void Pointer(Vector2 position, bool held)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = (ushort)(held ? 1 : 0) });
            InputSystem.Update();
        }

        void Finish()
        {
            Debug.Log(failed ? "TAKI_RUNTIME_CHECKS_FAILED" : "TAKI_RUNTIME_CHECKS_PASSED");
            File.WriteAllText(Path.Combine(output, "result.txt"), failed ? "FAIL" : "PASS");
            InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            Application.Quit(failed ? 1 : 0);
        }

        void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        void ClearFlow()
        {
            foreach (Transform child in game.flowRoot) child.gameObject.SetActive(false);
        }

        void Require(bool condition, string message)
        {
            if (condition) Debug.Log("PASS: " + message);
            else { failed = true; Debug.LogError("FAIL: " + message); }
        }

        IEnumerator Screenshot(string name)
        {
            yield return new WaitForEndOfFrame();
            // A hidden Windows player has no back buffer to capture. Render explicitly to an offscreen target.
            var camera = Camera.main;
            var canvas = game.hud.GetComponent<Canvas>();
            var originalMode = canvas.renderMode;
            var originalCamera = canvas.worldCamera;
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG());
                var pixels = image.GetPixels32();
                int min = 765, max = 0;
                for (int i = 0; i < pixels.Length; i += 137)
                {
                    int luminance = pixels[i].r + pixels[i].g + pixels[i].b;
                    min = Mathf.Min(min, luminance); max = Mathf.Max(max, luminance);
                }
                Require(max - min > 80, "Screenshot includes rendered content: " + name);
            }
            finally
            {
                canvas.renderMode = originalMode;
                canvas.worldCamera = originalCamera;
                camera.targetTexture = originalTarget;
                RenderTexture.active = originalActive;
                RenderTexture.ReleaseTemporary(target);
                Destroy(image);
            }
            yield return null;
        }
    }
}
