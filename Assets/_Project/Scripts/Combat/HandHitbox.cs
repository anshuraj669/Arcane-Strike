using System.Collections.Generic;
using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Combat
{
    /// <summary>
    /// A physical trigger that follows one palm, so the player can hit enemies by touching them.
    /// Damage scales with how fast the hand was moving, so a real punch hurts and a slow touch doesn't.
    /// Put one on an empty GameObject per hand (set <see cref="side"/>).
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class HandHitbox : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] HandMotion motion;
        [SerializeField] HandSide side = HandSide.Right;

        [Header("Hitbox")]
        [SerializeField] float radius = 0.06f;

        [Header("Damage")]
        [Tooltip("Hand speed (m/s) needed for contact to count as a hit.")]
        [SerializeField] float minHitSpeed = 1.0f;
        [SerializeField] float damagePerMetrePerSecond = 10f;
        [SerializeField] float maxDamage = 40f;
        [Tooltip("The same target can't be hit again by this hand within this time.")]
        [SerializeField] float sameTargetCooldown = 0.4f;

        Rigidbody body;
        SphereCollider sphere;
        readonly Dictionary<IHittable, float> lastHit = new Dictionary<IHittable, float>();

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (motion == null) motion = FindAnyObjectByType<HandMotion>();

            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            sphere = GetComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = radius;
        }

        void FixedUpdate()
        {
            if (input == null) return;
            HandFrame f = input.Get(side);
            sphere.enabled = f.IsTracked;
            if (f.IsTracked) body.MovePosition(f.PalmCenter);
        }

        void OnTriggerEnter(Collider other)
        {
            var target = other.GetComponentInParent<IHittable>();
            if (target == null || motion == null || !PowerUnlocks.Allows(Power.Strikes)) return;

            float speed = motion.Speed(side);
            if (speed < minHitSpeed) return;
            if (lastHit.TryGetValue(target, out float t) && Time.time - t < sameTargetCooldown) return;
            lastHit[target] = Time.time;

            target.TakeHit(new HitInfo
            {
                Damage = Mathf.Min(maxDamage, speed * damagePerMetrePerSecond),
                Point = other.ClosestPoint(transform.position),
                Direction = motion.Velocity(side).normalized,
                Speed = speed,
                Hand = side,
            });
        }
    }
}
