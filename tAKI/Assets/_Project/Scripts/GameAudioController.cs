using UnityEngine;

namespace Taki
{
    [DisallowMultipleComponent]
    public sealed class GameAudioController : MonoBehaviour
    {
        public GameAudioSettings Settings { get; private set; }
        public float MasterVolume { get; private set; }
        public AudioSource MusicSource => music;
        public bool IsConfigured => Settings != null;
        public bool IsMusicPlaying => music != null && music.isPlaying;
        public float MusicTime => music != null ? music.time : 0;
        static float sessionVolume = -1;
        static AudioSource persistentMusic;
        AudioSource music;
        AudioSource effects;
        AudioSource celebration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            sessionVolume = -1;
            persistentMusic = null;
        }

        public void Initialize(GameAudioSettings settings)
        {
            if (Settings != null || settings == null) return;
            Settings = settings;
            bool startMusic = persistentMusic == null;
            if (startMusic)
            {
                persistentMusic = CreateSource("BGM", true);
                persistentMusic.transform.SetParent(null);
                DontDestroyOnLoad(persistentMusic.gameObject);
                persistentMusic.clip = settings.backgroundMusic;
            }
            music = persistentMusic;
            effects = CreateSource("Effects", false);
            celebration = CreateSource("ClearSound", false);
            MasterVolume = Mathf.Clamp01(sessionVolume >= 0 ? sessionVolume : settings.defaultMasterVolume);
            ApplyVolumes();
            if (startMusic && music.clip != null)
            {
                music.time = 0;
                music.Play();
            }
        }

        AudioSource CreateSource(string name, bool loop)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.loop = loop;
            source.pitch = 1;
            return source;
        }

        public void SetMasterVolume(float value)
        {
            MasterVolume = Mathf.Clamp01(value);
            sessionVolume = MasterVolume;
            if (IsConfigured) ApplyVolumes();
        }

        void ApplyVolumes()
        {
            music.volume = MasterVolume * Mathf.Clamp01(Settings.musicVolume);
            effects.volume = MasterVolume * Mathf.Clamp01(Settings.effectsVolume);
            celebration.volume = MasterVolume * Mathf.Clamp01(Settings.clearVolume);
        }

        public void PlayPickup(bool completedSet)
        {
            if (!IsConfigured) return;
            var clip = completedSet ? Settings.akiBoost : Settings.pickup;
            if (clip != null) effects.PlayOneShot(clip);
        }

        public void PlayHit()
        {
            if (IsConfigured && Settings.hit != null) effects.PlayOneShot(Settings.hit);
        }

        public void PlayClear()
        {
            if (!IsConfigured) return;
            effects.Stop();
            if (Settings.clear != null) celebration.PlayOneShot(Settings.clear);
        }
    }

}
