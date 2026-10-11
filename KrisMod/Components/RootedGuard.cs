using KrisMod.Modules;
using RoR2;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// What the Rooted debuff does: no jumping while it lasts. Done the way DEFEND does it
    /// (SkillStates.KrisDefend.HoldGround), by spending the jumps every tick on the machine that
    /// moves Kris - a jump power of zero would still be a jump, and stall a fall mid-air.
    /// The buff replicates, so the owning client sees it without anything extra networked.
    /// </summary>
    public class RootedGuard : MonoBehaviour
    {
        private CharacterBody body;
        private CharacterMotor motor;
        private bool rooted;
        private int savedJumpCount;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            motor = GetComponent<CharacterMotor>();
        }

        private void FixedUpdate()
        {
            if (body == null || motor == null || !body.hasEffectiveAuthority) return;

            bool now = Buffs.Rooted != null && body.HasBuff(Buffs.Rooted);
            if (now && !rooted) savedJumpCount = motor.jumpCount;

            if (now)
            {
                motor.jumpCount = body.maxJumpCount;
            }
            else if (rooted)
            {
                // The motor only resets the count on landing, so standing it would stay spent.
                // Airborne: what they had, as DEFEND leaves it.
                motor.jumpCount = motor.isGrounded ? 0 : Mathf.Max(savedJumpCount, 1);
            }

            rooted = now;
        }
    }
}
