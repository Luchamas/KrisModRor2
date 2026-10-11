using System.Collections.Generic;
using System.Reflection;
using RoR2;
using UnityEngine;

namespace KrisMod.Modules
{
    /// <summary>
    /// Moves everything on the borrowed rig that is tied to a bone onto Kris's own skeleton.
    ///
    /// The rig object stays - it carries the HurtBoxGroup, CharacterModel, RagdollController and
    /// the rest, and those only hold references - but what they reference was Loader's skeleton,
    /// frozen upright underneath them. Read out of the game on Flowery (the mod this rig code was
    /// written for, and Kris inherited), that meant:
    ///
    /// - Loader's main hurtbox is a fixed capsule on the rig, spanning body height -0.89 to 1.63.
    ///   Kris's own pose puts their toes near 0 and their head near 1.75. So the capsule
    ///   stopped at their chest, their head could not be hit at all, and nothing followed their lean.
    /// - The body's core - the point shields, burns and every other effect around a character
    ///   are centred on - is Loader's chest bone, set through CharacterBody.overrideCoreTransform.
    ///   Frozen, that sits 0.85 up the body, around Kris's thighs. That is why the Rose Buckler
    ///   shield trailed around their legs. The override is now cleared, so the core is the centre
    ///   of their own body hurtbox (see CentreCoreOnHurtBox).
    /// - Loader's weak point (the Railgunner sniper target) sat on Kris's chest.
    /// - The ChildLocator named Loader's bones, so anything asking for "Head" or "HandL" got a
    ///   point on the frozen skeleton.
    ///
    /// Everything is measured off Kris's bones as they stand in the prefab, the same way
    /// KrisRagdoll measures them, so none of it depends on the scale the model arrives at.
    ///
    /// Once nothing points at Loader's skeleton any more, <see cref="StripDonor"/> deletes it -
    /// skeleton, meshes, animator, skins and all - and what was Loader's model object becomes
    /// mdlKris: a holder for Kris's model and the components the game looks for on a model.
    /// </summary>
    internal static class KrisRig
    {
        private static readonly FieldInfo PairsField =
            typeof(ChildLocator).GetField("transformPairs", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Loader's ChildLocator names, and the bone of Kris's that plays the same part. The
        /// Mech* entries are Loader's gauntlets; Kris's own arms are the nearest thing to them.
        /// Swing anchors hung off Loader's hips for their punch trails; their chest is where Kris's
        /// punches come from. HeadCenter is not a bone on either skeleton - it is built.
        /// </summary>
        private static readonly Dictionary<string, string> ChildBones = new Dictionary<string, string>
        {
            { "Base", "pelvis" },          { "Pelvis", "pelvis" },
            { "Stomach", "spine1" },       { "Chest", "spine2" },
            { "Neck", "neck" },            { "Head", "head" },
            { "HeadCenter", HeadCenterName },

            { "ClavicleL", "shoulder.L" }, { "ClavicleR", "shoulder.R" },
            { "UpperArmL", "arm.L" },      { "UpperArmR", "arm.R" },
            { "LowerArmL", "forearm.L" },  { "LowerArmR", "forearm.R" },
            { "HandL", "hand.L" },         { "HandR", "hand.R" },

            { "ThighL", "thigh.L" },       { "ThighR", "thigh.R" },
            { "CalfL", "legs.L" },          { "CalfR", "legs.R" },
            { "FootL", "feet.L" },         { "FootR", "feet.R" },

            { "MechBase", "spine2" },
            { "MechUpperArmL", "arm.L" },  { "MechUpperArmR", "arm.R" },
            { "MechLowerArmL", "forearm.L" }, { "MechLowerArmR", "forearm.R" },
            { "MechHandL", "hand.L" },     { "MechHandR", "hand.R" },
            { "MechHandRight", "hand.R" }, { "MuzzleLeft", "hand.L" },
            { "MechFinger23R", "middle.R.003" }, { "MechFinger33R", "ring.R.003" },

            { "SwingLeft", "spine2" },     { "SwingRight", "spine2" },
        };

        private const string HeadCenterName = "HeadCenter";

        /// <summary>
        /// Loader's bones that the ChildLocator named, and the bone of Kris's each became.
        /// Filled while repointing the ChildLocator and used by <see cref="StripDonor"/>: a vanilla
        /// component still holding one of those bones gets Kris's instead of nothing.
        /// </summary>
        private static readonly Dictionary<Transform, Transform> DonorBones = new Dictionary<Transform, Transform>();

        /// <summary>
        /// How much wider than their shoulders the body hurtbox is. Shoulder pivots sit inside the
        /// arms; half again covers the arms themselves and the coat.
        /// </summary>
        private const float BodyRadiusFactor = 1.5f;

        /// <summary>
        /// The head sphere's radius as a multiple of the neck-to-head length. It comes out at
        /// 0.21m, which is Loader's weak point to within a centimetre - so Railgunner finds
        /// Kris's head no harder or easier to hit than any other survivor's.
        /// </summary>
        private const float HeadRadiusFactor = 1.5f;

        internal static void MoveOntoModel(Transform body, Transform rig, Transform custom)
        {
            if (body == null || rig == null || custom == null) return;

            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in custom.GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            }

            Transform pelvis, neck, head, armL, armR;
            if (!bones.TryGetValue("pelvis", out pelvis) || !bones.TryGetValue("neck", out neck) ||
                !bones.TryGetValue("head", out head) || !bones.TryGetValue("arm.L", out armL) ||
                !bones.TryGetValue("arm.R", out armR))
            {
                Log.Warning("Rig: the model is missing pelvis, neck, head or an arm - hurtboxes and " +
                            "attachment points stay on the borrowed skeleton.");
                return;
            }

            // The head is the one part with no bone past it, so its centre is projected out along
            // the neck, exactly as the ragdoll's head collider is.
            Vector3 neckToHead = head.position - neck.position;
            float headLength = neckToHead.magnitude;
            Vector3 headCentre = head.position + neckToHead.normalized * (headLength * 1.1f);
            float headRadius = headLength * HeadRadiusFactor;

            var headCentreObject = new GameObject(HeadCenterName);
            headCentreObject.transform.SetParent(head, false);
            headCentreObject.transform.position = headCentre;
            bones[HeadCenterName] = headCentreObject.transform;

            float bodyRadius = Vector3.Distance(armL.position, armR.position) * 0.5f * BodyRadiusFactor;
            Vector3 centreLocal;
            float height;
            MeasureUpright(body, bones, pelvis, headCentre, headRadius, bodyRadius, out centreLocal, out height);

            MoveHurtBoxes(body, rig, pelvis, head, headCentre, headRadius, bodyRadius, centreLocal, height);
            FrameLogbook(body, rig, centreLocal, height);
            RepointChildLocator(rig, custom, bones);
            RemoveDonorMachinery(rig, custom);
        }

        /// <summary>
        /// Kris stood upright, in body space: <paramref name="height"/> from their lowest foot to
        /// the top of their head, and <paramref name="centreLocal"/> the middle of that, straight
        /// over their pelvis. It is the shape of their body hurtbox, and what the logbook frames.
        /// </summary>
        private static void MeasureUpright(Transform body, Dictionary<string, Transform> bones, Transform pelvis,
                                           Vector3 headCentre, float headRadius, float bodyRadius,
                                           out Vector3 centreLocal, out float height)
        {
            float lowest = float.MaxValue;
            foreach (string foot in new[] { "toes.L", "toes.R", "feet.L", "feet.R", "heel.L", "heel.R" })
            {
                Transform t;
                if (bones.TryGetValue(foot, out t)) lowest = Mathf.Min(lowest, body.InverseTransformPoint(t.position).y);
            }
            if (lowest == float.MaxValue) lowest = body.InverseTransformPoint(pelvis.position).y - 1f;

            // Stood upright in body space, not along the pelvis bone: the pose they are exported in
            // leans, and a capsule tilted with it would miss whichever end it leaned away from.
            // It reaches the top of their head, not its centre, so that the middle of the capsule is
            // the middle of them - that point is where shields are centred (CentreCoreOnHurtBox).
            //
            // Its axis stands straight over their pelvis. It used to sit halfway between the pelvis
            // and the head, but the pose they are exported in carries their head 0.4m forward of their
            // hips, and the capsule keeps whatever offset it is given from the pelvis it hangs
            // on. In the poses they actually flies in their head is over their torso, so that offset
            // left the shield bubble centred in front of them and them standing at the back of it.
            Vector3 pelvisLocal = body.InverseTransformPoint(pelvis.position);
            Vector3 headLocal = body.InverseTransformPoint(headCentre);
            float top = headLocal.y + headRadius;
            height = Mathf.Max(top - lowest, bodyRadius * 2.1f);
            centreLocal = new Vector3(pelvisLocal.x, (lowest + top) * 0.5f, pelvisLocal.z);
        }

        /// <summary>
        /// The body hurtbox becomes one capsule from their lowest foot to the top of their head,
        /// hung off their pelvis so it leans, bobs and dashes with them. The weak point goes on their
        /// head. The HurtBox components themselves are kept, not rebuilt: their group, index,
        /// bullseye and sniper flags and physics layer are all already right.
        ///
        /// The capsule's centre is also their core - see <see cref="CentreCoreOnHurtBox"/>.
        /// </summary>
        private static void MoveHurtBoxes(Transform body, Transform rig, Transform pelvis, Transform head,
                                          Vector3 headCentre, float headRadius, float bodyRadius,
                                          Vector3 centreLocal, float height)
        {
            // The character-select mannequin has none, and has no need of any.
            var group = rig.GetComponent<HurtBoxGroup>();
            if (group == null || group.hurtBoxes == null) return;

            CentreCoreOnHurtBox(body);

            int moved = 0;
            foreach (HurtBox hurtBox in group.hurtBoxes)
            {
                if (hurtBox == null) continue;

                bool isHead = hurtBox.isSniperTarget && hurtBox != group.mainHurtBox;
                Transform t = hurtBox.transform;

                t.SetParent(isHead ? head : pelvis, false);
                t.position = isHead ? headCentre : body.TransformPoint(centreLocal);
                t.rotation = body.rotation;
                // Undo whatever scale the bone chain carries, so the sizes below are in metres.
                t.localScale = Vector3.one;
                t.localScale = Vector3.one / Mathf.Max(0.0001f, t.lossyScale.x);

                // Resized in place, never replaced: HurtBox requires a Collider, so Unity refuses
                // to destroy the one it has, and a second one added beside it is not the one
                // HurtBox picks up.
                Collider collider = t.GetComponent<Collider>();

                if (isHead)
                {
                    var sphere = collider as SphereCollider;
                    if (sphere == null)
                    {
                        Log.Warning("Rig: weak point '" + t.name + "' is not a sphere - moved, not resized.");
                        moved++;
                        continue;
                    }
                    sphere.center = Vector3.zero;
                    sphere.radius = headRadius;
                    Log.Info("Rig: weak point '" + t.name + "' on Kris's head, radius " +
                             headRadius.ToString("0.00") + "m.");
                }
                else
                {
                    var capsule = collider as CapsuleCollider;
                    if (capsule == null)
                    {
                        Log.Warning("Rig: hurtbox '" + t.name + "' is not a capsule - moved, not resized.");
                        moved++;
                        continue;
                    }
                    capsule.center = Vector3.zero;
                    capsule.direction = 1;
                    capsule.radius = bodyRadius;
                    capsule.height = height;
                    Log.Info("Rig: hurtbox '" + t.name + "'" + (hurtBox == group.mainHurtBox ? " (main)" : "") +
                             " on Kris's pelvis, " + height.ToString("0.00") + "m tall, radius " +
                             bodyRadius.ToString("0.00") + "m, centred " + centreLocal.y.ToString("0.00") +
                             "m up the body.");
                }

                moved++;
            }

            if (moved != group.hurtBoxes.Length)
            {
                Log.Warning("Rig: moved " + moved + " of " + group.hurtBoxes.Length + " hurtboxes.");
            }
        }

        /// <summary>
        /// Centres the body's core - the point shields, burns and every other effect around a
        /// character are centred on - on the middle of Kris.
        ///
        /// CharacterBody takes its core from the main hurtbox unless overrideCoreTransform is set,
        /// and Loader's body sets it to their chest bone. Pointing that at Kris's chest instead
        /// was tried, and in game the shield bubble rode visibly high - clear above their hair, short
        /// of their knees: a chest is the middle of Loader, who is mostly torso, but not of Kris,
        /// who is mostly legs. So the override is cleared and the core falls back to the main
        /// hurtbox, whose capsule spans them from their feet to the top of their head and hangs off their
        /// pelvis. Cleared before StripDonor runs, so its sweep has nothing here to remap.
        /// </summary>
        private static void CentreCoreOnHurtBox(Transform body)
        {
            var characterBody = body.GetComponent<CharacterBody>();
            if (characterBody == null || characterBody.overrideCoreTransform == null) return;

            Log.Info("Rig: core override cleared (was Loader's '" + characterBody.overrideCoreTransform.name +
                     "') - Kris's core is the centre of their body hurtbox.");
            characterBody.overrideCoreTransform = null;
        }

        /// <summary>
        /// The logbook camera's vertical field of view, in degrees - measured, not read.
        ///
        /// It is not in code: it comes out of the logbook panel's and the camera prefab's
        /// serialized values. ModelPanel's own default is 60, and framing for 60 left Kris
        /// small in the panel. Measured in game instead: Loader's camera, 0.90m from its target,
        /// showed about 1.7m of Kris top to bottom (shoulders to ankles), which is 2 atan(0.94)
        /// = 86 degrees. Taken as 85.
        /// </summary>
        private const float LogbookFov = 85f;

        /// <summary>
        /// How much taller than Kris the logbook shot is. 1.25 leaves about 10% of the panel
        /// clear above their head and below their feet, which also absorbs their hover, the camera's
        /// tilt, and the measuring error in <see cref="LogbookFov"/>.
        /// </summary>
        private const float LogbookMargin = 1.25f;

        /// <summary>
        /// Aims the logbook camera at all of Kris.
        ///
        /// The logbook frames a survivor entirely from ModelPanelParameters on the model object:
        /// the camera goes on cameraPositionTransform and looks at focusPointTransform
        /// (ModelPanel.CameraFramingCalculator; it only guesses from bones and bounds when the
        /// component is missing). StripDonor keeps Loader's two points, LogbookTarget and
        /// LogbookCamera, since the component throws without them - but they were placed for
        /// Loader: at Loader's height in the body, and at a distance chosen for Loader. Loader's
        /// hurtbox runs from -0.89 to 1.63 in body space and Kris's from about 0 to 2.2, so on
        /// Kris that was a close-up of their hips, with their head and feet out of the panel.
        ///
        /// Loader's viewing angle is kept; the point and the distance become Kris's. Loader's
        /// zoom range is scaled by the same factor as the distance, so scrolling feels the same
        /// as on every other entry.
        /// </summary>
        private static void FrameLogbook(Transform body, Transform rig, Vector3 centreLocal, float height)
        {
            var panel = rig.GetComponent<ModelPanelParameters>();
            if (panel == null) return;

            Vector3 target = body.TransformPoint(centreLocal);
            float distance = height * LogbookMargin * 0.5f / Mathf.Tan(LogbookFov * 0.5f * Mathf.Deg2Rad);

            Vector3 direction = body.forward;
            float oldDistance = 0f;
            if (panel.focusPointTransform != null && panel.cameraPositionTransform != null)
            {
                Vector3 offset = panel.cameraPositionTransform.position - panel.focusPointTransform.position;
                oldDistance = offset.magnitude;
                if (oldDistance > 0.01f) direction = offset / oldDistance;
            }

            if (oldDistance > 0.01f)
            {
                float scale = distance / oldDistance;
                panel.minDistance *= scale;
                panel.maxDistance *= scale;
            }
            else
            {
                panel.minDistance = distance * 0.5f;
                panel.maxDistance = distance * 2f;
            }

            if (panel.focusPointTransform == null) panel.focusPointTransform = NewPoint(rig, "LogbookTarget");
            if (panel.cameraPositionTransform == null) panel.cameraPositionTransform = NewPoint(rig, "LogbookCamera");

            panel.focusPointTransform.position = target;
            panel.cameraPositionTransform.position = target + direction * distance;

            Log.Info("Rig: logbook camera " + distance.ToString("0.00") + "m from the middle of Kris's " +
                     height.ToString("0.00") + "m (Loader's was " + oldDistance.ToString("0.00") + "m), zoom " +
                     panel.minDistance.ToString("0.00") + "-" + panel.maxDistance.ToString("0.00") + "m.");
        }

        private static Transform NewPoint(Transform rig, string name)
        {
            var point = new GameObject(name).transform;
            point.SetParent(rig, false);
            return point;
        }

        /// <summary>
        /// Points every ChildLocator name at Kris's matching bone. Names the table does not
        /// know are reported if they sat on the borrowed skeleton, and left alone if they hang
        /// straight off the rig - those are fixed points like MuzzleCenter, not bones.
        /// </summary>
        private static void RepointChildLocator(Transform rig, Transform custom, Dictionary<string, Transform> bones)
        {
            var locator = rig.GetComponent<ChildLocator>();
            if (locator == null || PairsField == null) return;

            var pairs = (ChildLocator.NameTransformPair[])PairsField.GetValue(locator);
            if (pairs == null) return;

            int repointed = 0;
            var missed = new List<string>();

            DonorBones.Clear();

            for (int i = 0; i < pairs.Length; i++)
            {
                string boneName;
                Transform bone;
                if (ChildBones.TryGetValue(pairs[i].name, out boneName) && bones.TryGetValue(boneName, out bone))
                {
                    if (pairs[i].transform != null && !pairs[i].transform.IsChildOf(custom))
                    {
                        DonorBones[pairs[i].transform] = bone;
                    }
                    pairs[i].transform = bone;
                    repointed++;
                }
                else if (pairs[i].transform != null && !pairs[i].transform.IsChildOf(custom) &&
                         pairs[i].transform.parent != rig)
                {
                    missed.Add(pairs[i].name);
                }
            }

            PairsField.SetValue(locator, pairs);

            Log.Info("Rig: " + repointed + " ChildLocator entries now point at Kris's bones.");
            if (missed.Count > 0)
            {
                Log.Warning("Rig: no Kris bone for " + string.Join(", ", missed.ToArray()) +
                            " - still on the borrowed skeleton.");
            }
        }

        /// <summary>
        /// Loader's aim layers, foot IK and sprint particles all work on the frozen skeleton and
        /// nothing of Kris's. The ragdoll's disable-on-death list is scrubbed afterwards:
        /// RagdollController does not null-check it, so a destroyed entry would throw as they die.
        /// The rest of Loader's model object goes in <see cref="StripDonor"/>.
        /// </summary>
        private static void RemoveDonorMachinery(Transform rig, Transform custom)
        {
            var removed = new List<string>();

            // Dependents before what they depend on: IKSetWeightFromAnimatorFloat drives the IK.
            // Matched by name because the IK lives in GenericIK.dll, which the mod does not reference.
            foreach (string typeName in new[] { "IKSetWeightFromAnimatorFloat", "InverseKinematics" })
            {
                foreach (MonoBehaviour behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour.transform.IsChildOf(custom)) continue;
                    if (behaviour.GetType().Name != typeName) continue;
                    removed.Add(typeName);
                    Object.DestroyImmediate(behaviour);
                }
            }

            foreach (SprintEffectController sprint in rig.GetComponentsInChildren<SprintEffectController>(true))
            {
                if (sprint.transform.IsChildOf(custom)) continue;
                if (sprint.loopRootObject != null && !sprint.loopRootObject.transform.IsChildOf(custom))
                {
                    sprint.loopRootObject.SetActive(false);
                }
                removed.Add("SprintEffectController");
                Object.DestroyImmediate(sprint);
            }

            foreach (AimAnimator aim in rig.GetComponentsInChildren<AimAnimator>(true))
            {
                if (aim.transform.IsChildOf(custom)) continue;
                removed.Add("AimAnimator");
                Object.DestroyImmediate(aim);
            }

            var ragdoll = rig.GetComponent<RagdollController>();
            if (ragdoll != null && ragdoll.componentsToDisableOnRagdoll != null)
            {
                var kept = new List<MonoBehaviour>();
                foreach (MonoBehaviour behaviour in ragdoll.componentsToDisableOnRagdoll)
                {
                    if (behaviour != null) kept.Add(behaviour);
                }
                ragdoll.componentsToDisableOnRagdoll = kept.ToArray();
            }

            Log.Info("Rig: removed the borrowed " + (removed.Count > 0 ? string.Join(", ", removed.ToArray()) : "nothing") + ".");
        }

        /// <summary>
        /// Deletes what is left of Loader's model object and leaves a holder for Kris.
        ///
        /// Kept: Kris's model, the components the game looks for on a model (CharacterModel,
        /// HurtBoxGroup, ChildLocator, RagdollController, ModelPanelParameters, Kris's own
        /// hitbox group), and the few transforms those point at that are not their bones -
        /// MuzzleCenter, the punch hitbox, the logbook camera points. Deleted: Loader's skeleton
        /// and meshes, their Animator, their ModelSkinController and skins, their FootstepHandler and
        /// their fist hitboxes.
        ///
        /// The Animator is the one that matters most. With it gone the model object has no
        /// Animator at all - Kris's lives on their model, a level down - so every vanilla state
        /// that plays Loader's animations (hurt, stun, freeze, pod exit, death) finds nothing to
        /// play them on and skips it, instead of throwing Loader's state and parameter names at
        /// Kris's controller and logging a warning for each one.
        ///
        /// Before anything is destroyed, every serialized reference anywhere on
        /// <paramref name="prefabRoot"/> that points into it is cleared and logged, so a vanilla
        /// component that held onto a Loader bone is visible in the log rather than a
        /// MissingReferenceException the first time it runs.
        /// </summary>
        internal static void StripDonor(Transform prefabRoot, Transform rig, Transform custom, string holderName)
        {
            if (prefabRoot == null || rig == null || custom == null) return;

            // Transforms that stay, wherever they currently are under the rig.
            var keep = new List<Transform> { custom };

            var hitBoxGroups = rig.GetComponents<HitBoxGroup>();
            foreach (HitBoxGroup group in hitBoxGroups)
            {
                if (!IsOurs(group) || group.hitBoxes == null) continue;
                foreach (HitBox hitBox in group.hitBoxes)
                {
                    if (hitBox != null) keep.Add(hitBox.transform);
                }
            }

            var locator = rig.GetComponent<ChildLocator>();
            var pairs = locator != null && PairsField != null
                ? (ChildLocator.NameTransformPair[])PairsField.GetValue(locator)
                : null;
            if (pairs != null)
            {
                foreach (ChildLocator.NameTransformPair pair in pairs)
                {
                    if (pair.transform != null) keep.Add(pair.transform);
                }
            }

            foreach (ModelPanelParameters panel in prefabRoot.GetComponentsInChildren<ModelPanelParameters>(true))
            {
                if (panel.focusPointTransform != null) keep.Add(panel.focusPointTransform);
                if (panel.cameraPositionTransform != null) keep.Add(panel.cameraPositionTransform);
            }

            // Anything kept that sits inside something about to be deleted is lifted onto the
            // holder first, holding its place in the world.
            foreach (Transform t in keep)
            {
                if (t == custom || !t.IsChildOf(rig) || t.IsChildOf(custom) || t.parent == rig) continue;
                t.SetParent(rig, true);
            }

            var doomedObjects = new List<Transform>();
            foreach (Transform child in rig)
            {
                if (!keep.Contains(child)) doomedObjects.Add(child);
            }

            var doomedComponents = new List<Component>();
            foreach (FootstepHandler footsteps in rig.GetComponents<FootstepHandler>()) doomedComponents.Add(footsteps);
            foreach (HitBoxGroup group in hitBoxGroups)
            {
                if (!IsOurs(group)) doomedComponents.Add(group);
            }
            foreach (ModelSkinController skins in rig.GetComponents<ModelSkinController>()) doomedComponents.Add(skins);
            // Last: nothing above may still be holding it when it goes.
            foreach (Animator animator in rig.GetComponents<Animator>()) doomedComponents.Add(animator);

            ClearReferencesInto(prefabRoot, doomedObjects, doomedComponents);

            var removed = new List<string>();
            foreach (Component component in doomedComponents)
            {
                removed.Add(component.GetType().Name);
                Object.DestroyImmediate(component);
            }
            foreach (Transform child in doomedObjects)
            {
                removed.Add(child.name);
                Object.DestroyImmediate(child.gameObject);
            }

            // Loader's model object carried Loader's scale (1.2) and, on the mannequin, a turn.
            // Neither means anything for a holder, so both go - with every child held in place, so
            // nothing in the world moves.
            var children = new List<Transform>();
            foreach (Transform child in rig) children.Add(child);
            foreach (Transform child in children) child.SetParent(rig.parent, true);
            rig.localRotation = Quaternion.identity;
            rig.localScale = Vector3.one;
            foreach (Transform child in children) child.SetParent(rig, true);

            rig.name = holderName;

            Log.Info("Rig: '" + holderName + "' is Kris's now. Removed from Loader's model object: " +
                     string.Join(", ", removed.ToArray()) + ".");
        }

        private static bool IsOurs(HitBoxGroup group)
        {
            return group != null && (group.groupName == SkillStates.KrisSlash.HitBoxGroupName ||
                                     group.groupName == SkillStates.SoulSlash.HitBoxGroupName);
        }

        /// <summary>
        /// Fixes every serialized field on <paramref name="prefabRoot"/> that refers to something
        /// about to be destroyed. A Loader bone the ChildLocator mapped becomes Kris's matching
        /// bone; anything else is cleared - a single reference is nulled, an array or list loses
        /// the entry. Arrays of structs are looked inside one level, and lose any entry holding
        /// something doomed: that is how CharacterModel.baseLightInfos lists Loader's lights, and
        /// left alone it threw from CharacterModel.UpdateLights every frame.
        /// </summary>
        private static void ClearReferencesInto(Transform prefabRoot, List<Transform> doomedObjects,
                                                List<Component> doomedComponents)
        {
            var sweep = new Sweep { objects = doomedObjects, components = doomedComponents };

            foreach (Component component in prefabRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;
                Object ignored;
                if (sweep.Doomed(component, typeof(Component), out ignored)) continue;

                for (System.Type type = component.GetType();
                     type != null && type != typeof(MonoBehaviour) && type != typeof(Behaviour) &&
                     type != typeof(Component);
                     type = type.BaseType)
                {
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!IsSerialized(field)) continue;
                        FixField(component, field, sweep);
                    }
                }
            }
        }

        private static bool IsSerialized(FieldInfo field)
        {
            return field.IsPublic || field.IsDefined(typeof(SerializeField), false);
        }

        /// <summary>What is being destroyed, and what a reference to it should become.</summary>
        private sealed class Sweep
        {
            internal List<Transform> objects;
            internal List<Component> components;

            /// <summary>
            /// True if <paramref name="value"/> is on its way out. <paramref name="replacement"/> is
            /// then Kris's matching bone, as a <paramref name="wanted"/>, or null for none.
            /// </summary>
            internal bool Doomed(Object value, System.Type wanted, out Object replacement)
            {
                replacement = null;
                if (value == null) return false;

                Transform t = value is GameObject go ? go.transform : (value as Component)?.transform;
                if (t == null) return false;

                bool gone = value is Component c && components.Contains(c);
                for (int i = 0; !gone && i < objects.Count; i++) gone = t.IsChildOf(objects[i]);
                if (!gone) return false;

                Transform bone;
                if (DonorBones.TryGetValue(t, out bone))
                {
                    if (value is Transform && wanted.IsAssignableFrom(typeof(Transform))) replacement = bone;
                    else if (value is GameObject && wanted.IsAssignableFrom(typeof(GameObject))) replacement = bone.gameObject;
                }
                return true;
            }
        }

        private static void FixField(Component owner, FieldInfo field, Sweep sweep)
        {
            System.Type fieldType = field.FieldType;
            string where = owner.GetType().Name + "." + field.Name + " on '" + owner.name + "'";
            Object replacement;

            if (typeof(Object).IsAssignableFrom(fieldType))
            {
                var value = field.GetValue(owner) as Object;
                if (!sweep.Doomed(value, fieldType, out replacement)) return;
                field.SetValue(owner, replacement);
                Log.Info("Rig: " + (replacement != null
                    ? "pointed " + where + " at Kris's '" + replacement.name + "'"
                    : "cleared " + where) + " (was Loader's '" + value.name + "').");
                return;
            }

            if (fieldType.IsArray)
            {
                System.Type element = fieldType.GetElementType();
                bool ofObjects = typeof(Object).IsAssignableFrom(element);
                bool ofStructs = element.IsValueType && !element.IsPrimitive && !element.IsEnum;
                if (!ofObjects && !ofStructs) return;

                var array = field.GetValue(owner) as System.Array;
                if (array == null) return;

                var kept = new List<object>();
                int repointed = 0;
                foreach (object entry in array)
                {
                    bool drop;
                    object fixedEntry = ofObjects
                        ? FixEntry(entry as Object, element, sweep, ref repointed, out drop)
                        : FixStruct(entry, sweep, ref repointed, out drop);
                    if (!drop) kept.Add(fixedEntry);
                }
                if (kept.Count == array.Length && repointed == 0) return;

                System.Array rebuilt = System.Array.CreateInstance(element, kept.Count);
                for (int i = 0; i < kept.Count; i++) rebuilt.SetValue(kept[i], i);
                field.SetValue(owner, rebuilt);
                Log.Info("Rig: " + where + ": dropped " + (array.Length - kept.Count) +
                         " of Loader's entries, pointed " + repointed + " at Kris's bones.");
                return;
            }

            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                typeof(Object).IsAssignableFrom(fieldType.GetGenericArguments()[0]))
            {
                var list = field.GetValue(owner) as System.Collections.IList;
                if (list == null) return;

                System.Type element = fieldType.GetGenericArguments()[0];
                int dropped = 0, repointed = 0;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    bool drop;
                    object fixedEntry = FixEntry(list[i] as Object, element, sweep, ref repointed, out drop);
                    if (drop) { list.RemoveAt(i); dropped++; }
                    else if (!ReferenceEquals(fixedEntry, list[i])) list[i] = fixedEntry;
                }
                if (dropped > 0 || repointed > 0)
                {
                    Log.Info("Rig: " + where + ": dropped " + dropped + " of Loader's entries, pointed " +
                             repointed + " at Kris's bones.");
                }
            }
        }

        /// <summary>
        /// The entry as it should be: itself, or Kris's bone in its place. <paramref name="drop"/>
        /// says it is doomed with nothing to stand in. An entry that was already null is kept.
        /// </summary>
        private static object FixEntry(Object entry, System.Type element, Sweep sweep, ref int repointed,
                                       out bool drop)
        {
            drop = false;
            Object replacement;
            if (!sweep.Doomed(entry, element, out replacement)) return entry;
            if (replacement == null) { drop = true; return null; }
            repointed++;
            return replacement;
        }

        /// <summary>
        /// A struct entry with its references fixed. <paramref name="drop"/> is set as soon as one
        /// of its references is doomed with nothing of Kris's to stand in.
        /// </summary>
        private static object FixStruct(object boxed, Sweep sweep, ref int repointed, out bool drop)
        {
            drop = false;
            foreach (FieldInfo field in boxed.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                                   BindingFlags.NonPublic))
            {
                if (!IsSerialized(field) || !typeof(Object).IsAssignableFrom(field.FieldType)) continue;

                Object replacement;
                if (!sweep.Doomed(field.GetValue(boxed) as Object, field.FieldType, out replacement)) continue;
                if (replacement == null) { drop = true; return boxed; }

                field.SetValue(boxed, replacement);
                repointed++;
            }
            return boxed;
        }
    }
}
