using UnityEngine;

namespace Taki
{
    [CreateAssetMenu(menuName = "tAKI/Audio Settings")]
    public sealed class GameAudioSettings : ScriptableObject
    {
        public AudioClip backgroundMusic;
        public AudioClip pickup;
        public AudioClip akiBoost;
        public AudioClip hit;
        public AudioClip clear;
        [Range(0, 1)] public float defaultMasterVolume = .5f;
        [Range(0, 1)] public float musicVolume = .6f;
        [Range(0, 1)] public float effectsVolume = .8f;
        [Range(0, 1)] public float clearVolume = .8f;
    }
}
