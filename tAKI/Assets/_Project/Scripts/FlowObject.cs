using UnityEngine;

namespace Taki
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FlowObject : MonoBehaviour
    {
        public ItemDefinition definition;
        public bool obstacle;
        public float fallSpeed = 2;
        public Transform visual;
        public bool Used { get; private set; }
        GameController game;
        Rigidbody body;
        float initialAngle;

        public void Initialize(GameController owner, ItemDefinition item)
        {
            game = owner;
            definition = item;
            body = GetComponent<Rigidbody>();
            if (item != null) fallSpeed = owner.settings.ScaleItemFallSpeed(item.fallSpeed, Random.value);
            else fallSpeed = owner.settings.obstacleFallSpeed;
            initialAngle = Random.Range(-12, 12);
        }

        void FixedUpdate()
        {
            if (game == null || game.State != GameState.Playing) return;
            body.MovePosition(body.position + Vector3.down * (fallSpeed * Time.fixedDeltaTime));
            if (body.position.y < game.FlowDespawnHeight || body.position.y < -5)
                Destroy(gameObject);
        }

        void Update()
        {
            if (game == null || game.State != GameState.Playing || visual == null) return;
            visual.localRotation = obstacle ? Quaternion.Euler(0, 0, initialAngle + Mathf.Sin(Time.time) * 5) :
                Quaternion.Euler(0, Mathf.Sin(Time.time * 1.4f + initialAngle) * 22, Mathf.Sin(Time.time * 2 + initialAngle) * 7);
        }

        public void Interact(PlayerController player)
        {
            if (game == null || game.State != GameState.Playing || !gameObject.activeSelf) return;
            // AKI removes the log. Ordinary contacts leave it active, so it can
            // cause another hit after the player's protection expires.
            if (obstacle && player.IsAkiInvulnerable)
            {
                Used = true;
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
            if (Used) return;
            if (obstacle)
            {
                if (!game.TryHit()) return;
            }
            else
            {
                if (definition == null) return;
                Used = true;
                game.Collect(definition, transform.position);
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
        }
    }
}
