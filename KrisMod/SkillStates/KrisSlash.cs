using System.Collections.Generic;
using EntityStates;
using KrisMod.Components;
using KrisMod.Modules;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace KrisMod.SkillStates
{
    /// <summary>
    /// FIGHT - the primary. Kris slashes with their sword for 250% damage, alternating forehand
    /// and backhand through SteppedSkillDef: flat, sideways cuts swung from the upper body alone,
    /// so they play over the walk, the run and the jump (BaseKrisState.PlayGesture).
    /// </summary>
    public class KrisSlash : BaseKrisState, SteppedSkillDef.IStepSetter
    {
        public const string HitBoxGroupName = "KrisSword";

        public static float attackStartFraction = 0.2f;
        public static float attackEndFraction = 0.5f;
        public static float procCoefficient = 1f;
        public static float pushAwayForce = 600f;

        /// <summary>
        /// The swing arc shows the blade's cut, frames 8-14 of the 30-frame SlashA/SlashB
        /// (KrisRig/posetool.py, slash_frames): it starts at 8/30 and its head reaches the end of
        /// the arc at 14/30. Move these with the clips.
        /// </summary>
        private const float ArcStartFraction = 0.25f;
        private const float ArcRevealFraction = 0.22f;

        private readonly List<HurtBox> hits = new List<HurtBox>();
        private OverlapAttack attack;
        private float duration;
        private bool attackStarted;
        private bool arcSpawned;
        private bool attackFinished;
        private int swingIndex;
        private static bool warnedAboutHitbox;

        /// <summary>
        /// Called by SteppedSkillDef so consecutive swings alternate - on the authority only, since
        /// that is where the skill fires. Every other machine learns the step from OnDeserialize.
        /// </summary>
        public void SetStep(int i) => swingIndex = i;

        public override void OnEnter()
        {
            base.OnEnter();

            duration = KrisConfig.SlashDuration.Value / attackSpeedStat;

            StartAimMode(2f, false);
            if (characterBody != null) characterBody.SetAimTimer(1.5f);

            string pose = swingIndex % 2 == 0 ? KrisAnimations.SlashA : KrisAnimations.SlashB;
            // Crossfade and clip both scale with attack speed, so the swing fills the state at any
            // speed instead of being cut off before the blade comes through. Upper body only: Kris
            // keeps walking, running and jumping under the swing.
            PlayGesture(pose, 0.05f / Mathf.Max(attackSpeedStat, 0.1f), KrisAnimations.SlashA);
            SetPoseSpeed(attackSpeedStat);

            attack = InitMeleeOverlap(KrisConfig.SlashDamage.Value, HitEffect,
                                      GetModelTransform(), HitBoxGroupName);
            attack.procCoefficient = procCoefficient;
            attack.pushAwayForce = pushAwayForce;
            attack.damageType = KrisDamage.Of(DamageType.Generic, DamageSource.Primary);
            attack.damageColorIndex = DamageColorIndex.Default;

            if (isAuthority && attack.hitBoxGroup == null && !warnedAboutHitbox)
            {
                warnedAboutHitbox = true;
                Log.Error("FIGHT found no HitBoxGroup named '" + HitBoxGroupName +
                          "' on the model - the slash will not damage anything.");
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            float fraction = duration > 0f ? fixedAge / duration : 1f;

            if (!attackStarted && fraction >= attackStartFraction) attackStarted = true;

            if (!arcSpawned && fraction >= ArcStartFraction)
            {
                arcSpawned = true;
                SpawnSwingEffect();
                PlaySwingSound();
            }

            if (attackStarted && !attackFinished)
            {
                if (fraction >= attackEndFraction)
                {
                    attackFinished = true;
                }
                else if (isAuthority && attack != null)
                {
                    hits.Clear();
                    if (attack.Fire(hits) && tension != null) tension.AddForHits(hits.Count);
                }
            }

            if (fixedAge >= duration && isAuthority)
            {
                outer.SetNextStateToMain();
            }
        }

        /// <summary>
        /// Played with the arc, as the blade comes through - not on entering the state. The sound
        /// is loud from its first sample, and from OnEnter it rang out over the 7-frame wind-up,
        /// well before the cut.
        /// </summary>
        private void PlaySwingSound()
        {
            // Every swing, one clip picked at random out of Primary (Basic or Basic2).
            Sounds.PlayAt(Sounds.Primary, gameObject);
        }

        private bool Forehand => swingIndex % 2 == 0;

        /// <summary>The DELTARUNE cut mark for this swing, or Merc's impact if it failed to build.</summary>
        private GameObject HitEffect
        {
            get
            {
                GameObject own = Forehand ? KrisAssets.FightHitA : KrisAssets.FightHitB;
                return own != null ? own : KrisAssets.SlashHitEffect;
            }
        }

        /// <summary>
        /// The crescent along the sword tip's path (Components/SlashArcVfx), local to every client
        /// since each runs this state. Both swings are flat, sideways cuts round Kris's body at
        /// chest height, matching KrisRig/posetool.py (SWINGS "A" and "B"):
        /// A, the forehand, sweeps from low ahead on the right, across the front, to behind on the left;
        /// B, the backhand, sweeps back from the left to behind on the right.
        /// (X-SLASH's crossing down-cuts are SWINGS "XA" and "XB", and are not these.)
        /// </summary>
        private void SpawnSwingEffect()
        {
            Ray aim = GetAimRay();
            Vector3 forward = new Vector3(aim.direction.x, 0f, aim.direction.z);
            if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
            forward.Normalize();

            // A positive roll tips the arc's "up" toward Kris's left: rolled a full 90 the arc lies
            // level, and its angle is the swing's own - 0 ahead, +90 Kris's left, -90 their right.
            const float roll = 90f;
            // The blade chambers at -12 (A) / 101 (B) and finishes at 169 / -129; the smear starts
            // once it is moving and runs a little past the steel, not far past it.
            float from = Forehand ? -5f : 95f;
            float to = Forehand ? 178f : -140f;

            Quaternion rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 0f, roll);
            Vector3 centre = characterBody.corePosition + Vector3.up * 0.2f;
            SlashArcVfx.Spawn(transform, centre, rotation, from, to, duration * ArcRevealFraction);
        }

        /// <summary>
        /// Skill, not Any. SkillDef.CanExecute never checks whether we are already in this state,
        /// so a primary that reports Any re-executes every frame the button is held and never
        /// reaches its damage window (found on Flowery). Skill blocks the primary from
        /// interrupting itself while still letting the other slots cut the swing short.
        /// </summary>
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;

        public override void OnExit()
        {
            ReleaseGesture();
            base.OnExit();
        }

        /// <summary>
        /// The step travels with the state, as Loader's SwingComboFist sends its gauntlet: without
        /// it every other player saw swing 0, the forehand, every time.
        /// </summary>
        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write((byte)swingIndex);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            swingIndex = reader.ReadByte();
        }
    }
}
