using EntityStates;
using KrisMod.Components;
using KrisMod.Modules;
using RoR2;
using UnityEngine;

namespace KrisMod.SkillStates
{
    /// <summary>
    /// SOUL, first half: Kris braces in the "Utility Aim" pose, hand at the chest, ready to pull
    /// their SOUL out, for as long as the button is held. The SOUL itself stays inside until the
    /// release; all the aiming player sees is a translucent ghost of it where it would land.
    /// Releasing spends the TP and hands over to <see cref="SoulPull"/> on the Body machine, which
    /// throws it and drags Kris after.
    ///
    /// On "Weapon", so Kris can keep walking while aiming; the pull is on "Body" because it
    /// drives the motor and would otherwise fight GenericCharacterMain for the velocity.
    /// </summary>
    public class SoulAim : BaseKrisState
    {
        /// <summary>How far short of the stopping point the pull ends, so Kris does not end inside a wall.</summary>
        internal const float StandOff = 1.2f;

        /// <summary>How see-through the landing ghost is.</summary>
        private const float GhostAlpha = 0.4f;

        private SoulVisual marker;
        private bool released;

        public override void OnEnter()
        {
            base.OnEnter();

            // Upper body only (the Gesture layer), so the legs keep walking/running while aiming.
            PlayGesture(KrisAnimations.SoulAim, 0.1f);
            // ...and standing still, the author's braced legs as well (the Brace layer, weighted by stillness).
            var locomotion = GetComponent<KrisLocomotion>();
            if (locomotion != null) locomotion.Brace(true);
            // "GrabSoul": the hand closes on the SOUL, before it is ripped out on the release.
            Sounds.PlayAt(Sounds.Utility, gameObject);

            // Only the aiming player needs to see where it will land. Built there at once, so it
            // never flashes at the chest for a frame.
            if (isAuthority) marker = SoulVisual.Create(FindTarget(GetAimRay()), GhostAlpha);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            Ray aim = GetAimRay();
            StartAimMode(aim, 0.5f, false);

            if (!isAuthority) return;

            Vector3 target = FindTarget(aim);
            if (marker != null) marker.transform.position = target;

            if (released || IsKeyDownAuthority()) return;
            released = true;

            Throw(target);
            outer.SetNextStateToMain();
        }

        private void Throw(Vector3 target)
        {
            if (tension == null || !tension.TrySpend(KrisTuning.SoulTensionCost))
            {
                // TP only rises between activation (where TensionSkillDef checked it) and here,
                // so this is a meter that went missing, not a player who ran short.
                Log.Warning("SOUL released without the TP to pay for it - not thrown.");
                return;
            }

            EntityStateMachine body = EntityStateMachine.FindByCustomName(gameObject, "Body");
            if (body == null)
            {
                Log.Error("Kris has no Body state machine - the SOUL cannot pull.");
                return;
            }

            body.SetNextState(new SoulPull { target = target, origin = SoulOrigin() });
        }

        /// <summary>
        /// Where the SOUL would stop: whatever the crosshair is on, within range of the chest,
        /// cut short by anything solid between the chest and that point.
        /// </summary>
        private Vector3 FindTarget(Ray aim)
        {
            float range = Mathf.Max(5f, KrisTuning.SoulRange);
            int mask = LayerIndex.world.mask | LayerIndex.entityPrecise.mask;
            Vector3 origin = SoulOrigin();

            // The aim ray starts at the camera, behind Kris, so allow for the distance to the chest.
            float cameraSlack = Vector3.Distance(aim.origin, origin);
            RaycastHit hit;
            Vector3 surface = Vector3.zero;
            Vector3 aimed;
            if (Util.CharacterRaycast(gameObject, aim, out hit, range + cameraSlack, mask, QueryTriggerInteraction.Ignore))
            {
                aimed = hit.point;
                surface = hit.normal;
            }
            else aimed = aim.GetPoint(range + cameraSlack);

            Vector3 toAimed = aimed - origin;
            if (toAimed.magnitude > range)
            {
                aimed = origin + toAimed.normalized * range;
                surface = Vector3.zero;
            }

            // The SOUL flies from the chest, not the camera, so a wall the camera sees past still stops it.
            toAimed = aimed - origin;
            if (toAimed.sqrMagnitude > 0.01f &&
                Physics.Raycast(origin, toAimed.normalized, out hit, toAimed.magnitude,
                                LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                aimed = hit.point;
                surface = hit.normal;
            }

            // Off whatever it landed on by its own size: centred on the surface, half the heart
            // would be underground.
            return aimed + surface * SoulVisual.Radius;
        }

        public override void OnExit()
        {
            if (marker != null) marker.Remove();
            var locomotion = GetComponent<KrisLocomotion>();
            if (locomotion != null) locomotion.Brace(false);
            ReleaseGesture(0.1f);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
