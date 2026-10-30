using System.Collections.Generic;
using UnityEngine;

namespace ArcaneStrike.Combat
{
    /// <summary>What kind of technique a move is (how <see cref="KalakhWarrior"/> carries it out).</summary>
    public enum MoveKind { Melee, Leap, Charge, Projectile, Eruption, Breath, Roar, Grab, Spin, ShadowStep, Feint, Retreat, Guard, Berserk }

    /// <summary>Where a blow lands, so how to get out of it: High (duck), Mid (parry or lean aside), Low (jump or step back).</summary>
    public enum Reach { High, Mid, Low }

    public enum HitShape { Cone, Line, Circle }

    public enum HitEffect { None, Knockback, Stun, Burn, Slow, Blind }

    public enum MissileKind { Fire, Rock, Bone, Shadow, Venom }

    /// <summary>One blow: the wind-up pose (the tell), the strike pose, its timing and what it hits.</summary>
    public class Strike
    {
        public KalakhPose wind, hit;
        public float windTime = 0.6f, hitTime = 0.18f, hold = 0.15f;
        public float lunge;                   // metres forward during the strike
        public float damage = 10f;
        public HitShape shape = HitShape.Cone;
        public float range = 1.7f, angle = 80f; // (cone: degrees; line: width in metres; circle: radius)
        public Reach reach = Reach.Mid;
        public bool unblockable;              // glows red: parrying won't stop it
        public HitEffect effect;
        public bool fromTail;
        public bool heavy;                    // a big blow (a deeper growl, dust, a rumble)
    }

    /// <summary>A Kalakh fighting technique.</summary>
    public class KalakhMove
    {
        public string name;
        public MoveKind kind;
        public int tier = 1;                  // the first monster knows tier 1, the boss all five
        public float minRange, maxRange = 2.2f;
        public float cooldown = 4f, weight = 1f;
        public readonly List<Strike> strikes = new List<Strike>();
        public float recover = 0.7f;
        // Specials
        public int count = 1;                 // missiles, eruptions, spins, charges
        public float speed = 8f, radius = 1.8f, delay = 1f, duration = 1.5f;
        public MissileKind missile;
        public KalakhMove then;               // a follow-up (feints, ambushes, combos into another move)
        public string breed;                  // a signature technique: only this kind of Kalakh (and the Warlord) knows it

        public KalakhMove Add(Strike s)
        {
            strikes.Add(s);
            return this;
        }
    }

    /// <summary>The Kalakh's body language and the fifty-odd techniques built from it.</summary>
    public static class KalakhMoves
    {
        /// <summary>
        /// Deals the techniques out to `count` soldiers so that no two share an attack, freshly shuffled every fight.
        /// Each gets at least one blow it can land up close (or it could never hurt anyone), then the rest go round;
        /// the easier ones tend to land in the earlier hands. The Warlord's own, its rages, tail blows (soldiers have no
        /// tail to speak of) and moves that only exist as another's follow-up are left out.
        /// </summary>
        public static List<KalakhMove>[] DealDecks(int count)
        {
            var followUps = new HashSet<KalakhMove>();
            foreach (KalakhMove m in All)
                if (m.then != null) followUps.Add(m.then);
            var close = new List<KalakhMove>();
            var rest = new List<KalakhMove>();
            foreach (KalakhMove m in All)
            {
                if (followUps.Contains(m) || m.breed == "Warlord" || m.kind == MoveKind.Berserk || m.kind == MoveKind.Guard) continue;
                if (m.kind == MoveKind.Retreat && m.then == null) continue;
                if (m.strikes.Exists(s => s.fromTail)) continue;
                bool landsClose = (m.kind == MoveKind.Melee || m.kind == MoveKind.Spin || m.kind == MoveKind.Grab) && m.minRange <= 0.5f && m.maxRange >= 1.7f;
                (landsClose ? close : rest).Add(m);
            }
            Roughly(close);
            Roughly(rest);
            var decks = new List<KalakhMove>[count];
            for (int i = 0; i < count; i++) decks[i] = new List<KalakhMove>();
            // Round and round, so the close blows spread evenly. (More soldiers than close blows: the last ones borrow
            // from a follow-up, which is the only time two can overlap, and only then.)
            for (int i = 0; i < close.Count; i++) decks[i % count].Add(close[i]);
            for (int i = 0; i < rest.Count; i++) decks[i % count].Add(rest[i]);
            var spare = new List<KalakhMove>(followUps);
            spare.RemoveAll(m => m.kind != MoveKind.Melee || m.minRange > 0.5f || m.breed == "Warlord");
            foreach (List<KalakhMove> deck in decks)
                if (!deck.Exists(m => m.kind == MoveKind.Melee || m.kind == MoveKind.Spin || m.kind == MoveKind.Grab) && spare.Count > 0)
                {
                    deck.Add(spare[spare.Count - 1]);
                    spare.RemoveAt(spare.Count - 1);
                }
            return decks;
        }

        // Shuffled, but with a lean towards the lower tiers first.
        static void Roughly(List<KalakhMove> moves)
        {
            var keyed = new List<(float key, KalakhMove move)>();
            foreach (KalakhMove m in moves) keyed.Add((m.tier + Random.Range(0f, 2.5f), m));
            keyed.Sort((a, b) => a.key.CompareTo(b.key));
            moves.Clear();
            foreach ((float _, KalakhMove m) in keyed) moves.Add(m);
        }

        // ---------------------------------------------------------------- poses (right-handed; Mirrored() for left)

        public static KalakhPose ClawUp() => KalakhBody.Idle().Arm(true, -150f, 30f, -55f).Arm(false, -25f, 12f, -25f).Body(-10f, -8f, 25f).Look(0f, -12f).Tail(10f, 10f);
        public static KalakhPose ClawDown() => KalakhBody.Idle().Arm(true, -40f, -35f, -10f).Arm(false, -10f, 18f, -18f).Body(25f, 15f, -35f).Look(5f, 5f, -10f).Leg(true, -30f, 38f).Leg(false, 5f, 15f).Tail(-5f, -15f);
        public static KalakhPose RakeWind() => KalakhBody.Idle().Arm(true, -80f, 80f, -20f).Arm(false, -30f, 20f, -30f).Body(5f, 5f, 40f).Tail(0f, 20f);
        public static KalakhPose RakeHit() => KalakhBody.Idle().Arm(true, -85f, -40f, -10f).Arm(false, -20f, 30f, -20f).Body(10f, 10f, -45f).Tail(0f, -25f);
        public static KalakhPose LowRakeWind() => RakeWind().Drop(0.16f).Legs(-45f, 75f, -20f).Body(35f, 20f, 40f).Arm(true, -40f, 70f, -10f);
        public static KalakhPose LowRakeHit() => RakeHit().Drop(0.16f).Legs(-45f, 75f, -20f).Body(40f, 25f, -45f).Arm(true, -35f, -45f, -5f);
        public static KalakhPose UpperWind() => KalakhBody.Idle().Drop(0.08f).Arm(true, 25f, 15f, -80f).Body(25f, 15f, 20f).Legs(-30f, 45f);
        public static KalakhPose UpperHit() => KalakhBody.Idle().Arm(true, -170f, 5f, -10f).Body(-20f, -15f, -15f).Look(-20f, -10f).Legs(-5f, 10f);
        public static KalakhPose BothUp() => KalakhBody.Idle().Arms(-165f, 15f, -40f).Body(-12f, -10f).Look(-15f, -15f).Tail(20f, 0f);
        public static KalakhPose BothSlam() => KalakhBody.Idle().Arms(-50f, 5f, -5f).Body(40f, 25f).Look(15f, 10f).Drop(0.08f, 8f).Tail(-10f, 0f);
        public static KalakhPose ClapWind() => KalakhBody.Idle().Arms(-70f, 85f, -10f).Body(-5f, -5f).Look(-10f, -10f);
        public static KalakhPose ClapHit() => KalakhBody.Idle().Arms(-75f, -12f, -5f).Body(15f, 10f);
        public static KalakhPose LungeWind() => KalakhBody.Idle().Drop(0.1f).Arms(-30f, 20f, -40f).Body(30f, 15f).Legs(-35f, 55f, -10f).Look(-20f, -20f);
        public static KalakhPose LungeHit() => KalakhBody.Idle().Arm(true, -95f, 0f, -5f).Arm(false, -80f, 0f, -10f).Body(40f, 20f).Look(-15f, -15f).Leg(true, -55f, 30f).Leg(false, 20f, 10f).Tail(15f, 0f);
        public static KalakhPose BiteWind() => KalakhBody.Idle().Body(-15f, -10f).Look(-25f, -20f).Arms(-20f, 25f, -30f);
        public static KalakhPose BiteHit() => KalakhBody.Idle().Body(35f, 25f).Look(35f, 15f).Arms(-50f, 20f, -20f);
        public static KalakhPose Rear() => KalakhBody.Idle().Body(-25f, -20f).Look(-20f, -30f).Arms(-40f, 75f, -35f).Tail(25f, 0f);
        public static KalakhPose GuardCross() => KalakhBody.Idle().Arms(-85f, -35f, -95f).Body(5f, 0f).Look(15f, 10f).Drop(0.05f);
        public static KalakhPose TailStabWind() => KalakhBody.Idle().TailEach(70f, 10f, 10f, 0f, 0f).Body(15f, 10f).Drop(0.05f).Arms(-25f, 30f, -30f);
        public static KalakhPose TailStabHit() => KalakhBody.Idle().TailEach(105f, 25f, 20f, 10f, 10f).Body(30f, 15f).Arms(-25f, 30f, -30f);
        public static KalakhPose ThrowWind() => KalakhBody.Idle().Arm(true, -160f, 20f, -90f).Arm(false, -60f, 20f, -20f).Body(-10f, -10f, 40f);
        public static KalakhPose ThrowHit() => KalakhBody.Idle().Arm(true, -60f, -20f, -10f).Arm(false, -30f, 20f, -20f).Body(25f, 15f, -35f);
        public static KalakhPose StompWind() => KalakhBody.Idle().Leg(true, -75f, 95f, -20f).Body(-5f, -5f).Arms(-40f, 45f, -30f);
        public static KalakhPose StompHit() => KalakhBody.Idle().Leg(true, -15f, 15f, 5f).Body(25f, 15f).Drop(0.06f).Arms(-30f, 30f, -20f);
        public static KalakhPose GrabWind() => KalakhBody.Idle().Arms(-60f, 45f, -20f).Body(10f, 5f).Drop(0.05f);
        public static KalakhPose GrabHit() => KalakhBody.Idle().Arms(-90f, 8f, -35f).Body(35f, 20f).Look(-10f, -10f);
        public static KalakhPose SpinCoil() => KalakhBody.Idle().Body(10f, 10f, 70f).Arms(-80f, 70f, -10f).Tail(0f, 60f);
        public static KalakhPose SpinOut() => KalakhBody.Idle().Body(10f, 10f, -20f).Arms(-85f, 85f, -5f).Tail(10f, -20f).Drop(0.06f);
        public static KalakhPose BreathWind() => KalakhBody.Idle().Body(-20f, -15f).Look(-30f, -25f).Arms(-40f, 40f, -40f);
        public static KalakhPose BreathHit() => KalakhBody.Idle().Body(20f, 15f).Look(20f, 10f).Arms(-30f, 30f, -20f);
        public static KalakhPose Crouch() => KalakhBody.Idle().Drop(0.18f).Legs(-45f, 75f, -25f).Body(30f, 15f).Arms(-20f, 15f, -30f);
        public static KalakhPose Air() => KalakhBody.Idle().Legs(-70f, 100f, -30f).Arms(-130f, 30f, -30f).Body(-10f, -10f).Tail(-30f, 0f);
        public static KalakhPose Land() => KalakhBody.Idle().Drop(0.15f).Legs(-40f, 70f).Arms(-70f, 30f, -10f).Body(40f, 20f);
        public static KalakhPose Stagger() => KalakhBody.Idle().Body(-25f, -15f).Look(-20f, -15f).Arms(-30f, 55f, -20f).Drop(0f, -8f);
        public static KalakhPose Taunt() => KalakhBody.Idle().Arms(-60f, 85f, -60f).Body(-20f, -15f).Look(-25f, -20f).Tail(30f, 0f);
        public static KalakhPose Charging() => KalakhBody.Idle().Body(35f, 20f).Look(25f, -10f).Arms(10f, 15f, -40f).Drop(0.08f);
        public static KalakhPose ShieldRaise() => KalakhBody.Idle().Arm(false, -95f, -50f, -100f).Arm(true, -40f, 40f, -30f).Body(5f, 0f, -25f).Look(10f, 5f).Drop(0.05f);
        public static KalakhPose ShieldBash() => KalakhBody.Idle().Arm(false, -90f, 5f, -15f).Arm(true, -30f, 30f, -20f).Body(30f, 15f, 20f).Leg(false, -45f, 25f).Leg(true, 15f, 10f);
        public static KalakhPose HeadbuttWind() => KalakhBody.Idle().Body(-30f, -20f).Look(-35f, -10f).Arms(-35f, 60f, -30f).Tail(15f, 0f);
        public static KalakhPose HeadbuttHit() => KalakhBody.Idle().Body(45f, 25f).Look(40f, 20f).Arms(-20f, 20f, -10f).Leg(true, -40f, 25f);
        public static KalakhPose KickWind() => KalakhBody.Idle().Leg(true, -95f, 110f, -10f).Body(-15f, -10f, 15f).Arms(-45f, 50f, -30f).Tail(20f, 0f);
        public static KalakhPose KickHit() => KalakhBody.Idle().Leg(true, -70f, 5f, 20f).Body(-20f, -10f, -10f).Arms(-35f, 40f, -30f).Tail(10f, 10f);
        public static KalakhPose ScissorHit() => KalakhBody.Idle().Arms(-85f, -55f, -5f).Body(20f, 15f).Look(10f, 5f);
        public static KalakhPose ReapWind() => KalakhBody.Idle().Drop(0.12f).Arms(-35f, 95f, -10f).Body(25f, 15f, 55f).Legs(-40f, 65f, -15f).Tail(0f, 40f);
        public static KalakhPose ReapHit() => KalakhBody.Idle().Drop(0.14f).Arms(-35f, -60f, -5f).Body(35f, 20f, -60f).Legs(-40f, 65f, -15f).Tail(0f, -40f);
        public static KalakhPose ElbowWind() => KalakhBody.Idle().Arm(true, -170f, 40f, -140f).Body(-20f, -10f, 20f).Look(-10f, -10f);
        public static KalakhPose ElbowHit() => KalakhBody.Idle().Arm(true, -60f, 10f, -140f).Body(45f, 25f, -20f).Drop(0.1f, 6f).Look(20f, 10f);
        public static KalakhPose GoreWind() => KalakhBody.Idle().Drop(0.12f).Body(50f, 30f).Look(45f, 25f).Arms(-15f, 40f, -40f).Legs(-45f, 70f, -20f);
        public static KalakhPose GoreHit() => KalakhBody.Idle().Body(10f, 0f).Look(-35f, -25f).Arms(-30f, 30f, -30f).Leg(true, -50f, 30f).Leg(false, 15f, 10f);
        public static KalakhPose CleaverWind() => KalakhBody.Idle().Arm(true, -175f, -10f, -40f).Arm(false, -175f, 10f, -40f).Body(-25f, -15f, 15f).Look(-20f, -20f).Tail(25f, 0f);
        public static KalakhPose CleaverHit() => KalakhBody.Idle().Arm(true, -25f, 10f, -5f).Arm(false, -30f, -10f, -5f).Body(50f, 30f, -10f).Drop(0.1f, 10f).Look(25f, 15f).Leg(true, -50f, 45f);
        public static KalakhPose Dead() => KalakhBody.Idle().Drop(0.5f, 70f).Body(40f, 30f).Look(30f, 20f).Arms(20f, 30f, 20f).Legs(-90f, 130f, -30f).Tail(-20f, 0f);

        static Strike Blow(KalakhPose wind, KalakhPose hit, float damage, Reach reach, float range = 1.7f, float angle = 80f,
                           float windTime = 0.6f, float hitTime = 0.18f, float lunge = 0.3f, HitShape shape = HitShape.Cone,
                           HitEffect effect = HitEffect.None, bool unblockable = false, bool heavy = false, bool tail = false) =>
            new Strike
            {
                wind = wind, hit = hit, damage = damage, reach = reach, range = range, angle = angle, windTime = windTime, hitTime = hitTime,
                lunge = lunge, shape = shape, effect = effect, unblockable = unblockable, heavy = heavy, fromTail = tail,
            };

        static Strike Rend(bool right, float damage = 10f, float wind = 0.6f) =>
            Blow(right ? ClawUp() : ClawUp().Mirrored(), right ? ClawDown() : ClawDown().Mirrored(), damage, Reach.Mid, windTime: wind);
        static Strike Rake(bool right, float damage = 11f, float wind = 0.55f) =>
            Blow(right ? RakeWind() : RakeWind().Mirrored(), right ? RakeHit() : RakeHit().Mirrored(), damage, Reach.High, 1.8f, 130f, wind);
        static Strike Upper(bool right, float damage = 14f) =>
            Blow(right ? UpperWind() : UpperWind().Mirrored(), right ? UpperHit() : UpperHit().Mirrored(), damage, Reach.Mid, 1.6f, 60f, 0.65f, 0.16f, 0.4f, effect: HitEffect.Knockback);
        static Strike Bite(float lunge = 0.6f, float damage = 13f, HitEffect effect = HitEffect.None) =>
            Blow(BiteWind(), BiteHit(), damage, Reach.High, 1.4f + lunge * 0.5f, 50f, 0.5f, 0.14f, lunge, effect: effect);
        static Strike Sweep(bool right, float damage = 9f) =>
            Blow(right ? LowRakeWind() : LowRakeWind().Mirrored(), right ? LowRakeHit() : LowRakeHit().Mirrored(), damage, Reach.Low, 2f, 150f, 0.55f);
        static Strike Stomp(float damage = 12f) =>
            Blow(StompWind(), StompHit(), damage, Reach.Low, 2.4f, 0f, 0.7f, 0.15f, 0f, HitShape.Circle, HitEffect.Stun, heavy: true);
        static Strike Slam(float damage, bool unblockable, float wind = 0.9f) =>
            Blow(BothUp(), BothSlam(), damage, Reach.Mid, 1.9f, 70f, wind, 0.2f, 0.5f, effect: HitEffect.Knockback, unblockable: unblockable, heavy: true);
        static Strike Lunge(float lunge = 1.6f, float damage = 14f) =>
            Blow(LungeWind(), LungeHit(), damage, Reach.Mid, 1.6f + lunge * 0.6f, 50f, 0.55f, 0.22f, lunge);
        static Strike TailStab(float damage = 16f) =>
            Blow(TailStabWind(), TailStabHit(), damage, Reach.High, 2.6f, 0.7f, 0.75f, 0.16f, 0.2f, HitShape.Line, tail: true);

        static KalakhMove M(string name, MoveKind kind, int tier, float min, float max, float cooldown, params Strike[] strikes)
        {
            var m = new KalakhMove { name = name, kind = kind, tier = tier, minRange = min, maxRange = max, cooldown = cooldown };
            foreach (Strike s in strikes) m.Add(s);
            return m;
        }

        static List<KalakhMove> all;

        /// <summary>Every technique, built once.</summary>
        public static List<KalakhMove> All => all ??= Build();

        static List<KalakhMove> Build()
        {
            var list = new List<KalakhMove>();
            KalakhMove Add(KalakhMove m)
            {
                list.Add(m);
                return m;
            }

            // ---- single blows
            var rendingClaw = Add(M("Rending Claw", MoveKind.Melee, 1, 0f, 2f, 2.5f, Rend(true)));
            Add(M("Sinister Rend", MoveKind.Melee, 1, 0f, 2f, 2.5f, Rend(false)));
            var rakingSweep = Add(M("Raking Sweep", MoveKind.Melee, 1, 0f, 2.1f, 3f, Rake(true)));
            Add(M("Backhand Rake", MoveKind.Melee, 1, 0f, 2.1f, 3f, Rake(false)));
            Add(M("Gutting Uppercut", MoveKind.Melee, 2, 0f, 1.9f, 4f, Upper(true)));
            Add(M("Hammerfall", MoveKind.Melee, 2, 0f, 2.2f, 5f, Slam(18f, false)));
            Add(M("Thunder Clap", MoveKind.Melee, 3, 0f, 1.8f, 6f, Blow(ClapWind(), ClapHit(), 15f, Reach.High, 1.6f, 70f, 0.7f, 0.12f, 0.3f, effect: HitEffect.Stun)));
            var savageBite = Add(M("Savage Bite", MoveKind.Melee, 1, 0f, 2f, 3.5f, Bite()));
            Add(M("Horn Ram", MoveKind.Melee, 2, 0.8f, 2.8f, 5f, Bite(1.2f, 12f, HitEffect.Knockback)));
            Add(M("Leg Sweep", MoveKind.Melee, 1, 0f, 2.2f, 4f, Sweep(true)));
            var quakeStomp = Add(M("Quake Stomp", MoveKind.Melee, 2, 0f, 2.3f, 6f, Stomp()));
            Add(M("Tail Impaler", MoveKind.Melee, 3, 1f, 2.8f, 5f, TailStab()));
            Add(M("Executioner Slam", MoveKind.Melee, 4, 0f, 2.2f, 9f, Slam(26f, true, 1.3f)));
            Add(M("Skewering Lunge", MoveKind.Melee, 1, 1.4f, 3.6f, 4f, Lunge()));
            Add(M("Pinning Strike", MoveKind.Melee, 2, 0.5f, 2.6f, 5f, Blow(ClawUp(), ClawDown(), 12f, Reach.Mid, 1.9f, 60f, 0.6f, 0.2f, 0.8f, effect: HitEffect.Slow)));
            Add(M("Skull Crusher", MoveKind.Melee, 5, 0f, 2.2f, 8f, Blow(ClawUp().Arm(true, -175f, 10f, -70f), BothSlam(), 24f, Reach.Mid, 1.9f, 60f, 1f, 0.16f, 0.5f, unblockable: true, heavy: true)));

            // ---- combinations
            Add(M("Twin Rake", MoveKind.Melee, 1, 0f, 2f, 4f, Rend(true, 9f, 0.55f), Rend(false, 9f, 0.3f)));
            Add(M("Triple Rend", MoveKind.Melee, 2, 0f, 2f, 5f, Rend(true, 8f, 0.5f), Rend(false, 8f, 0.28f), Rend(true, 10f, 0.28f)));
            Add(M("Hook and Slash", MoveKind.Melee, 3, 0f, 2.1f, 5f, Rake(true, 10f), Upper(false, 12f)));
            Add(M("Claw Flurry", MoveKind.Melee, 3, 0f, 1.9f, 6f, Rend(true, 6f, 0.45f), Rend(false, 6f, 0.22f), Rend(true, 6f, 0.22f), Rend(false, 6f, 0.22f), Rend(true, 8f, 0.25f)));
            Add(M("Frenzied Assault", MoveKind.Melee, 4, 0f, 2f, 7f, Rake(true, 9f, 0.45f), Rake(false, 9f, 0.3f), Bite(0.4f, 10f), Slam(16f, false, 0.6f)));
            Add(M("Rake and Stomp", MoveKind.Melee, 3, 0f, 2.1f, 6f, Rake(true, 10f), Stomp(11f)));
            Add(M("Bite and Toss", MoveKind.Melee, 4, 0f, 2f, 6f, Bite(0.5f, 10f), Upper(true, 14f)));
            Add(M("Double Lunge", MoveKind.Melee, 4, 1.4f, 4f, 6f, Lunge(1.4f, 12f), Lunge(1.6f, 14f)));
            Add(M("Low-High Mixup", MoveKind.Melee, 4, 0f, 2.1f, 6f, Sweep(true, 9f), Rake(false, 11f, 0.35f)));
            Add(M("Sweep and Rend", MoveKind.Melee, 2, 0f, 2.1f, 5f, Sweep(false, 8f), Rend(true, 10f, 0.4f)));

            // ---- leaps, charges, footwork
            var pounce = Add(new KalakhMove { name = "Ash Pounce", kind = MoveKind.Leap, tier = 2, minRange = 3f, maxRange = 9f, cooldown = 6f, radius = 1.8f, speed = 2.2f });
            pounce.Add(Blow(Crouch(), Land(), 16f, Reach.Mid, 1.8f, 0f, 0.6f, 0.2f, 0f, HitShape.Circle, heavy: true));
            Add(new KalakhMove { name = "Meteor Dive", kind = MoveKind.Leap, tier = 4, minRange = 4f, maxRange = 12f, cooldown = 9f, radius = 2.8f, speed = 4f })
                .Add(Blow(Crouch(), Land(), 22f, Reach.Low, 2.8f, 0f, 0.8f, 0.2f, 0f, HitShape.Circle, HitEffect.Burn, heavy: true));
            Add(new KalakhMove { name = "Stampede Charge", kind = MoveKind.Charge, tier = 3, minRange = 3.5f, maxRange = 12f, cooldown = 8f, speed = 7.5f })
                .Add(Blow(Charging(), Charging(), 18f, Reach.Mid, 0.9f, 0f, 0.8f, 0.2f, 0f, HitShape.Line, HitEffect.Knockback, heavy: true));
            Add(new KalakhMove { name = "Horn Charge", kind = MoveKind.Charge, tier = 4, minRange = 3.5f, maxRange = 12f, cooldown = 9f, speed = 8.5f, then = M("Charge Uppercut", MoveKind.Melee, 4, 0f, 3f, 0f, Upper(true, 12f)) })
                .Add(Blow(Charging(), BiteHit(), 15f, Reach.Mid, 0.9f, 0f, 0.7f, 0.2f, 0f, HitShape.Line, HitEffect.Knockback, heavy: true));
            Add(new KalakhMove { name = "Shadow Step Ambush", kind = MoveKind.ShadowStep, tier = 3, minRange = 0f, maxRange = 8f, cooldown = 9f, then = rendingClaw });
            Add(new KalakhMove { name = "Phantom Feint", kind = MoveKind.Feint, tier = 2, minRange = 0f, maxRange = 2f, cooldown = 6f, delay = 0.5f, then = rakingSweep })
                .Add(Blow(ClawUp(), ClawUp(), 0f, Reach.Mid, windTime: 0.55f));
            Add(new KalakhMove { name = "False Retreat", kind = MoveKind.Retreat, tier = 4, minRange = 0f, maxRange = 2.5f, cooldown = 9f, radius = 3.5f, then = pounce });
            Add(new KalakhMove { name = "Backstep", kind = MoveKind.Retreat, tier = 1, minRange = 0f, maxRange = 1.3f, cooldown = 5f, radius = 2.2f, weight = 0.6f });
            Add(new KalakhMove { name = "Sidestep Counter", kind = MoveKind.Retreat, tier = 2, minRange = 0f, maxRange = 2f, cooldown = 6f, radius = -1.6f, then = savageBite });

            // ---- at range
            Add(new KalakhMove { name = "Lava Spit", kind = MoveKind.Projectile, tier = 1, minRange = 3f, maxRange = 12f, cooldown = 5f, missile = MissileKind.Fire, count = 1, speed = 9f })
                .Add(Blow(BreathWind(), BreathHit(), 10f, Reach.Mid, windTime: 0.6f, effect: HitEffect.Burn));
            Add(new KalakhMove { name = "Ember Volley", kind = MoveKind.Projectile, tier = 2, minRange = 3f, maxRange = 12f, cooldown = 7f, missile = MissileKind.Fire, count = 3, speed = 8f })
                .Add(Blow(BreathWind(), BreathHit(), 8f, Reach.Mid, windTime: 0.7f, effect: HitEffect.Burn));
            Add(new KalakhMove { name = "Magma Barrage", kind = MoveKind.Projectile, tier = 4, minRange = 4f, maxRange = 14f, cooldown = 10f, missile = MissileKind.Fire, count = 6, speed = 7f, duration = 1.8f })
                .Add(Blow(Rear(), BreathHit(), 9f, Reach.Mid, windTime: 0.8f, effect: HitEffect.Burn));
            Add(new KalakhMove { name = "Bone Javelin", kind = MoveKind.Projectile, tier = 3, minRange = 3f, maxRange = 14f, cooldown = 7f, missile = MissileKind.Bone, count = 1, speed = 15f })
                .Add(Blow(ThrowWind(), ThrowHit(), 15f, Reach.Mid, windTime: 0.75f));
            Add(new KalakhMove { name = "Boulder Hurl", kind = MoveKind.Projectile, tier = 4, minRange = 4f, maxRange = 14f, cooldown = 9f, missile = MissileKind.Rock, count = 1, speed = 9f })
                .Add(Blow(BothUp(), ThrowHit(), 18f, Reach.Mid, windTime: 0.9f, effect: HitEffect.Knockback, heavy: true));
            Add(new KalakhMove { name = "Shadow Bolts", kind = MoveKind.Projectile, tier = 4, minRange = 3f, maxRange = 14f, cooldown = 8f, missile = MissileKind.Shadow, count = 3, speed = 5f, duration = 1.2f })
                .Add(Blow(Taunt(), ClapHit(), 8f, Reach.Mid, windTime: 0.7f));
            Add(new KalakhMove { name = "Venom Spit", kind = MoveKind.Projectile, tier = 4, minRange = 3f, maxRange = 12f, cooldown = 8f, missile = MissileKind.Venom, count = 1, speed = 10f })
                .Add(Blow(BiteWind(), BiteHit(), 7f, Reach.Mid, windTime: 0.5f, effect: HitEffect.Slow));

            // ---- the ground and the air around the player
            Add(new KalakhMove { name = "Magma Geyser", kind = MoveKind.Eruption, tier = 3, minRange = 2.5f, maxRange = 14f, cooldown = 8f, count = 1, radius = 1.5f, delay = 1.1f })
                .Add(Blow(Rear(), BothSlam(), 16f, Reach.Low, windTime: 0.6f, effect: HitEffect.Burn, heavy: true));
            Add(new KalakhMove { name = "Spike Eruption", kind = MoveKind.Eruption, tier = 4, minRange = 2.5f, maxRange = 12f, cooldown = 9f, count = 4, radius = 1.2f, delay = 0.9f })
                .Add(Blow(BothUp(), BothSlam(), 14f, Reach.Low, windTime: 0.7f, heavy: true));
            Add(new KalakhMove { name = "Inferno Ring", kind = MoveKind.Eruption, tier = 5, minRange = 0f, maxRange = 6f, cooldown = 12f, count = -1, radius = 6f, delay = 0.8f, speed = 6f })
                .Add(Blow(BothUp(), BothSlam(), 15f, Reach.Low, windTime: 0.9f, effect: HitEffect.Burn, heavy: true));
            Add(new KalakhMove { name = "Fire Breath", kind = MoveKind.Breath, tier = 3, minRange = 0f, maxRange = 4.5f, cooldown = 9f, duration = 1.6f, radius = 4.5f })
                .Add(Blow(BreathWind(), BreathHit(), 6f, Reach.Mid, 4.5f, 40f, 0.7f, effect: HitEffect.Burn));
            Add(new KalakhMove { name = "Ash Cloud", kind = MoveKind.Roar, tier = 4, minRange = 0f, maxRange = 6f, cooldown = 12f, radius = 6f })
                .Add(Blow(Rear(), Taunt(), 0f, Reach.Mid, windTime: 0.6f, effect: HitEffect.Blind));
            Add(new KalakhMove { name = "Howl of Ruin", kind = MoveKind.Roar, tier = 2, minRange = 0f, maxRange = 4f, cooldown = 10f, radius = 4f })
                .Add(Blow(Rear(), Taunt(), 6f, Reach.Mid, windTime: 0.7f, effect: HitEffect.Knockback));
            Add(new KalakhMove { name = "Berserk Roar", kind = MoveKind.Berserk, tier = 5, minRange = 0f, maxRange = 20f, cooldown = 25f, duration = 10f })
                .Add(Blow(Rear(), Taunt(), 0f, Reach.Mid, windTime: 0.8f));
            Add(new KalakhMove { name = "Iron Guard", kind = MoveKind.Guard, tier = 1, minRange = 0f, maxRange = 2.5f, cooldown = 7f, duration = 2f, weight = 0.7f });
            Add(new KalakhMove { name = "Crushing Grab", kind = MoveKind.Grab, tier = 3, minRange = 0f, maxRange = 1.9f, cooldown = 10f, count = 3 })
                .Add(Blow(GrabWind(), GrabHit(), 6f, Reach.Mid, 1.8f, 60f, 0.65f, 0.2f, 0.5f));
            Add(new KalakhMove { name = "Tail Cyclone", kind = MoveKind.Spin, tier = 2, minRange = 0f, maxRange = 2.5f, cooldown = 6f, count = 1, radius = 2.6f })
                .Add(Blow(SpinCoil(), SpinOut(), 12f, Reach.Low, 2.6f, 0f, 0.6f, 0.45f, 0f, HitShape.Circle, tail: true));
            Add(new KalakhMove { name = "Death Spiral", kind = MoveKind.Spin, tier = 5, minRange = 0f, maxRange = 2.5f, cooldown = 9f, count = 3, radius = 2.4f })
                .Add(Blow(SpinCoil(), SpinOut(), 10f, Reach.Mid, 2.4f, 0f, 0.6f, 0.35f, 0.4f, HitShape.Circle));
            Add(new KalakhMove { name = "Tail Lash", kind = MoveKind.Spin, tier = 1, minRange = 0f, maxRange = 2.4f, cooldown = 5f, count = 1, radius = 2.3f, weight = 0.8f })
                .Add(Blow(SpinCoil(), SpinOut(), 9f, Reach.Low, 2.3f, 0f, 0.5f, 0.4f, 0f, HitShape.Circle, tail: true));
            Add(new KalakhMove { name = "Rock Toss", kind = MoveKind.Projectile, tier = 2, minRange = 3f, maxRange = 12f, cooldown = 6f, missile = MissileKind.Rock, count = 1, speed = 10f })
                .Add(Blow(ThrowWind(), ThrowHit(), 12f, Reach.Mid, windTime: 0.7f));
            Add(new KalakhMove { name = "Twin Geysers", kind = MoveKind.Eruption, tier = 5, minRange = 2f, maxRange = 14f, cooldown = 10f, count = 2, radius = 1.6f, delay = 1f })
                .Add(Blow(BothUp(), BothSlam(), 16f, Reach.Low, windTime: 0.7f, effect: HitEffect.Burn, heavy: true));
            Add(new KalakhMove { name = "Shadow Pounce Combo", kind = MoveKind.ShadowStep, tier = 5, minRange = 0f, maxRange = 10f, cooldown = 11f, then = M("Ambush Flurry", MoveKind.Melee, 5, 0f, 3f, 0f, Rake(true, 9f, 0.4f), Bite(0.4f, 10f), Rend(false, 11f, 0.3f)) });
            Add(new KalakhMove { name = "Quake Leap", kind = MoveKind.Leap, tier = 5, minRange = 3f, maxRange = 10f, cooldown = 10f, radius = 3.2f, speed = 3f, then = quakeStomp })
                .Add(Blow(Crouch(), Land(), 18f, Reach.Low, 3.2f, 0f, 0.7f, 0.2f, 0f, HitShape.Circle, HitEffect.Stun, heavy: true));
            // ---- more blows: kicks, butts, bashes, two-handed cleaves
            var shieldBash = Add(M("Shield Bash", MoveKind.Melee, 1, 0f, 2f, 4f, Blow(ShieldRaise(), ShieldBash(), 11f, Reach.Mid, 1.7f, 70f, 0.5f, 0.14f, 0.6f, effect: HitEffect.Stun)));
            shieldBash.breed = "DreadWarden";
            var headbutt = Add(M("Skull Bash", MoveKind.Melee, 1, 0f, 1.8f, 4f, Blow(HeadbuttWind(), HeadbuttHit(), 12f, Reach.High, 1.5f, 50f, 0.55f, 0.13f, 0.5f, effect: HitEffect.Stun)));
            var kneeBreaker = Add(M("Knee Breaker", MoveKind.Melee, 2, 0f, 2.1f, 4f, Blow(KickWind(), KickHit(), 11f, Reach.Low, 1.9f, 60f, 0.5f, 0.14f, 0.3f, effect: HitEffect.Slow)));
            Add(M("Backhand Swat", MoveKind.Melee, 1, 0f, 2.1f, 3.5f, Blow(RakeWind().Mirrored(), RakeHit().Mirrored(), 10f, Reach.Mid, 1.9f, 120f, 0.45f, effect: HitEffect.Knockback)));
            var scissor = Add(M("Scissor Claws", MoveKind.Melee, 2, 0f, 1.9f, 5f, Blow(ClapWind(), ScissorHit(), 14f, Reach.Mid, 1.7f, 90f, 0.6f, 0.14f, 0.4f)));
            var reap = Add(M("Reaping Sweep", MoveKind.Melee, 3, 0f, 2.4f, 6f, Blow(ReapWind(), ReapHit(), 13f, Reach.Low, 2.4f, 170f, 0.7f, 0.2f, 0.2f, heavy: true)));
            var cleaver = Add(M("Overhead Cleaver", MoveKind.Melee, 2, 0f, 2.2f, 6f, Blow(CleaverWind(), CleaverHit(), 20f, Reach.Mid, 2f, 50f, 0.85f, 0.16f, 0.5f, heavy: true)));
            Add(M("Falling Elbow", MoveKind.Melee, 3, 0f, 1.8f, 6f, Blow(ElbowWind(), ElbowHit(), 17f, Reach.High, 1.6f, 60f, 0.75f, 0.15f, 0.5f, effect: HitEffect.Stun, heavy: true)));
            Add(M("Goring Rush", MoveKind.Melee, 2, 1f, 3.4f, 6f, Blow(GoreWind(), GoreHit(), 15f, Reach.Mid, 2.2f, 50f, 0.65f, 0.2f, 1.4f, effect: HitEffect.Knockback, heavy: true))).breed = "Ravager";
            Add(M("Patient Hammer", MoveKind.Melee, 3, 0f, 2.2f, 8f, Slam(20f, false, 1.5f))); // the long, long tell: wait for it
            Add(M("Kick and Rend", MoveKind.Melee, 2, 0f, 2.1f, 5f, Blow(KickWind(), KickHit(), 8f, Reach.Low, 1.9f, 60f, 0.45f), Rend(true, 10f, 0.3f)));
            Add(M("Bash and Bite", MoveKind.Melee, 3, 0f, 1.9f, 5f, Blow(HeadbuttWind(), HeadbuttHit(), 9f, Reach.High, 1.5f, 50f, 0.5f), Bite(0.4f, 11f)));

            // ---- each kind's own techniques (the Warlord knows them all)
            var jackalRush = Add(M("Jackal Rush", MoveKind.Melee, 2, 1.2f, 4f, 6f, Lunge(1.5f, 10f), Bite(0.4f, 9f), Bite(0.3f, 9f), Rend(true, 10f, 0.25f)));
            jackalRush.breed = "Cinderfang";
            Add(M("Hamstring Snap", MoveKind.Melee, 2, 0f, 2.2f, 5f, Sweep(true, 8f), Bite(0.5f, 12f, HitEffect.Slow))).breed = "Cinderfang";
            Add(new KalakhMove { name = "Cinder Dash", kind = MoveKind.ShadowStep, tier = 2, minRange = 2f, maxRange = 9f, cooldown = 8f, breed = "Cinderfang", then = jackalRush });
            Add(M("Ravager's Wrath", MoveKind.Melee, 3, 0f, 2.2f, 8f, Slam(16f, false, 0.8f), Stomp(10f), Upper(true, 14f))).breed = "Ravager";
            Add(new KalakhMove { name = "Boulder Barrage", kind = MoveKind.Projectile, tier = 3, minRange = 4f, maxRange = 14f, cooldown = 10f, missile = MissileKind.Rock, count = 3, speed = 9f, duration = 1.4f, breed = "Ravager" })
                .Add(Blow(BothUp(), ThrowHit(), 13f, Reach.Mid, windTime: 0.8f, effect: HitEffect.Knockback, heavy: true));
            Add(M("Warden's Judgement", MoveKind.Melee, 4, 0f, 2.2f, 8f, Blow(ShieldRaise(), ShieldBash(), 9f, Reach.Mid, 1.7f, 70f, 0.45f, 0.14f, 0.5f),
                  Blow(CleaverWind(), CleaverHit(), 22f, Reach.Mid, 2f, 50f, 0.8f, 0.16f, 0.4f, unblockable: true, heavy: true))).breed = "DreadWarden";
            Add(new KalakhMove { name = "Iron Spikes", kind = MoveKind.Eruption, tier = 4, minRange = 2f, maxRange = 12f, cooldown = 9f, count = 6, radius = 1.1f, delay = 0.8f, breed = "DreadWarden" })
                .Add(Blow(ShieldRaise(), BothSlam(), 13f, Reach.Low, windTime: 0.7f, heavy: true));
            Add(M("Crown Breaker", MoveKind.Melee, 5, 0f, 2.4f, 9f, Blow(CleaverWind(), CleaverHit(), 26f, Reach.Mid, 2.2f, 60f, 1.1f, 0.18f, 0.6f, effect: HitEffect.Stun, unblockable: true, heavy: true),
                  Blow(ReapWind(), ReapHit(), 14f, Reach.Low, 2.4f, 170f, 0.5f, 0.2f, 0.2f, heavy: true))).breed = "Warlord";
            Add(new KalakhMove { name = "Cinder Rain", kind = MoveKind.Projectile, tier = 5, minRange = 3f, maxRange = 16f, cooldown = 12f, missile = MissileKind.Fire, count = 9, speed = 7f, duration = 2.4f, breed = "Warlord" })
                .Add(Blow(BothUp(), BothSlam(), 8f, Reach.Mid, windTime: 0.9f, effect: HitEffect.Burn));

            // ---- more specials anyone may learn
            Add(new KalakhMove { name = "Ground Rupture", kind = MoveKind.Eruption, tier = 3, minRange = 2f, maxRange = 12f, cooldown = 9f, count = 3, radius = 1.4f, delay = 0.9f })
                .Add(Blow(StompWind(), StompHit(), 14f, Reach.Low, windTime: 0.6f, heavy: true));
            Add(new KalakhMove { name = "Bone Shrapnel", kind = MoveKind.Projectile, tier = 3, minRange = 3f, maxRange = 12f, cooldown = 8f, missile = MissileKind.Bone, count = 4, speed = 12f, duration = 1f })
                .Add(Blow(ThrowWind(), ThrowHit(), 8f, Reach.Mid, windTime: 0.6f));
            Add(new KalakhMove { name = "Venom Fan", kind = MoveKind.Projectile, tier = 5, minRange = 3f, maxRange = 12f, cooldown = 9f, missile = MissileKind.Venom, count = 3, speed = 9f, duration = 0.6f })
                .Add(Blow(BiteWind(), BiteHit(), 7f, Reach.Mid, windTime: 0.5f, effect: HitEffect.Slow));
            Add(new KalakhMove { name = "Rolling Charge", kind = MoveKind.Charge, tier = 2, minRange = 3f, maxRange = 10f, cooldown = 7f, speed = 6.5f })
                .Add(Blow(Charging(), Charging(), 13f, Reach.Mid, 0.9f, 0f, 0.6f, 0.2f, 0f, HitShape.Line, HitEffect.Knockback, heavy: true));
            Add(new KalakhMove { name = "Skyfall Bite", kind = MoveKind.Leap, tier = 3, minRange = 3f, maxRange = 8f, cooldown = 8f, radius = 2f, speed = 2.6f, then = savageBite })
                .Add(Blow(Crouch(), Land(), 12f, Reach.Mid, 2f, 0f, 0.55f, 0.2f, 0f, HitShape.Circle, heavy: true));
            Add(new KalakhMove { name = "Blink Uppercut", kind = MoveKind.ShadowStep, tier = 4, minRange = 0f, maxRange = 9f, cooldown = 9f, then = M("Blink Upper", MoveKind.Melee, 4, 0f, 3f, 0f, Upper(true, 15f)) });
            Add(new KalakhMove { name = "Smoke Roar", kind = MoveKind.Roar, tier = 3, minRange = 0f, maxRange = 5f, cooldown = 11f, radius = 4.5f })
                .Add(Blow(Rear(), Taunt(), 4f, Reach.Mid, windTime: 0.6f, effect: HitEffect.Blind));
            Add(new KalakhMove { name = "Brimstone Breath", kind = MoveKind.Breath, tier = 5, minRange = 0f, maxRange = 5.5f, cooldown = 11f, duration = 2.4f, radius = 5.5f })
                .Add(Blow(BreathWind(), BreathHit(), 7f, Reach.Mid, 5.5f, 55f, 0.8f, effect: HitEffect.Burn));
            Add(new KalakhMove { name = "Whirling Talons", kind = MoveKind.Spin, tier = 3, minRange = 0f, maxRange = 2.5f, cooldown = 7f, count = 2, radius = 2.3f })
                .Add(Blow(SpinCoil(), SpinOut(), 9f, Reach.Mid, 2.3f, 0f, 0.55f, 0.35f, 0.3f, HitShape.Circle));
            Add(new KalakhMove { name = "High Feint, Low Reap", kind = MoveKind.Feint, tier = 3, minRange = 0f, maxRange = 2.2f, cooldown = 7f, delay = 0.4f, then = reap })
                .Add(Blow(CleaverWind(), CleaverWind(), 0f, Reach.Mid, windTime: 0.6f));
            Add(new KalakhMove { name = "Low Feint, Skull Bash", kind = MoveKind.Feint, tier = 2, minRange = 0f, maxRange = 2f, cooldown = 7f, delay = 0.4f, then = headbutt })
                .Add(Blow(KickWind(), KickWind(), 0f, Reach.Mid, windTime: 0.5f));
            Add(new KalakhMove { name = "Retreating Spit", kind = MoveKind.Retreat, tier = 3, minRange = 0f, maxRange = 2.2f, cooldown = 8f, radius = 3.5f,
                                 then = new KalakhMove { name = "Parting Spit", kind = MoveKind.Projectile, tier = 3, missile = MissileKind.Fire, count = 2, speed = 9f, duration = 0.5f }
                                     .Add(Blow(BreathWind(), BreathHit(), 9f, Reach.Mid, windTime: 0.45f, effect: HitEffect.Burn)) });
            Add(new KalakhMove { name = "Scissor Ambush", kind = MoveKind.ShadowStep, tier = 3, minRange = 2f, maxRange = 9f, cooldown = 10f, then = scissor });
            Add(new KalakhMove { name = "Hobbling Pounce", kind = MoveKind.Leap, tier = 3, minRange = 3f, maxRange = 9f, cooldown = 9f, radius = 1.8f, speed = 2.2f, then = kneeBreaker })
                .Add(Blow(Crouch(), Land(), 12f, Reach.Mid, 1.8f, 0f, 0.6f, 0.2f, 0f, HitShape.Circle, heavy: true));
            Add(new KalakhMove { name = "Cleaving Charge", kind = MoveKind.Charge, tier = 4, minRange = 3.5f, maxRange = 12f, cooldown = 9f, speed = 7f, then = cleaver })
                .Add(Blow(Charging(), Charging(), 12f, Reach.Mid, 0.9f, 0f, 0.7f, 0.2f, 0f, HitShape.Line, HitEffect.Knockback, heavy: true));

            Add(new KalakhMove { name = "Lunging Fangs", kind = MoveKind.Melee, tier = 3, minRange = 1.2f, maxRange = 3.4f, cooldown = 6f }.Add(Lunge(1.3f, 10f)).Add(Bite(0.5f, 12f)));
            return list;
        }
    }
}
