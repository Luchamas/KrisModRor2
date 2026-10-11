using EntityStates;
using KrisMod.Components;
using UnityEngine;

namespace KrisMod.SkillStates
{
    /// <summary>Shared plumbing for Kris's skills: the TP meter, facing, and poses.</summary>
    public abstract class BaseKrisState : BaseSkillState
    {
        protected TensionController tension;

        public override void OnEnter()
        {
            base.OnEnter();
            tension = GetComponent<TensionController>();
        }

        public override void OnExit()
        {
            ReleaseFacing();
            base.OnExit();
        }

        /// <summary>True while this state has taken the facing off CharacterDirection (see <see cref="FacingLock"/>).</summary>
        private bool facingLocked;

        /// <summary>
        /// Takes the facing away from CharacterDirection, and reports the transform to rotate.
        ///
        /// A skill cannot ask nicely for the model's heading: CharacterDirection eases it round
        /// while the motor keeps writing its own heading from movement input, and raising the
        /// turn speed to win that race only made the two writers alternate every frame, which
        /// read as flickering (found on Flowery). So the component is switched off for the rest
        /// of the state and the model base is rotated here instead.
        /// </summary>
        private Transform TakeFacing(Vector3 direction, out Vector3 flat)
        {
            flat = Vector3.zero;
            if (characterDirection == null) return null;

            var wanted = new Vector3(direction.x, 0f, direction.z);
            if (wanted.sqrMagnitude < 0.001f) return null;
            flat = wanted.normalized;

            if (!facingLocked)
            {
                facingLocked = true;
                FacingLock.Take(characterDirection);
            }

            Transform target = characterDirection.targetTransform;
            if (target == null) target = GetModelBaseTransform();
            return target;
        }

        /// <summary>Points the model flat along <paramref name="direction"/> at once.</summary>
        protected void LockFacing(Vector3 direction)
        {
            Vector3 flat;
            Transform target = TakeFacing(direction, out flat);
            if (target == null) return;

            target.rotation = Quaternion.LookRotation(flat);
            // Kept in step so handing control back does not spin the model round again.
            characterDirection.forward = flat;
        }

        /// <summary>Eases the model toward <paramref name="direction"/>, as the only writer.</summary>
        protected void TrackFacing(Vector3 direction, float degreesPerSecond = 720f)
        {
            Vector3 flat;
            Transform target = TakeFacing(direction, out flat);
            if (target == null) return;

            target.rotation = Quaternion.RotateTowards(target.rotation,
                                                       Quaternion.LookRotation(flat),
                                                       degreesPerSecond * Time.fixedDeltaTime);
            characterDirection.forward = flat;
        }

        /// <summary>Hands the facing back to CharacterDirection. Safe to call unconditionally.</summary>
        protected void ReleaseFacing()
        {
            if (!facingLocked) return;
            FacingLock.Give(characterDirection);
            facingLocked = false;
        }

        /// <summary>
        /// Crossfades to one of Kris's poses.
        ///
        /// The controller is a single layer of whole-body poses rather than vanilla's stack of
        /// masked override layers: a state names the pose it wants and the next state names its
        /// own. Playing a state the animator does not have silently freezes the crossfade, so
        /// it is checked. The controller comes out of the asset bundle while this comes out of
        /// the DLL, and the two are deployed by different commands, so a pose added on this side
        /// can be a rebuild ahead of the bundle - <paramref name="fallback"/> is what to wear
        /// until it catches up.
        /// </summary>
        protected void PlayPose(string state, float crossfade = 0.08f, string fallback = null)
        {
            Animator animator = GetKrisAnimator();
            if (animator == null || string.IsNullOrEmpty(state)) return;

            int layer = animator.GetLayerIndex(KrisAnimations.BaseLayer);
            if (layer < 0) layer = 0;

            if (!animator.HasState(layer, Animator.StringToHash(state)))
            {
                KrisAnimations.ReportOnce(state, "animator has no pose '" + state + "'" +
                    (string.IsNullOrEmpty(fallback) ? "" : " - wearing '" + fallback + "' instead"));
                if (string.IsNullOrEmpty(fallback) || fallback == state) return;
                PlayPose(fallback, crossfade);
                return;
            }

            ownedPose = state;
            animator.CrossFadeInFixedTime(state, crossfade, layer);
            // Before the animator has run, the pose is invisible to KrisLocomotion - tell it.
            var locomotion = GetComponent<KrisLocomotion>();
            if (locomotion != null) locomotion.SkillPosed();
            ApplyWeaponsFor(state);
        }

        /// <summary>
        /// The sword and shield belong to the pose, not to the skill - the rule Flowery's cape
        /// follows. Set here, the one place a skill ever sets a pose, there is no hide-in-OnEnter /
        /// show-in-OnExit pairing to get out of step across the two state machines: whichever pose
        /// was played last says what is out. (The enforcer's own starting pose is the exception,
        /// and it applies the same rule itself.)
        /// </summary>
        private void ApplyWeaponsFor(string pose)
        {
            Transform model = GetModelTransform();
            if (model == null) return;

            var enforcer = model.GetComponent<KrisModelEnforcer>();
            if (enforcer != null)
            {
                enforcer.WantWeapons(KrisAnimations.ShowsSword(pose), KrisAnimations.ShowsShield(pose));
            }
        }

        /// <summary>The pose this state last asked for, so it can tell whether it still owns it.</summary>
        private string ownedPose;

        /// <summary>
        /// Back to the idle pose - but only if this state's own pose is still the one showing.
        ///
        /// Kris's skills are split across two entity state machines ("Weapon" for the slash,
        /// shield and SOUL aim, "Body" for the pull and the ACT menu), and the controller is a
        /// single layer of whole-body poses, so the last writer wins. A slash that finishes on
        /// its own during a pull must not yank the pose back to idle in the middle of it.
        /// </summary>
        protected void ReleasePose()
        {
            if (string.IsNullOrEmpty(ownedPose)) return;

            Animator animator = GetKrisAnimator();
            if (animator == null) return;

            int layer = animator.GetLayerIndex(KrisAnimations.BaseLayer);
            if (layer < 0) layer = 0;

            // Mid-crossfade the destination is what matters, not the pose fading out of view.
            AnimatorStateInfo info = animator.IsInTransition(layer)
                ? animator.GetNextAnimatorStateInfo(layer)
                : animator.GetCurrentAnimatorStateInfo(layer);

            if (info.shortNameHash != Animator.StringToHash(ownedPose)) return;

            PlayIdle();
        }

        /// <summary>
        /// Plays <paramref name="state"/> on the upper-body Gesture layer, over whatever the legs are
        /// doing - the slashes, which Kris swings walking, running and in the air. The base layer is
        /// left alone, so KrisLocomotion keeps the run, Jump and Air going underneath and is not told
        /// a skill posed.
        ///
        /// A bundle without the layer (or without this state on it) gets the whole-body pose
        /// instead, through <see cref="PlayPose"/> - which is what Kris did before the layer existed.
        /// </summary>
        protected void PlayGesture(string state, float crossfade = 0.05f, string fallback = null)
        {
            Animator animator = GetKrisAnimator();
            if (animator == null || string.IsNullOrEmpty(state)) return;

            int layer = animator.GetLayerIndex(KrisAnimations.GestureLayer);
            var locomotion = GetComponent<KrisLocomotion>();
            if (layer < 0 || locomotion == null || !animator.HasState(layer, Animator.StringToHash(state)))
            {
                KrisAnimations.ReportOnce("gesture " + state, "animator has no '" + state + "' on layer '" +
                    KrisAnimations.GestureLayer + "' - playing it whole-body; Kris stops moving their legs to swing");
                PlayPose(state, crossfade, fallback);
                return;
            }

            // Out of nothing, the clip starts on its first frame and the layer's weight does the
            // blending; already showing a gesture (the next swing of the combo), it crossfades.
            if (animator.GetLayerWeight(layer) < 0.01f) animator.Play(state, layer, 0f);
            else animator.CrossFadeInFixedTime(state, crossfade, layer, 0f);
            locomotion.FadeGesture(layer, 1f, crossfade);
            ownedGesture = state;
            ApplyWeaponsFor(state);
        }

        /// <summary>The gesture this state last played, so it can tell whether it still owns the layer.</summary>
        private string ownedGesture;

        /// <summary>
        /// Fades the Gesture layer out - if this state's gesture is still the one on it - and gives
        /// back the whole-body pose if <see cref="PlayGesture"/> had to fall back to one. The fade is
        /// KrisLocomotion's to run, since this state is about to be gone. A swing that follows at
        /// once fades it straight back in within the same frame.
        /// </summary>
        protected void ReleaseGesture(float fade = 0.15f)
        {
            ReleasePose();
            if (string.IsNullOrEmpty(ownedGesture)) return;

            Animator animator = GetKrisAnimator();
            var locomotion = GetComponent<KrisLocomotion>();
            if (animator == null || locomotion == null) return;

            int layer = animator.GetLayerIndex(KrisAnimations.GestureLayer);
            if (layer < 0) return;

            AnimatorStateInfo info = animator.IsInTransition(layer)
                ? animator.GetNextAnimatorStateInfo(layer)
                : animator.GetCurrentAnimatorStateInfo(layer);
            if (info.shortNameHash != Animator.StringToHash(ownedGesture)) return;

            locomotion.FadeGesture(layer, 0f, fade);
        }

        /// <summary>
        /// Scales a pose that is a real animation rather than a held frame - the slashes, whose
        /// state runs for SlashDuration / attackSpeedStat. The controller drives those states
        /// off this parameter and defaults it to 1, so an older bundle simply plays at 1x.
        /// </summary>
        protected void SetPoseSpeed(float speed)
        {
            Animator animator = GetKrisAnimator();
            if (animator == null || animator.runtimeAnimatorController == null) return;

            // ⚠ Set blind, never looked up in Animator.parameterCount / GetParameter first: that list
            // can come back empty on an animator that is running its controller (see KrisLocomotion),
            // and the lookup then silently pinned every slash to 1x. An old bundle without the
            // parameter costs a Unity warning, nothing more.
            animator.SetFloat(SpeedParameterHash, speed);
        }

        private static readonly int SpeedParameterHash = Animator.StringToHash(KrisAnimations.SpeedParameter);

        /// <summary>
        /// The animator that actually drives Kris: the one on the model instance inside mdlKris,
        /// not the holder ModelLocator points at (it deliberately has none - KrisRig.StripDonor).
        /// While Kris is still wearing Loader, this is Loader's animator, and the poses below
        /// simply are not found and are reported once each.
        /// </summary>
        protected Animator GetKrisAnimator()
        {
            if (cachedAnimator != null) return cachedAnimator;

            Transform model = GetModelTransform();
            if (model == null) return null;

            Transform own = KrisModelEnforcer.FindCustom(model);
            cachedAnimator = own != null ? own.GetComponentInChildren<Animator>(true)
                                         : GetModelAnimator();
            return cachedAnimator;
        }

        private Animator cachedAnimator;

        /// <summary>
        /// Back to the idle pose unconditionally. Prefer <see cref="ReleasePose"/> when ending a
        /// state: this takes the pose out from under a skill running on the other machine.
        /// </summary>
        protected void PlayIdle(float crossfade = 0.12f)
        {
            PlayPose(KrisAnimations.Idle, crossfade);
        }

        /// <summary>The point Kris's SOUL leaves from: the chest, or the body's core without a model.</summary>
        protected Vector3 SoulOrigin()
        {
            Transform chest = FindModelChild("Chest");
            return chest != null ? chest.position : (characterBody != null ? characterBody.corePosition : transform.position);
        }
    }

    /// <summary>
    /// Kris's animator state names, kept in one place.
    ///
    /// ⚠ This list is written twice: here, and in the state table of the Unity editor script
    /// (KrisUnityProject/Assets/Scripts/Editor/KrisSetup.cs). A name that exists on one side
    /// only is not an error anywhere - PlayPose reports it once and falls back. Change both.
    /// </summary>
    internal static class KrisAnimations
    {
        internal const string BaseLayer = "Base Layer";

        /// <summary>
        /// The upper-body override layer the slashes play on, over the legs' run (KrisSetup's
        /// GestureLayer, which also lists the states it holds).
        /// </summary>
        internal const string GestureLayer = "Gesture";

        /// <summary>
        /// The whole-body layer above it holding <see cref="SoulBrace"/> (KrisSetup.BuildBraceLayer),
        /// weighted by KrisLocomotion by how still Kris stands while the SOUL is aimed.
        /// </summary>
        internal const string BraceLayer = "Brace";

        /// <summary>Name of Kris's model instance inside the model object (mdlKris).</summary>
        internal const string ModelObject = "KrisCustomModel";

        /// <summary>
        /// Idle, run and sprint: Mercenary's own locomotion retargeted onto Kris, as one state of
        /// blend trees driven by <see cref="MoveXParameter"/>, <see cref="MoveZParameter"/>,
        /// <see cref="SprintParameter"/> and <see cref="LocoSpeedParameter"/> (KrisSetup.BuildIdleState).
        /// </summary>
        internal const string Idle = "Idle";

        /// <summary>
        /// The character-select entrance: played once when Kris is picked. Arms already crossed and
        /// back to the camera, Kris looks off into the wind for two seconds, then back over their
        /// shoulder. The controller hands over to <see cref="Select"/> by itself when it finishes
        /// (KrisSetup.ChainSelectIntro).
        /// </summary>
        internal const string SelectIntro = "SelectIntro";

        /// <summary>
        /// The select-screen loop the entrance lands in: arms crossed, back mostly to the camera,
        /// looking back over the shoulder, the cape blown out to one side. No weapons -
        /// <see cref="ShowsSword"/> hides them.
        /// </summary>
        internal const string Select = "Select";

        /// <summary>Two alternating sword swings, stepped by the primary.</summary>
        internal const string SlashA = "SlashA";
        internal const string SlashB = "SlashB";

        /// <summary>Planted behind the raised shield, held while DEFEND is.</summary>
        internal const string Defend = "Defend";

        /// <summary>The author's "Utility Aim", held while aiming: hand at the chest, ready to take the SOUL out.</summary>
        internal const string SoulAim = "SoulAim";

        /// <summary>
        /// The author's Utility Aim whole-body, braced legs and all - the Brace layer's only state,
        /// worn over <see cref="SoulAim"/> while Kris aims standing still.
        /// </summary>
        internal const string SoulBrace = "SoulBrace";

        /// <summary>The SOUL ripped out of the chest and thrown, from <see cref="SoulAim"/>; played once.</summary>
        internal const string SoulThrow = "SoulThrow";

        /// <summary>Flying along the tether to the SOUL, reaching to catch it ("CATCH_SOUL"; SoulSlash starts from it).</summary>
        internal const string SoulPull = "SoulPull";

        /// <summary>
        /// The SOUL caught at the end of the pull: a forward somersault, the sword cutting one
        /// vertical circle round Kris. Runs at attack speed, like the slashes.
        /// </summary>
        internal const string SoulSlash = "SoulSlash";

        /// <summary>Standing still with the ACT menu open: the author's "ACT 2", held.</summary>
        internal const string Act = "Act";

        /// <summary>
        /// An ACT once picked: the menu's pose flicked into the author's "ACT 2 END", then held.
        /// Its first frame is <see cref="Act"/>, so the menu hands over to it without a pop.
        /// </summary>
        internal const string ActEnd = "ActEnd";

        /// <summary>X-SLASH's two crossing cuts.</summary>
        internal const string XSlash = "XSlash";

        /// <summary>
        /// The author's PIROUETTE, held while PirouetteAct turns Kris: on one toe, swordless, the
        /// cape and hair flung out against the spin.
        /// </summary>
        internal const string Pirouette = "Pirouette";

        /// <summary>Float parameter the slash states take their playback rate from.</summary>
        internal const string SpeedParameter = "SlashSpeed";

        /// <summary>
        /// Idle's float parameters, all written by KrisLocomotion, and the same four names as
        /// KrisSetup's MoveXParam/MoveZParam/SprintParam/LocoSpeedParam: the move in Kris's own
        /// frame (-1..1, right and forward), sprinting (0..1), and the state's playback rate.
        /// </summary>
        internal const string MoveXParameter = "MoveX";
        internal const string MoveZParameter = "MoveZ";
        internal const string SprintParameter = "Sprint";
        internal const string LocoSpeedParameter = "LocoSpeed";

        /// <summary>
        /// Mercenary's jump (from liftoff, easing into the rising air pose) and his air pose, a blend
        /// of rising and falling on <see cref="AirUpParameter"/> (0 falling hard, 1 rising). Neither
        /// is a skill's pose: KrisLocomotion plays them, and only ever in place of
        /// <see cref="Idle"/> - so a skill takes Idle, Jump or Air alike and gives back Idle, and
        /// KrisLocomotion puts them back in the air if they still are. Same names as KrisSetup's
        /// BuildAirStates.
        /// </summary>
        internal const string Jump = "Jump";
        internal const string Air = "Air";
        internal const string AirUpParameter = "AirUp";

        /// <summary>
        /// The weapons' renderer names, as export_fbx leaves them: one mesh each, kept whole by
        /// the split by material. Matched as prefixes. Rename a weapon mesh in the .blend and it
        /// stops matching - it is then simply always shown.
        /// </summary>
        internal const string SwordMesh = "mesh_krissword";
        internal const string ShieldMesh = "mesh_krisshield";

        /// <summary>
        /// Whether a pose shows the sword: every pose but the character-select screen's, where it
        /// is never seen, and PIROUETTE's, whose free hands are posed without it.
        ///
        /// ⚠ The previews mirror this and <see cref="ShowsShield"/> in posetool.SHOWS, so a
        /// render draws only what the game will. Change one, change the other.
        /// </summary>
        internal static bool ShowsSword(string pose)
        {
            return pose != Select && pose != SelectIntro && pose != Pirouette;
        }

        /// <summary>
        /// Whether a pose shows the shield: DEFEND's, and nothing else. An allowlist, so a pose
        /// added later is shieldless until it says otherwise.
        /// </summary>
        internal static bool ShowsShield(string pose)
        {
            return pose == Defend;
        }

        private static readonly System.Collections.Generic.HashSet<string> reported =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>Logs a rig complaint once per key, so a missing pose is not spammed.</summary>
        internal static void ReportOnce(string key, string message)
        {
            if (!reported.Add(key)) return;
            Modules.Log.Warning("Kris animator: " + message);
        }
    }
}
