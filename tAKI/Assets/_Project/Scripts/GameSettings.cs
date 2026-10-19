using UnityEngine;

namespace Taki
{
    [CreateAssetMenu(menuName = "tAKI/Game Settings")]
    public sealed class GameSettings : ScriptableObject
    {
        [Header("Movement (units / seconds)")]
        [Min(0)] public float horizontalSpeed = 5;
        [Min(0)] public float gravity = 3;
        [Min(0)] public float verticalAssist = 1;
        [Min(0)] public float maxRiseSpeed = 14;
        [Tooltip("Stored upward speed limit, in units/s. Effective limit is at least Max Rise Speed; actual motion still uses Max Rise Speed.")]
        [Min(0)] public float maxStoredRiseSpeed = 30;
        [Min(0)] public float maxFallSpeed = 1.5f;
        [Min(2)] public float waterfallWidth = 24;
        [Tooltip("Actual summit height in world units.")]
        [Min(1)] public float goalHeight = 360;
        [Tooltip("Distance shown in meters when reaching the summit; independent of world units.")]
        [Min(1)] public float displayedGoalMeters = 100;
        [Header("Collection")]
        public ItemDefinition[] items;
        [Min(0)] public float setBonus = 8;
        [Tooltip("Invulnerability after completing AKI; touching obstacles destroys them without a hit penalty.")]
        [Min(0)] public float akiInvulnerabilitySeconds = .8f;
        [Tooltip("Vertical travel between item spawns, in either direction, in world units.")]
        [Min(.5f)] public float itemSpawnDistance = 6;
        [Tooltip("Additional items above the screen every N game seconds. Set to 0 to disable timed spawning.")]
        [Min(0)] public float itemTimedSpawnInterval = 1;
        [Tooltip("Minimum random multiplier applied to each item's base fall speed at spawn.")]
        [Min(0)] public float itemFallSpeedMultiplierMin = 1;
        [Tooltip("Maximum random multiplier applied to each item's base fall speed at spawn.")]
        [Min(0)] public float itemFallSpeedMultiplierMax = 3;
        [Tooltip("Extra world-unit clearance beyond the camera's top or bottom edge for item spawning.")]
        [Min(2)] public float itemOffscreenMargin = 2;
        [Tooltip("Obstacle spawn offset above the player, in world units.")]
        [Min(5)] public float spawnAhead = 18;
        [Header("Obstacles")]
        public FlowObject obstaclePrefab;
        [Min(0)] public float obstacleFallSpeed = 2.6f;
        [Range(0, 1)] public float middleAt = .3f;
        [Range(0, 1)] public float finalAt = .7f;
        [Tooltip("New summit height gained per obstacle in the early section, in world units.")]
        [Min(.5f)] public float earlyObstacleDistance = 30;
        [Tooltip("New summit height gained per obstacle in the middle section, in world units.")]
        [Min(.5f)] public float middleObstacleDistance = 17;
        [Tooltip("New summit height gained per obstacle in the final section, in world units.")]
        [Min(.5f)] public float finalObstacleDistance = 9;
        [Min(0)] public float hitFallSpeed = 1.5f;
        [Min(0)] public float invulnerabilitySeconds = .8f;

        public float ClampVelocity(float value) => Mathf.Clamp(value, -maxFallSpeed, maxRiseSpeed);
        public float ClampStoredVelocity(float value) => Mathf.Clamp(value, -maxFallSpeed,
            Mathf.Max(maxRiseSpeed, maxStoredRiseSpeed));
        public float StepStoredVelocity(float current, float input, float seconds, bool atFloor)
        {
            float value = ClampStoredVelocity(current +
                (-gravity + Mathf.Clamp(input, -1, 1) * verticalAssist) * Mathf.Max(0, seconds));
            return atFloor ? Mathf.Max(0, value) : value;
        }
        public float RiseSpeedFraction(float speed) => maxRiseSpeed > 0 ? Mathf.Clamp01(speed / maxRiseSpeed) : 0;
        public float ScaleItemFallSpeed(float baseSpeed, float sample)
        {
            float min = Mathf.Max(0, Mathf.Min(itemFallSpeedMultiplierMin, itemFallSpeedMultiplierMax));
            float max = Mathf.Max(min, Mathf.Max(itemFallSpeedMultiplierMin, itemFallSpeedMultiplierMax));
            return Mathf.Max(0, baseSpeed) * Mathf.Lerp(min, max, sample);
        }
        public float PickupVelocity(float currentVelocity, float upwardImpulse) =>
            ClampStoredVelocity(Mathf.Max(upwardImpulse, currentVelocity));
        public float DisplayedHeight(float height) => Mathf.Clamp01(height / Mathf.Max(1, goalHeight)) * displayedGoalMeters;
        public float DisplayedVerticalSpeed(float worldSpeed) => worldSpeed / Mathf.Max(1, goalHeight) * displayedGoalMeters;
        public float ObstacleDistance(float height)
        {
            float progress = Mathf.Clamp01(height / Mathf.Max(1, goalHeight));
            float spacing = progress >= finalAt ? finalObstacleDistance :
                progress >= middleAt ? middleObstacleDistance : earlyObstacleDistance;
            return Mathf.Max(.5f, spacing);
        }
    }

    public sealed class ObstacleSpawnDistanceState
    {
        public float NextHeight { get; private set; }

        public void Reset(float height, GameSettings settings)
        {
            NextHeight = height + settings.ObstacleDistance(height);
        }

        public bool TryAdvance(float height, GameSettings settings, out float threshold)
        {
            threshold = NextHeight;
            if (height < threshold) return false;
            // Never move this frontier backward, even after falling or destroying a log.
            NextHeight += settings.ObstacleDistance(threshold);
            return true;
        }
    }

    public sealed class ItemSpawnDistanceState
    {
        public int Direction { get; private set; } = 1;
        float previousHeight;
        float distance;

        public void Reset(float height)
        {
            previousHeight = height;
            distance = 0;
            Direction = 1;
        }

        public int Advance(float height, float spacing)
        {
            float movement = height - previousHeight;
            previousHeight = height;
            if (movement == 0) return 0;
            Direction = movement > 0 ? 1 : -1;
            distance += Mathf.Abs(movement);
            float step = Mathf.Max(.5f, spacing);
            int count = Mathf.FloorToInt(distance / step);
            distance -= count * step;
            return count;
        }
    }

    public sealed class ItemSpawnTimerState
    {
        float elapsed;

        public void Reset() => elapsed = 0;

        public int Advance(float seconds, float interval)
        {
            if (interval <= 0) { Reset(); return 0; }
            float step = Mathf.Max(.1f, interval);
            elapsed += Mathf.Max(0, seconds);
            // Avoid losing a boundary to float rounding after many small frame steps.
            int count = Mathf.FloorToInt((elapsed + .00001f) / step);
            elapsed = Mathf.Max(0, elapsed - count * step);
            return count;
        }
    }

    public sealed class ComboState
    {
        public int Count { get; private set; }

        public void RecordPickup(float verticalSpeedBeforePickup)
        {
            UpdateSpeed(verticalSpeedBeforePickup);
            if (verticalSpeedBeforePickup > 0) Count++;
        }

        public void UpdateSpeed(float verticalSpeed)
        {
            if (verticalSpeed <= 0) Reset();
        }

        public void Reset() => Count = 0;
    }

    public sealed class CollectionState
    {
        public int Mask { get; private set; }
        public int CompletedSets { get; private set; }
        public bool Has(ItemKind kind) => (Mask & (1 << (int)kind)) != 0;
        public bool Add(ItemKind kind)
        {
            Mask |= 1 << (int)kind;
            if (Mask != 7) return false;
            Mask = 0;
            CompletedSets++;
            return true;
        }
        public void Reset() { Mask = 0; CompletedSets = 0; }
    }
}
