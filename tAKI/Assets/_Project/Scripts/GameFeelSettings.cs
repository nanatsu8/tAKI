using UnityEngine;

namespace Taki
{
    [CreateAssetMenu(menuName = "tAKI/Game Feel Settings")]
    public sealed class GameFeelSettings : ScriptableObject
    {
        [Header("Pickup")]
        [Min(.05f)] public float pickupSeconds = .22f;
        [Min(0)] public float pickupRingRadius = 1.4f;
        [Range(0, 64)] public int pickupParticles = 18;
        [Range(0, .5f)] public float fishStretch = .24f;
        [Header("Speed HUD")]
        [Min(.05f)] public float speedPunchSeconds = .15f;
        [Range(0, .5f)] public float speedPunchScale = .16f;
        [Header("Water lines")]
        [Range(0, 64)] public int waterLineCount = 32;
        [Range(0, 1)] public float waterLineAlpha = .32f;
        [Header("AKI")]
        public Color boostColor = new Color(1, .8f, .3f);
        [Min(.05f)] public float boostPunchSeconds = .55f;
        [Range(0, .3f)] public float boostZoomOut = .09f;
        [Min(0)] public float boostTrailSeconds = .28f;
        [Header("Combo")]
        public int[] comboMilestones = { 5, 10, 20 };
    }

    // These functions only describe presentation; gameplay values are never changed.
    public static class GameFeelMath
    {
        public static float Reserve(float storedSpeed, float movementCap, float storedCap)
        {
            float range = Mathf.Max(movementCap, storedCap) - movementCap;
            return range > 0 && movementCap > 0 ? Mathf.Clamp01((storedSpeed - movementCap) / range) : 0;
        }

        public static bool AtMaxSpeed(float speed, float cap) => cap > 0 && speed >= cap - .001f;

        public static float WaterIntensity(float speedFraction) => Mathf.SmoothStep(0, 1,
            Mathf.InverseLerp(.2f, 1, speedFraction));

        public static int CrossedMilestone(int previous, int current, int[] milestones)
        {
            int crossed = 0;
            if (milestones == null) return 0;
            foreach (int value in milestones)
                if (value > 0 && previous < value && current >= value) crossed = Mathf.Max(crossed, value);
            return crossed;
        }
    }
}
