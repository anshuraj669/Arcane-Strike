using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Combat
{
    public struct HitInfo
    {
        public float Damage;
        public Vector3 Point;
        public Vector3 Direction;
        public float Speed;
        public HandSide Hand;
    }

    /// <summary>Anything the player's hands can hit: enemies, Shades, training dummies, breakable props.</summary>
    public interface IHittable
    {
        void TakeHit(HitInfo hit);
    }
}
