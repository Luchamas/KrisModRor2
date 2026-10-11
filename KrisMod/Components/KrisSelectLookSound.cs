using KrisMod.Modules;
using KrisMod.SkillStates;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// Plays the "Selected/Look" sound on the character-select mannequin the moment Kris looks
    /// back over their shoulder in the SelectIntro entrance.
    ///
    /// Read off the mannequin's own animator rather than timed from when Kris was picked: the
    /// sound belongs to the head turn, so it waits for that frame however late the mannequin
    /// spawned or the clip started. The menu rebuilds the mannequin every time Kris is picked,
    /// so every pick plays it once.
    /// </summary>
    public class KrisSelectLookSound : MonoBehaviour
    {
        /// <summary>
        /// Seconds into SelectIntro the head starts to turn - posetool's SELECT_HOLD. Change both.
        /// </summary>
        internal const float LookAt = 1.0f;

        private Animator animator;
        private int introHash;
        private bool played;

        private void Start()
        {
            animator = GetComponentInChildren<Animator>(true);
            introHash = Animator.StringToHash(KrisAnimations.SelectIntro);
            if (animator == null) enabled = false;
        }

        private void Update()
        {
            if (played || animator == null || !animator.isActiveAndEnabled) return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash != introHash) return;

            // A one-shot clip: normalizedTime runs 0 to 1 over the intro, then holds.
            if (state.normalizedTime * state.length < LookAt) return;

            played = true;
            Sounds.PlayUI(Sounds.SelectedLook);
        }
    }
}
