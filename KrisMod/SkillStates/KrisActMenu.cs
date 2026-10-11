using EntityStates;
using KrisMod.Components;
using KrisMod.Content;
using KrisMod.Modules;
using RoR2;
using UnityEngine;

namespace KrisMod.SkillStates
{
    /// <summary>Marks the Body states that belong to ACT, so <see cref="ActWeaponLock"/> knows when to let go.</summary>
    public interface IKrisActState
    {
    }

    /// <summary>
    /// ACT - the special. Kris stops dead and a menu of four ACTs opens; the number keys 1-4 pick
    /// one, jump backs out, and the menu closes by itself after a while.
    ///
    /// The menu is drawn by <see cref="ActMenuHud"/>, which also latches the number keys, and
    /// meanwhile the Weapon machine is parked in <see cref="ActWeaponLock"/> so the skill keys
    /// cannot swing, raise the shield or throw the SOUL while Kris stands frozen in the menu.
    /// Only the authority draws and reads the menu; what it picks becomes an
    /// ordinary networked state change, so every client sees the ACT itself.
    /// </summary>
    public class KrisActMenu : BaseKrisState, IKrisActState
    {
        private KrisAct[] options;
        private ActMenuHud hud;
        private bool chose;

        public override void OnEnter()
        {
            base.OnEnter();

            PlayPose(KrisAnimations.Act, 0.1f);
            Sounds.PlayAt(Sounds.ActSelect, gameObject);

            if (!isAuthority) return;

            options = KrisActs.Draw();

            EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
            if (weapon != null) weapon.SetInterruptState(new ActWeaponLock(), InterruptPriority.Frozen);

            hud = GetComponent<ActMenuHud>();
            if (hud != null) hud.Show(options, tension);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            // Static: keep whatever vertical speed there is (a menu opened mid-air still falls),
            // drop the rest.
            if (characterMotor != null)
            {
                characterMotor.velocity = new Vector3(0f, Mathf.Min(0f, characterMotor.velocity.y), 0f);
                characterMotor.moveDirection = Vector3.zero;
            }

            if (!isAuthority || chose) return;

            if (inputBank != null && inputBank.jump.justPressed)
            {
                Close(refund: true);
                return;
            }

            if (fixedAge >= Mathf.Max(1f, KrisTuning.ActMenuTimeout))
            {
                Close(refund: true);
                return;
            }

            int picked = PickedIndex();
            if (picked < 0 || options == null || picked >= options.Length) return;

            // The state is built first so it can turn the pick down (PROCEED with nothing in
            // front of Kris) before any TP is taken.
            KrisAct act = options[picked];
            var state = (EntityState)System.Activator.CreateInstance(act.State);
            var prepared = state as BaseKrisAct;
            if ((prepared != null && !prepared.Prepare(characterBody, GetAimRay()))
                || tension == null || !tension.TrySpend(act.Cost))
            {
                if (hud != null) hud.Deny(picked);
                // Only the authority reads the menu, so only the player picking hears this.
                Sounds.PlayAt(Sounds.ActDeny, gameObject);
                return;
            }

            // The selection sound is the ACT state's, so every client hears it, not just this one.
            chose = true;
            outer.SetNextState(state);
        }

        /// <summary>
        /// Which option was just picked off the number row (1-4). The keys are latched by the HUD
        /// in Update: GetKeyDown is per rendered frame and would be missed by a FixedUpdate that
        /// does not land on it.
        /// </summary>
        private int PickedIndex() => hud != null ? hud.TakeKeyPick() : -1;

        /// <summary>
        /// Backing out gives the ACT back: opening the menu is free, only an ACT costs anything.
        /// The special slot's stock was spent on opening, and Reset restores it and its cooldown.
        /// </summary>
        private void Close(bool refund)
        {
            if (refund && skillLocator != null && skillLocator.special != null) skillLocator.special.Reset();
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            if (hud != null) hud.Hide();
            ReleasePose();
            base.OnExit();
        }

        /// <summary>
        /// Frozen: nothing but death and freezes may interrupt the menu - in particular not the
        /// special itself, which would otherwise reopen it on a second press.
        /// </summary>
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }

    /// <summary>
    /// Parks the Weapon machine for as long as an ACT is open or running, so the skill keys
    /// cannot fire the skills they normally do. Lets go on its own once the
    /// Body machine has left every ACT state.
    /// </summary>
    public class ActWeaponLock : BaseState
    {
        private EntityStateMachine body;

        public override void OnEnter()
        {
            base.OnEnter();
            body = EntityStateMachine.FindByCustomName(gameObject, "Body");
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;

            // A tick of grace, so the lock does not release in the frame before the menu starts.
            if (fixedAge < 0.1f) return;
            if (body == null || !(body.state is IKrisActState)) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }
}
