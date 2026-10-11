using RoR2;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// Notices the moment the ragdoll wakes, and stops Kris's animator.
    ///
    /// A sleeping ragdoll is kinematic; its bones going dynamic IS the death signal, so nothing
    /// here has to hook the death state or trust a private RoR2 field to have fired.
    ///
    /// The ragdoll is on Kris's own bones, so their Animator has to stop, or it keeps writing
    /// the pose onto the same transforms physics is writing to. Two writers on one transform is
    /// what tears a ragdoll apart.
    ///
    /// It used to have a second job: with no ragdoll of their own, re-parent their model onto the
    /// borrowed corpse's root bone. Loader's skeleton is gone now, so there is no such corpse.
    /// </summary>
    public class KrisRagdollWatcher : MonoBehaviour
    {
        /// <summary>The ragdoll whose bones carry the physics.</summary>
        public RagdollController ragdoll;

        /// <summary>Kris's animator, stopped so it cannot fight the physics. May be null.</summary>
        public Animator animatorToStop;

        private Rigidbody anchorBody;

        private void Start()
        {
            // Resolved once: the bones do not change, and this polls until they die.
            Transform anchor = FindAnchor();
            anchorBody = anchor != null ? anchor.GetComponent<Rigidbody>() : null;

            if (anchorBody == null)
            {
                Modules.Log.Warning("No ragdoll bone to watch - Kris's corpse will not fall.");
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (anchorBody == null || anchorBody.isKinematic) return;

            if (animatorToStop != null)
            {
                animatorToStop.enabled = false;
                Modules.Log.Info("Ragdoll woke: stopped '" + animatorToStop.name +
                                 "' so it cannot fight the physics.");
            }

            enabled = false;
        }

        /// <summary>
        /// The bone to watch. The pelvis is the root of the ragdoll, so it is preferred by name,
        /// with the first usable bone as a fallback in case a rig ever names it something else.
        /// </summary>
        private Transform FindAnchor()
        {
            if (ragdoll == null || ragdoll.bones == null) return null;

            Transform fallback = null;

            foreach (Transform bone in ragdoll.bones)
            {
                if (bone == null || bone.GetComponent<Rigidbody>() == null) continue;

                string name = bone.name;
                if (name.IndexOf("pelvis", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("hip", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return bone;
                }

                if (fallback == null) fallback = bone;
            }

            return fallback;
        }
    }
}
