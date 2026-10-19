using UnityEngine;

namespace Taki
{
    public enum ItemKind { A, K, I }

    [CreateAssetMenu(menuName = "tAKI/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        public ItemKind kind;
        public string displayName;
        [Min(0)] public float spawnWeight = 1;
        [Tooltip("Minimum stored upward speed after collecting this item, in units/s. Faster stored speed is preserved; actual motion is capped separately.")]
        [Min(0)] public float upwardImpulse = 5;
        [Min(0)] public float fallSpeed = 2.6f;
        public Color displayColor = Color.white;
        public FlowObject prefab;

        public static ItemDefinition Choose(ItemDefinition[] items, float sample)
        {
            if (items == null) return null;
            float total = 0;
            foreach (var item in items)
                if (item != null) total += Mathf.Max(0, item.spawnWeight);
            if (total <= 0) return null;
            float point = Mathf.Clamp01(sample) * total;
            ItemDefinition last = null;
            foreach (var item in items)
            {
                if (item == null || item.spawnWeight <= 0) continue;
                last = item;
                if (point < item.spawnWeight) return item;
                point -= item.spawnWeight;
            }
            return last;
        }
    }
}
