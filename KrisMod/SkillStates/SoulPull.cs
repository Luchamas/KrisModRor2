using EntityStates;
using KrisMod.Components;
using KrisMod.Modules;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace KrisMod.SkillStates
{
    /// <summary>
    /// SOUL, second half: Kris rips the SOUL out of their chest - it hurts, 1 HP - and throws it
    /// (the "SoulThrow" clip), it flies out to <see cref="target"/> while Kris hangs where they
    /// are, tethered to it, and once it lands Kris is dragged along the tether to it. Close in,
    /// the SOUL comes to the free hand, and the catch hands over to <see cref="SoulSlash"/> on
    /// "Weapon"; the heart goes back into the chest as this state ends. M1 or Shift during the
    /// pull ends it early the same way, the SOUL flying back from wherever it was left.
    ///
    /// On the "Body" machine, since it drives the motor. The target is serialized so that every
    /// client draws the SOUL flying to the same place; the movement itself is the authority's
    /// motor, which the game already replicates.
    /// </summary>
    public class SoulPull : BaseKrisState
    {
        /// <summary>Hang time floor, so a point-blank throw still reads as a throw.</summary>
        private const float MinFlightTime = 0.12f;

        /// <summary>How much of the pull's speed is kept when it ends, so Kris lands rather than stops dead.</summary>
        private const float ExitSpeedFraction = 0.35f;

        /// <summary>Extra time past the ideal pull before giving up - something is in the way.</summary>
        private const float PullGrace = 0.4f;

        /// <summary>Upward kick at the end, so a pull onto a ledge clears its lip.</summary>
        private const float ExitHop = 6f;

        /// <summary>When the heart shows in the fist, and when it leaves the hand: frames 4 and 10 of
        /// "SoulThrow" at 60 fps. ⚠ posetool THROW_RIP / THROW_RELEASE time the clip - change both.</summary>
        private const float RipAt = 0.05f;
        private const float ReleaseAt = 0.15f;

        /// <summary>How far out the SOUL starts sliding off the ground into the reaching hand.</summary>
        private const float GrabReach = 4f;

        /// <summary>How long the caught SOUL takes to sink back into the chest.</summary>
        private const float AbsorbTime = 0.15f;

        /// <summary>What tearing the SOUL out costs.</summary>
        private const float RipDamage = 1f;

        /// <summary>The longest a SOUL left lying far off takes to fly back into the chest.</summary>
        private const float MaxReturnTime = 0.45f;

        public Vector3 target;
        public Vector3 origin;

        private SoulVisual soul;
        private Transform hand;
        private Vector3 releasedFrom;
        private bool released;
        private float flightTime;
        private float pullTime;
        private bool pulling;
        private float pullStartedAt;
        private Vector3 pullDirection;
        private bool gravitySuspended;
        private float hurtEndsAt;
        private bool pullSoundPlayed;
        private bool caught;

        public override void OnEnter()
        {
            base.OnEnter();

            if (origin == Vector3.zero) origin = SoulOrigin();

            float distance = Vector3.Distance(origin, target);
            flightTime = Mathf.Max(MinFlightTime, distance / Mathf.Max(1f, KrisConfig.SoulSpeed.Value));

            // Where Kris will end up: short of the SOUL, measured from the feet rather than the chest.
            Vector3 chestToFeet = transform.position - origin;
            Vector3 toTarget = target - origin;
            Vector3 stopPoint = target - toTarget.normalized * Mathf.Min(SoulAim.StandOff, toTarget.magnitude);
            float pullDistance = Vector3.Distance(transform.position, stopPoint + chestToFeet);
            pullTime = pullDistance / Mathf.Max(1f, KrisConfig.SoulPullSpeed.Value);

            hand = FindModelChild("HandL");
            releasedFrom = origin;

            PlayPose(KrisAnimations.SoulThrow, 0.05f, KrisAnimations.SoulAim);
            LockFacing(toTarget);

            if (characterMotor != null)
            {
                Gravity.Suspend(characterMotor);
                gravitySuspended = true;
                characterMotor.velocity = Vector3.zero;
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            if (!pulling)
            {
                // Hang in the air through the throw and while the SOUL flies.
                if (characterMotor != null) characterMotor.velocity = Vector3.zero;

                if (soul == null && fixedAge >= RipAt) Rip();
                if (fixedAge < ReleaseAt) return;

                if (!released)
                {
                    released = true;
                    releasedFrom = HandPosition();
                    flightTime = Mathf.Max(MinFlightTime, Vector3.Distance(releasedFrom, target) /
                                                          Mathf.Max(1f, KrisConfig.SoulSpeed.Value));
                }

                float t = Mathf.Clamp01((fixedAge - ReleaseAt) / flightTime);
                if (soul != null) soul.transform.position = Vector3.Lerp(releasedFrom, target, t);

                if (t >= 1f) BeginPull();
                return;
            }

            PlaceSoul();
            if (!pullSoundPlayed && fixedAge >= hurtEndsAt) PlayPullSound();
            if (!isAuthority || characterMotor == null) return;

            // M1 or Shift ends the pull where it is, as if the SOUL had been reached: the same
            // somersault, and the SOUL flies back from where it lies. Read here rather than as
            // skills, since nothing fires skills while the Body machine is away from GenericCharacterMain.
            if (inputBank != null && (inputBank.skill1.justPressed || inputBank.skill3.justPressed))
            {
                Catch();
                return;
            }

            Vector3 toSoul = target - NearestPointTo(target);
            float remaining = toSoul.magnitude;
            float pulledFor = fixedAge - pullStartedAt;

            // Out of time means something is in the way - most likely whatever the SOUL was
            // thrown at - so that is a catch too, and the cuts still come.
            if (remaining <= SoulAim.StandOff || pulledFor >= pullTime + PullGrace)
            {
                Catch();
                return;
            }

            pullDirection = toSoul / remaining;
            characterMotor.velocity = pullDirection * KrisConfig.SoulPullSpeed.Value;
            LockFacing(pullDirection);
        }

        public override void Update()
        {
            base.Update();
            // Held, the heart rides the fist every rendered frame, not just every physics step.
            if (soul != null && !released) soul.transform.position = HandPosition();
            else if (pulling) PlaceSoul();
        }

        private void Rip()
        {
            soul = SoulVisual.Create(HandPosition());
            // No chest in the ChildLocator means no tether - the heart still flies.
            Transform chest = FindModelChild("Chest");
            if (chest != null) soul.SetTether(chest);

            int hurt = Sounds.PickIndex(Sounds.UtilityHurt);
            Sounds.PlayAt(Sounds.UtilityHurt, hurt, gameObject);
            if (!Sounds.Overlaps) hurtEndsAt = fixedAge + Sounds.AudibleLength(Sounds.UtilityHurt, hurt);

            if (NetworkServer.active && healthComponent != null)
            {
                // Kris's own doing, so no attacker to credit; non-lethal, and past armour and
                // blocks, so it is always exactly the one point and never the last one.
                healthComponent.TakeDamage(new DamageInfo
                {
                    damage = RipDamage,
                    attacker = null,
                    inflictor = gameObject,
                    position = HandPosition(),
                    force = Vector3.zero,
                    crit = false,
                    procCoefficient = 0f,
                    damageType = KrisDamage.Of(DamageType.NonLethal | DamageType.BypassArmor | DamageType.BypassBlock,
                                               DamageSource.Utility),
                    damageColorIndex = DamageColorIndex.Default,
                });
            }
        }

        /// <summary>
        /// Through the pull the SOUL waits where it landed; over the last <see cref="GrabReach"/>
        /// it slides into the reaching hand, there by the time the pull stops.
        /// </summary>
        private void PlaceSoul()
        {
            if (soul == null) return;
            float remaining = Vector3.Distance(target, NearestPointTo(target));
            float k = Mathf.Clamp01((remaining - SoulAim.StandOff) / GrabReach);
            soul.transform.position = Vector3.Lerp(HandPosition(), target, k);
        }

        /// <summary>
        /// The pull's whoosh. Where lines cut each other off, it waits for the cry of the rip to
        /// finish, and is dropped if the pull is over by then.
        /// </summary>
        private void PlayPullSound()
        {
            pullSoundPlayed = true;
            Sounds.PlayAt(Sounds.UtilityPull, gameObject);
        }

        private void Catch()
        {
            caught = true;

            EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
            if (weapon != null) weapon.SetNextState(new SoulSlash { direction = pullDirection });
            else Log.Error("Kris has no Weapon state machine - the SOUL is caught without the cuts.");

            outer.SetNextStateToMain();
        }

        /// <summary>
        /// The point of Kris nearest <paramref name="point"/>, on the line from the feet up to the
        /// chest. The pull is measured from here rather than from the chest: a SOUL lying on the
        /// ground is a chest-height below it, so the chest alone could never come within
        /// <see cref="SoulAim.StandOff"/> - Kris would overshoot, turn back, and shudder over the
        /// spot until <see cref="PullGrace"/> ran out. The feet can walk right up to it.
        /// </summary>
        private Vector3 NearestPointTo(Vector3 point)
        {
            Vector3 chest = SoulOrigin();
            Vector3 feet = characterBody != null ? characterBody.footPosition : transform.position;
            Vector3 up = chest - feet;
            float along = up.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(point - feet, up) / up.sqrMagnitude) : 0f;
            return feet + up * along;
        }

        /// <summary>Where the free hand is - or where the SOUL starts, on a model without one.</summary>
        private Vector3 HandPosition() => hand != null ? hand.position : origin;

        private void BeginPull()
        {
            pulling = true;
            pullStartedAt = fixedAge;
            pullDirection = (target - NearestPointTo(target)).normalized;

            PlayPose(KrisAnimations.SoulPull, 0.06f, KrisAnimations.SoulAim);
            if (fixedAge >= hurtEndsAt) PlayPullSound();

            if (KrisAssets.SoulLandEffect != null)
            {
                KrisEffects.SpawnDetached(KrisAssets.SoulLandEffect, target,
                                          Quaternion.LookRotation(-pullDirection), 1.5f);
            }

            if (isAuthority && characterMotor != null)
            {
                characterMotor.Motor.ForceUnground();
                characterMotor.disableAirControlUntilCollision = false;
            }
        }

        public override void OnExit()
        {
            // Back into the chest, outliving this state so the catch does not blink it away. Timed
            // by how far it has to go: caught, it is in the hand and sinks straight in; let go
            // mid-pull, it is still lying out where it landed and flies back - at its throwing
            // speed, so every client times it the same without being told the pull was cut short.
            if (soul != null)
            {
                Transform chest = FindModelChild("Chest");
                float away = chest != null ? Vector3.Distance(soul.transform.position, chest.position) : 0f;
                float back = Mathf.Clamp(away / Mathf.Max(1f, KrisConfig.SoulSpeed.Value), AbsorbTime, MaxReturnTime);
                soul.Absorb(chest, back);
            }

            if (characterMotor != null)
            {
                if (gravitySuspended)
                {
                    Gravity.Restore(characterMotor);
                    gravitySuspended = false;
                }

                if (isAuthority && pulling)
                {
                    characterMotor.velocity = pullDirection * (KrisConfig.SoulPullSpeed.Value * ExitSpeedFraction) +
                                              Vector3.up * ExitHop;
                    // At least the cuts' own hop, so it comes out the same whichever machine
                    // moves first this frame - the cuts' entry raises the climb to it, never past it.
                    if (caught) characterMotor.velocity.y = Mathf.Max(characterMotor.velocity.y, SoulSlash.Hop);
                }
            }

            ReleasePose();
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(target);
            writer.Write(origin);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            target = reader.ReadVector3();
            origin = reader.ReadVector3();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
