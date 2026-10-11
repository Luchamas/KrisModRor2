using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// Starts Kris's pose, keeps their renderers switched on and registered, and shows or hides
    /// the sword and shield for the pose that is playing.
    ///
    /// Nothing else starts the custom model's animator, and the game switches renderers back on
    /// (or, from a donor skin, off) when it refreshes the model. Skin application happens in Start
    /// with no ordering guarantee against this component and can run again on a skin change, so
    /// this re-checks for a few frames and then keeps a cheap watch rather than assuming it won.
    ///
    /// The weapons work the way Flowery's cape does: the pose asks (<see cref="WantWeapons"/>,
    /// from BaseKrisState.PlayPose), and the answer is re-applied every frame. (Kris's cape needs
    /// none of it - it is skinned to its own bones in the main mesh and follows every pose.)
    /// </summary>
    public class KrisModelEnforcer : MonoBehaviour
    {
        /// <summary>
        /// The animator state this model idles in. Nothing else drives Kris's animator, so
        /// whatever is set here is what they do when no skill is running: Idle on a body,
        /// Select on the character-select mannequin.
        /// </summary>
        public string startPose = SkillStates.KrisAnimations.Idle;

        /// <summary>
        /// What to play if <see cref="startPose"/> is not a state this bundle's controller has.
        /// Null means there is nothing to fall back to and the wanted pose is played regardless.
        ///
        /// This exists for exactly one failure, and it is a silent one: Animator.Play on a state
        /// the controller does not contain does nothing and logs nothing, leaving the animator in
        /// its DEFAULT state. So an asset bundle built before a pose existed does not fall back
        /// to a sensible pose, it falls back to whatever the controller's default happens to be.
        /// </summary>
        public string fallbackPose;

        private CharacterModel characterModel;
        private Transform custom;

        /// <summary>
        /// How long a pose that shows a weapon must hold before the weapon actually appears.
        /// Hiding is instant; only showing waits.
        ///
        /// The renderer is a switch with no crossfade to hide behind, while the pose fades in over
        /// KrisDefend's 0.08s crossfade. Shown at once, the shield would pop into Kris's idle hand,
        /// down at their side, and only then rise into the guard. Held back as long as the fade, it
        /// appears in the raised hand. (Flowery's cape waits for a different reason - its swings
        /// hand back to the flight pose between punches - and no Kris pose does that to a weapon:
        /// the sword is out in every pose a skill plays, and DEFEND is one held pose.)
        /// </summary>
        private const float WeaponShowDelay = 0.08f;

        /// <summary>One weapon this component shows and hides per pose - see WantWeapons.</summary>
        private sealed class Weapon
        {
            internal readonly string mesh;

            /// <summary>What the pose on the model asks for. Set through WantWeapons.</summary>
            internal bool wanted = true;

            /// <summary>How long <see cref="wanted"/> has been true without interruption.</summary>
            internal float wantedFor = WeaponShowDelay;

            /// <summary>
            /// Whether it is off right now - the settled answer, not the request. The sweep in
            /// Apply treats a disabled renderer of Kris's as a mistake and switches it back on,
            /// so it has to be told this one is off on purpose.
            /// </summary>
            internal bool hidden;

            /// <summary>Its renderers, cached so the per-frame assert costs nothing.</summary>
            internal Renderer[] renderers;

            internal Weapon(string mesh) { this.mesh = mesh; }

            internal bool Owns(Renderer r) => r.name.StartsWith(mesh);
        }

        private readonly Weapon sword = new Weapon(SkillStates.KrisAnimations.SwordMesh);
        private readonly Weapon shield = new Weapon(SkillStates.KrisAnimations.ShieldMesh);

        private CharacterModel.RendererInfo[] ourInfos;
        private int framesLeft = 10;
        private float recheckTimer;

        private void Start()
        {
            characterModel = GetComponent<CharacterModel>();
            custom = FindCustom(transform);

            // The item display rules are still Loader's, with offsets authored for Loader's bones
            // (see KrisBody.ConfigureItemDisplays). Clearing the rule set when the prefab is
            // built has not always survived spawn, so it is re-asserted here.
            if (characterModel != null && !Modules.KrisTuning.ShowItemDisplays &&
                characterModel.itemDisplayRuleSet != null)
            {
                characterModel.itemDisplayRuleSet = null;
                Modules.Log.Info("Item displays cleared on the live model.");
            }

            if (custom == null)
            {
                Modules.Log.Warning("KrisModelEnforcer: no '" +
                    SkillStates.KrisAnimations.ModelObject + "' under the model; nothing to enforce.");
                enabled = false;
                return;
            }

            ourInfos = BuildInfos(custom.gameObject);
            RefreshWeaponRenderers();

            string pose = startPose;
            var anim = custom.GetComponentInChildren<Animator>(true);
            if (anim == null)
            {
                Modules.Log.Error("KrisModelEnforcer: the custom model has NO Animator - " +
                                  "it can only ever show its bind pose.");
            }
            else
            {
                Modules.Log.Info("Kris animator: enabled=" + anim.enabled +
                                 " controller=" + (anim.runtimeAnimatorController != null
                                     ? anim.runtimeAnimatorController.name : "NULL") +
                                 " avatar=" + (anim.avatar != null ? anim.avatar.name : "NULL") +
                                 " valid=" + (anim.avatar != null && anim.avatar.isValid) +
                                 " layers=" + anim.layerCount +
                                 " culling=" + anim.cullingMode);
                if (anim.runtimeAnimatorController != null)
                {
                    var names = new List<string>();
                    foreach (var c in anim.runtimeAnimatorController.animationClips)
                    {
                        if (c != null && !names.Contains(c.name)) names.Add(c.name);
                    }
                    Modules.Log.Info("Kris clips: " + string.Join(", ", names.ToArray()));
                }
                // Nothing else drives this animator, so start it on this model's idle pose.
                anim.enabled = true;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.applyRootMotion = false;
                pose = ResolvePose(anim);
                anim.Play(pose, 0);
            }

            // This pose is started here, not through PlayPose, so nothing else will tell the
            // weapons what it is - and the select-screen mannequin never runs a skill at all, so
            // this is the only call it ever gets. (Flowery's cape got away without one because its
            // default, shown, happened to be right for the select screen. The sword's is not.)
            WantWeapons(SkillStates.KrisAnimations.ShowsSword(pose),
                        SkillStates.KrisAnimations.ShowsShield(pose));
            Apply();
        }

        /// <summary>
        /// The pose to actually start on: <see cref="startPose"/> if this controller has it,
        /// <see cref="fallbackPose"/> if it does not. See fallbackPose for why this is checked
        /// rather than assumed.
        /// </summary>
        private string ResolvePose(Animator anim)
        {
            if (string.IsNullOrEmpty(fallbackPose) || fallbackPose == startPose) return startPose;

            int layer = anim.GetLayerIndex(SkillStates.KrisAnimations.BaseLayer);
            if (layer < 0) layer = 0;
            if (anim.HasState(layer, Animator.StringToHash(startPose))) return startPose;

            // Reported through the animator's own once-per-pose reporter rather than logged
            // straight: the select screen rebuilds this mannequin every time the highlighted
            // survivor changes, so a raw warning here is one line per click on Kris.
            SkillStates.KrisAnimations.ReportOnce(startPose,
                "no pose '" + startPose + "' - wearing '" + fallbackPose + "' instead. Re-export " +
                "the FBX and re-run Kris/Set Up Kris to get it.");
            return fallbackPose;
        }

        /// <summary>
        /// Whether the game has hidden them outright - the stage-entry delay before the teleporter
        /// effect prints them in (KrisSpawnState), and anything else that raises the count.
        /// CharacterModel switches renderers off for that and only switches them back on when the
        /// count drops, so the sweep here must not turn anything on while it is up.
        /// </summary>
        private bool Invisible => characterModel != null && characterModel.invisibilityCount > 0;

        /// <summary>
        /// Asks for the sword and shield on behalf of whatever pose is being played. Cheap and
        /// idempotent - it is called on every pose change, not every frame.
        /// </summary>
        internal void WantWeapons(bool showSword, bool showShield)
        {
            Want(sword, showSword);
            Want(shield, showShield);
        }

        private void Want(Weapon weapon, bool visible)
        {
            if (weapon.wanted == visible) return;
            weapon.wanted = visible;
            weapon.wantedFor = 0f;
            // Hiding is not allowed to wait a frame: DEFEND ending must take the shield with it.
            if (!visible) ApplyWeapon(weapon, true);
        }

        /// <summary>
        /// Brings a weapon out once a pose that shows it has held for <see cref="WeaponShowDelay"/>,
        /// and re-asserts the result EVERY frame, not just when the pose changes.
        ///
        /// The weapons are registered in CharacterModel.baseRendererInfos like every other part of
        /// Kris, and the game switches those back on whenever it refreshes the model - landing a hit
        /// is one such refresh. The sweep in <see cref="Apply"/> fights that only once a second, so
        /// anything that re-enabled a hidden weapon would get up to a second of it on screen: that is
        /// how Flowery's cape came back on punches that connected and not on ones that missed.
        /// Asserting at frame rate closes the window without having to know which refresh did it.
        /// </summary>
        private void UpdateWeapon(Weapon weapon)
        {
            if (!weapon.wanted)
            {
                weapon.wantedFor = 0f;
            }
            else if (weapon.wantedFor < WeaponShowDelay)
            {
                weapon.wantedFor += Time.deltaTime;
            }

            ApplyWeapon(weapon, !weapon.wanted || weapon.wantedFor < WeaponShowDelay);
        }

        /// <summary>Switches a weapon's renderers now and records it for the sweep in Apply.</summary>
        private void ApplyWeapon(Weapon weapon, bool hidden)
        {
            weapon.hidden = hidden;
            if (weapon.renderers == null) return;

            bool on = !hidden && !Invisible;
            for (int i = 0; i < weapon.renderers.Length; i++)
            {
                Renderer r = weapon.renderers[i];
                if (r == null) { RefreshWeaponRenderers(); return; }   // model was rebuilt under us
                if (r.enabled != on) r.enabled = on;
            }
        }

        private void RefreshWeaponRenderers()
        {
            foreach (Weapon weapon in new[] { sword, shield })
            {
                if (custom == null) { weapon.renderers = null; continue; }

                var found = new List<Renderer>();
                foreach (Renderer r in custom.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r is ParticleSystemRenderer || !weapon.Owns(r)) continue;
                    found.Add(r);
                }
                weapon.renderers = found.ToArray();

                // A weapon mesh renamed in the .blend stops matching and is then simply always
                // shown, with nothing else to say so - so say so.
                if (found.Count == 0)
                {
                    SkillStates.KrisAnimations.ReportOnce("weapon:" + weapon.mesh, "no renderer named '" +
                        weapon.mesh + "*' - it cannot be shown or hidden per pose");
                }
            }
        }

        /// <summary>Whether this renderer belongs to a weapon that is off on purpose.</summary>
        private bool HiddenWeapon(Renderer r)
        {
            return (sword.hidden && sword.Owns(r)) || (shield.hidden && shield.Owns(r));
        }

        private void Update()
        {
            UpdateWeapon(sword);
            UpdateWeapon(shield);

            // The skin can be applied after us, so keep asserting for a few frames...
            if (framesLeft > 0)
            {
                framesLeft--;
                Apply();
                return;
            }

            // ...then settle into an occasional check, which also covers a later skin change.
            recheckTimer -= Time.deltaTime;
            if (recheckTimer > 0f) return;
            recheckTimer = 1f;
            Apply();
        }

        private void Apply()
        {
            if (custom == null) return;

            bool invisible = Invisible;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer) continue;

                bool isOurs = r.transform.IsChildOf(custom);
                bool want = isOurs && !invisible && !HiddenWeapon(r);
                if (r.enabled != want) r.enabled = want;
            }

            // Keep their renderers in the CharacterModel so overlays, elite materials and cloaking
            // act on them. Their skin sets the same renderers, so this is a formality now - but a
            // cheap one, and it keeps a later skin that forgets one from losing it silently.
            if (characterModel != null && ourInfos != null &&
                characterModel.baseRendererInfos != ourInfos)
            {
                characterModel.baseRendererInfos = ourInfos;
            }
        }

        /// <summary>
        /// Kris's model instance under a model object: normally its direct child (mdlKris),
        /// searched for more widely only as a fallback.
        /// </summary>
        internal static Transform FindCustom(Transform modelTransform)
        {
            string want = SkillStates.KrisAnimations.ModelObject;
            Transform direct = modelTransform.Find(want);
            if (direct != null) return direct;

            Transform parent = modelTransform.parent;
            if (parent != null)
            {
                Transform sibling = parent.Find(want);
                if (sibling != null) return sibling;
            }

            foreach (Transform t in modelTransform.root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == want) return t;
            }
            return null;
        }

        private static CharacterModel.RendererInfo[] BuildInfos(GameObject model)
        {
            var infos = new List<CharacterModel.RendererInfo>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (r.sharedMaterials != null && r.sharedMaterials.Length > 1)
                {
                    Modules.Log.Warning("Renderer '" + r.name + "' has " + r.sharedMaterials.Length +
                        " materials; CharacterModel will drop all but the first. Split the mesh " +
                        "by material in export_fbx.py.");
                }
                infos.Add(new CharacterModel.RendererInfo
                {
                    renderer = r,
                    defaultMaterial = r.sharedMaterial,
                    defaultShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                    ignoreOverlays = false,
                    hideOnDeath = false,
                });
            }
            return infos.ToArray();
        }
    }
}
