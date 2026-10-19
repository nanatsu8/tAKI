using UnityEngine;
using UnityEngine.InputSystem;

namespace Taki
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class PlayerController : MonoBehaviour
    {
        public GameController game;
        public Transform visual;
        public Transform tail;
        public Rigidbody Body { get; private set; }
        public Vector2 MoveInput { get; private set; }
        public bool AnyMovementPressed { get; private set; }
        public float InternalVerticalSpeed => internalVerticalSpeed;
        public float ActualVerticalSpeed => Body != null ? Body.velocity.y : 0;
        [SerializeField, Tooltip("Current stored vertical speed. Actual Rigidbody speed is capped independently.")]
        float internalVerticalSpeed;
        public float InvulnerableUntil => protection.Until;
        public bool IsAkiInvulnerable => game.State == GameState.Playing && protection.IsBoostActive(Time.time);
        readonly InvulnerabilityState protection = new InvulnerabilityState();
        readonly RaycastHit[] sweepHits = new RaycastHit[32];
        readonly Collider[] overlaps = new Collider[32];
        Vector3 visualScale;
        float pulse;
        const int FlowMask = 1 << 8;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.useGravity = false;
            Body.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            visualScale = visual.localScale;
        }

        void Update()
        {
            var keys = Keyboard.current;
            MoveInput = keys == null ? Vector2.zero : new Vector2(
                (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
            AnyMovementPressed = keys != null && (keys.wKey.isPressed || keys.aKey.isPressed ||
                keys.sKey.isPressed || keys.dKey.isPressed);
            float pickupSeconds = game.Feedback != null ? game.Feedback.Settings.pickupSeconds : .22f;
            float stretch = game.Feedback != null ? game.Feedback.Settings.fishStretch : .24f;
            pulse = Mathf.MoveTowards(pulse, 0, Time.deltaTime / Mathf.Max(.05f, pickupSeconds));
            bool visible = IsAkiInvulnerable || Time.time >= InvulnerableUntil || Mathf.FloorToInt(Time.time * 16) % 2 == 0;
            visual.gameObject.SetActive(visible);
            visual.localScale = Vector3.Scale(visualScale, new Vector3(1 - pulse * stretch * .5f, 1 + pulse * stretch, 1));
            visual.localRotation = Quaternion.Euler(0, 0, -MoveInput.x * 14);
            if (tail != null) tail.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 16) * 24, 0);
        }

        void FixedUpdate()
        {
            if (game.State != GameState.Playing) { StopMotion(); return; }
            var settings = game.settings;
            var position = Body.position;
            float halfWidth = settings.waterfallWidth * .5f - .65f;
            position.x = Mathf.Clamp(position.x, -halfWidth, halfWidth);
            if (position.y <= 0) position.y = 0;
            position.z = 0;
            Body.position = position;
            internalVerticalSpeed = settings.StepStoredVelocity(internalVerticalSpeed,
                MoveInput.y, Time.fixedDeltaTime, position.y <= 0);
            float vy = settings.ClampVelocity(internalVerticalSpeed);
            float vx = MoveInput.x * settings.horizontalSpeed;
            vx = Mathf.Clamp(vx, (-halfWidth - position.x) / Time.fixedDeltaTime,
                (halfWidth - position.x) / Time.fixedDeltaTime);
            Body.velocity = new Vector3(vx, vy, 0);
            game.Combo.UpdateSpeed(vy);
            CheckPath(Body.velocity * Time.fixedDeltaTime);
        }

        void CheckPath(Vector3 movement)
        {
            Vector3 lower = Body.position + Vector3.down * .32f;
            Vector3 upper = Body.position + Vector3.up * .32f;
            int count = Physics.OverlapCapsuleNonAlloc(lower, upper, .44f, overlaps, FlowMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++) Interact(overlaps[i]);
            if (movement.sqrMagnitude < .00001f) return;
            count = Physics.CapsuleCastNonAlloc(lower, upper, .44f, movement.normalized,
                sweepHits, movement.magnitude, FlowMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++) Interact(sweepHits[i].collider);
        }

        void OnTriggerEnter(Collider other) => Interact(other);
        void Interact(Collider other)
        {
            if (other != null && other.TryGetComponent<FlowObject>(out var flow)) flow.Interact(this);
        }

        public void ApplyPickup(float upwardImpulse)
        {
            SetVerticalSpeed(game.settings.PickupVelocity(internalVerticalSpeed, upwardImpulse));
            pulse = 1;
        }

        public void Boost(float amount)
        {
            SetVerticalSpeed(internalVerticalSpeed + amount);
            pulse = 1;
        }

        public bool Hit()
        {
            if (game.State != GameState.Playing || protection.IsActive(Time.time)) return false;
            SetVerticalSpeed(-game.settings.hitFallSpeed);
            protection.GrantHit(Time.time, game.settings.invulnerabilitySeconds);
            return true;
        }

        public void ActivateAkiBoost()
        {
            if (game.State != GameState.Playing) return;
            Boost(game.settings.setBonus);
            protection.GrantBoost(Time.time, game.settings.akiInvulnerabilitySeconds);
        }

        public void ResetPlayer()
        {
            Body.position = Vector3.zero;
            StopMotion();
            protection.Reset();
            pulse = 0;
            visual.gameObject.SetActive(true);
        }

        public void SetVerticalSpeed(float value)
        {
            internalVerticalSpeed = game.settings.ClampStoredVelocity(value);
            Body.velocity = new Vector3(Body.velocity.x, game.settings.ClampVelocity(internalVerticalSpeed), 0);
            if (game.State == GameState.Playing) game.Combo.UpdateSpeed(Body.velocity.y);
        }

        public void StopMotion()
        {
            internalVerticalSpeed = 0;
            Body.velocity = Vector3.zero;
            if (game.State == GameState.Playing) game.Combo.Reset();
        }
    }

    public sealed class InvulnerabilityState
    {
        public float Until { get; private set; }
        public float BoostUntil { get; private set; }
        public bool IsActive(float now) => now < Until;
        public bool IsBoostActive(float now) => now < BoostUntil;

        public void GrantHit(float now, float seconds)
        {
            Until = Mathf.Max(Until, now + Mathf.Max(0, seconds));
        }

        public void GrantBoost(float now, float seconds)
        {
            BoostUntil = Mathf.Max(BoostUntil, now + Mathf.Max(0, seconds));
            Until = Mathf.Max(Until, BoostUntil);
        }

        public void Reset() { Until = 0; BoostUntil = 0; }
    }
}
