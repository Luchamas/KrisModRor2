using System.Collections.Generic;
using EntityStates;
using KrisMod.Modules;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace KrisMod.SkillStates
{
    /// <summary>
    /// SOUL, last part: the SOUL caught at the end of the pull, Kris somersaults forward once and
    /// the sword cuts one vertical circle round them, down through the front and up the back -
    /// Mercenary's Whirlwind as he does it in the air, but a single cut.
    ///
    /// On "Weapon", as Merc's is, so the Body machine is back in GenericCharacterMain and Kris
    /// keeps air control through it. <see cref="SoulPull"/> starts it, on the authority, as the
    /// pull ends; <see cref="direction"/> is the way the pull was going, serialized so every
    /// client spins the same way.
    /// </summary>
    public class SoulSlash : BaseKrisState
    {
        /// <summary>The slab the cut hits through, built on the model by KrisBody.</summary>
        public const string HitBoxGroupName = "KrisSoulSlash";

        /// <summary>Length at 1x attack speed. ⚠ The clip is posetool soul_slash_frames; keep the two the same length.</summary>
        private const float BaseDuration = 0.5f;

        /// <summary>
        /// When the cut lands, as a fraction of the state: where the blade comes down through the
        /// front. ⚠ posetool SOUL_SLASH_CUTS - change both.
        /// </summary>
        private const float CutAt = 0.34f;

        /// <summary>How long the box stays live after the cut, at 1x attack speed: the blade's way down the front.</summary>
        private const float CutWindow = 0.08f;

        /// <summary>Upward speed at the start, so a catch on the ground still leaves room to flip.</summary>
        internal const float Hop = 8f;

        private static bool warnedAboutHitbox;

        public Vector3 direction;

        private float duration;
        private bool cut;
        private Vector3 flat;
        private OverlapAttack attack;
        private float attackEnd;
        private readonly List<HurtBox> hits = new List<HurtBox>();

        public override void OnEnter()
        {
            base.OnEnter();

            duration = BaseDuration / Mathf.Max(attackSpeedStat, 0.1f);

            flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 0.001f && characterDirection != null) flat = characterDirection.forward;
            if (flat.sqrMagnitude < 0.001f) flat = transform.forward;
            flat.Normalize();

            PlayPose(KrisAnimations.SoulSlash, 0.05f / Mathf.Max(attackSpeedStat, 0.1f), KrisAnimations.SoulPull);
            SetPoseSpeed(attackSpeedStat);
            LockFacing(flat);

            if (isAuthority && characterMotor != null)
            {
                characterMotor.Motor.ForceUnground();
                characterMotor.velocity.y = Mathf.Max(characterMotor.velocity.y, Hop);
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            if (!cut && fixedAge >= CutAt * duration) Cut();

            if (attack != null)
            {
                hits.Clear();
                if (attack.Fire(hits) && tension != null) tension.AddForHits(hits.Count);
                if (fixedAge >= attackEnd) attack = null;
            }

            if (isAuthority && fixedAge >= duration) outer.SetNextStateToMain();
        }

        private void Cut()
        {
            cut = true;

            // FIGHT's swing sound - the occasional one, which is the one with the blade in it.
            Sounds.PlayAt(Sounds.PrimaryOccasional, gameObject);

            // Merc's whirlwind swings round in its own XY plane. In the air his model holds it (the
            // WhirlwindAir muzzle) with X up, Y back and Z out to his left: standing in the plane
            // he flips in, through his front and back. The same here, off the locked facing.
            if (KrisAssets.XSlashEffect != null)
            {
                Vector3 centre = characterBody != null ? characterBody.corePosition : transform.position;
                Vector3 left = Vector3.Cross(flat, Vector3.up);
                KrisEffects.SpawnDetached(KrisAssets.XSlashEffect, centre, Quaternion.LookRotation(left, -flat), 1.5f);
            }

            if (!isAuthority) return;

            attack = InitMeleeOverlap(KrisTuning.SoulSlashDamage, KrisAssets.SlashHitEffect,
                                      GetModelTransform(), HitBoxGroupName);
            attack.procCoefficient = 1f;
            attack.pushAwayForce = 600f;
            attack.damageType = KrisDamage.Of(DamageType.Generic, DamageSource.Utility);
            attack.damageColorIndex = DamageColorIndex.Default;
            attackEnd = fixedAge + CutWindow / Mathf.Max(attackSpeedStat, 0.1f);

            if (attack.hitBoxGroup == null && !warnedAboutHitbox)
            {
                warnedAboutHitbox = true;
                Log.Error("SOUL found no HitBoxGroup named '" + HitBoxGroupName +
                          "' on the model - the cut will not damage anything.");
            }
        }

        public override void OnExit()
        {
            ReleasePose();
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(direction);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            direction = reader.ReadVector3();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
