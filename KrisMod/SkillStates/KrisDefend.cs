using EntityStates;
using KrisMod.Modules;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace KrisMod.SkillStates
{
    /// <summary>
    /// DEFEND - the secondary. Kris raises their shield and holds their ground for as long as the
    /// button is held: a planted guard, not a way of getting about.
    ///
    /// Everything the shield does is the Defend buff: the root in Buffs, the damage cut and the TP
    /// for hits taken in DefendGuard. The buff is added and removed by the server
    /// copy of this state - state changes replicate to every machine, so the server runs this
    /// OnEnter/OnExit too, which is the only place AddBuff does anything.
    ///
    /// On the "Weapon" machine, with a minimum interrupt priority of Skill: FIGHT (priority Any)
    /// cannot cut in, which is the whole trade - no swinging behind the shield - while the SOUL
    /// and ACT (PrioritySkill) still can, so the SOUL is the way out of a guard.
    /// </summary>
    public class KrisDefend : BaseKrisState
    {
        /// <summary>The motor's jump count when the guard went up, handed back when it comes down.</summary>
        private int savedJumpCount;

        public override void OnEnter()
        {
            base.OnEnter();

            PlayPose(KrisAnimations.Defend, 0.08f);
            Sounds.PlayAt(Sounds.Secondary, gameObject);
            StartAimMode(1f, false);

            if (NetworkServer.active && Buffs.Defend != null) characterBody.AddBuff(Buffs.Defend);

            if (isAuthority && characterMotor != null)
            {
                savedJumpCount = characterMotor.jumpCount;
                HoldGround();
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            if (!isAuthority) return;

            // Shield up means no sprinting; the skill def cancels it on activation, this keeps it off.
            if (characterBody != null && characterBody.isSprinting) characterBody.isSprinting = false;
            if (characterBody != null) characterBody.SetAimTimer(0.5f);

            HoldGround();

            if (!IsKeyDownAuthority()) outer.SetNextStateToMain();
        }

        /// <summary>
        /// Keeps Kris where they stand. The buff's root takes the move speed to zero, but it only
        /// lands once the server has added the buff and the stats have been recalculated, and the
        /// motor then decelerates rather than stops - so the guard would slide a body length on a
        /// sprint. Here the authority stops them dead on the ground, and uses up their jumps.
        ///
        /// Jumps are blocked through the jump count rather than the stats: a jump power of zero is
        /// still a jump, and GenericCharacterMain applies it by SETTING the vertical velocity to
        /// it, so tapping jump mid-air would have stalled a fall. The motor only ever resets the
        /// count on landing and on leaving the ground (CharacterMotor.OnLanded,
        /// OnLeaveStableGround), so writing it every tick holds.
        ///
        /// Airborne, horizontal momentum is left alone: zeroing it would drop Kris out of a jump
        /// pad's arc like a stone.
        /// </summary>
        private void HoldGround()
        {
            if (characterMotor == null) return;

            if (characterMotor.isGrounded)
            {
                Vector3 v = characterMotor.velocity;
                characterMotor.velocity = new Vector3(0f, v.y, 0f);
            }

            if (characterBody != null) characterMotor.jumpCount = characterBody.maxJumpCount;
        }

        public override void OnExit()
        {
            if (NetworkServer.active && Buffs.Defend != null && characterBody.HasBuff(Buffs.Defend))
            {
                characterBody.RemoveBuff(Buffs.Defend);
            }

            if (isAuthority && characterMotor != null)
            {
                // Standing: nothing spent, as landing would leave it. Airborne: what they had,
                // but leaving the ground while guarding spends the ground jump, exactly as
                // walking off a ledge does.
                characterMotor.jumpCount = characterMotor.isGrounded ? 0 : Mathf.Max(savedJumpCount, 1);
            }

            ReleasePose();
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }
}
