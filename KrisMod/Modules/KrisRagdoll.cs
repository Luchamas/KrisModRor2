using System.Collections.Generic;
using System.Reflection;
using RoR2;
using UnityEngine;

namespace KrisMod.Modules
{
    /// <summary>
    /// Builds a real ragdoll on Kris's own skeleton and hands it to the model object's
    /// <see cref="RagdollController"/> (the one Loader's body came with).
    ///
    /// The FBX ships bones and skinning but no physics, so on death there was nothing to fall:
    /// the controller drove Loader's bones instead, and Kris - who hangs off the model
    /// object rather than off any bone - stayed standing. This adds a rigidbody, a collider and
    /// a joint to eleven of their bones and repoints the controller at them, so RoR2's own death
    /// flow ragdolls Kris. It runs before Loader's skeleton is deleted, which is where it
    /// reads the ragdoll physics layer from.
    ///
    /// Everything is measured off the live transforms rather than hard-coded, because the model
    /// arrives about a hundred times larger than it is in Blender and is then scaled back down
    /// on the rig; lengths written in Blender units would be meaningless here.
    /// </summary>
    internal static class KrisRagdoll
    {
        /// <summary>
        /// RagdollController keeps its animator private, and it is the field that matters: the
        /// controller switches it off as it wakes the bones, and without that the animator keeps
        /// writing the pose over the physics every frame and the corpse never moves.
        /// </summary>
        private static readonly FieldInfo AnimatorField =
            typeof(RagdollController).GetField("animator",
                                               BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// One physics bone. <see cref="Tip"/> is the bone the capsule reaches toward - it sets
        /// the length, the radius and the joint's twist axis - and <see cref="Parent"/> is the
        /// physics bone this one hangs from. The limits are degrees.
        /// </summary>
        private struct BoneSpec
        {
            public string Name;
            public string Tip;
            public string Parent;
            public float Mass;
            public float RadiusFactor;
            public float Twist;
            public float Swing1;
            public float Swing2;

            public BoneSpec(string name, string tip, string parent, float mass, float radiusFactor,
                            float twist, float swing1, float swing2)
            {
                Name = name; Tip = tip; Parent = parent; Mass = mass; RadiusFactor = radiusFactor;
                Twist = twist; Swing1 = swing1; Swing2 = swing2;
            }
        }

        // Eleven bodies: the standard humanoid ragdoll. Hands, feet, fingers, neck, shoulders and
        // the whole cape stay skinned and simply follow whichever body they hang under - they are
        // small enough that giving them physics buys nothing but jitter.
        // Elbows and knees get a wide swing on one axis and almost none on the other, so they
        // hinge instead of bending sideways.
        private static readonly BoneSpec[] Skeleton =
        {
            new BoneSpec("pelvis",    "spine2",    null,      8f,   0.32f,  0f,  0f,  0f),
            new BoneSpec("spine2",    "neck",      "pelvis",  8f,   0.45f, 20f, 30f, 25f),
            // The head has no bone past it, so it is measured off the neck instead - hence the
            // much larger factor: the neck is roughly half the size of the skull it carries.
            new BoneSpec("head",      null,        "spine2",  3f,   1.00f, 25f, 30f, 25f),

            new BoneSpec("arm.L",     "forearm.L", "spine2",  2f,   0.22f, 25f, 60f, 45f),
            new BoneSpec("arm.R",     "forearm.R", "spine2",  2f,   0.22f, 25f, 60f, 45f),
            new BoneSpec("forearm.L", "hand.L",    "arm.L",   1.5f, 0.20f, 15f, 70f,  5f),
            new BoneSpec("forearm.R", "hand.R",    "arm.R",   1.5f, 0.20f, 15f, 70f,  5f),

            new BoneSpec("thigh.L",   "legs.L",     "pelvis",  4f,   0.22f, 15f, 55f, 35f),
            new BoneSpec("thigh.R",   "legs.R",     "pelvis",  4f,   0.22f, 15f, 55f, 35f),
            new BoneSpec("legs.L",     "heel.L",    "thigh.L", 3f,   0.18f, 10f, 60f,  5f),
            new BoneSpec("legs.R",     "heel.R",    "thigh.R", 3f,   0.18f, 10f, 60f,  5f),
        };

        /// <summary>
        /// Adds physics to <paramref name="model"/> and repoints <paramref name="rig"/>'s ragdoll
        /// at it. Returns false if the model is not what we expect - Kris then has no ragdoll,
        /// and their corpse holds its last pose.
        /// </summary>
        internal static bool Build(Transform rig, Transform model)
        {
            if (rig == null || model == null) return false;

            var controller = rig.GetComponent<RagdollController>();
            if (controller == null)
            {
                Log.Warning("The rig has no RagdollController - Kris cannot be given a ragdoll.");
                return false;
            }

            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (AnimatorField == null || animator == null)
            {
                // Handing over the bones without handing over the animator would leave the pose
                // fighting the physics, which looks worse than not ragdolling at all.
                Log.Warning("Ragdoll: could not reach RagdollController.animator" +
                            (animator == null ? " (and the model has no Animator)" : "") +
                            " - Kris will have no ragdoll.");
                return false;
            }

            Dictionary<string, Transform> bones = MapBones(model);
            var built = new List<Transform>();
            var bodies = new Dictionary<string, Rigidbody>();

            foreach (BoneSpec spec in Skeleton)
            {
                Transform bone;
                if (!bones.TryGetValue(spec.Name, out bone))
                {
                    Log.Warning("Ragdoll: the model has no bone '" + spec.Name + "'.");
                    return false;
                }

                // The tip is what gives the limb its length and direction. The head has no bone
                // past it, so it measures back to its own parent instead and gets a sphere.
                Transform tip = null;
                if (spec.Tip != null) bones.TryGetValue(spec.Tip, out tip);

                Vector3 localTip = tip != null
                    ? bone.InverseTransformPoint(tip.position)
                    : -bone.InverseTransformPoint(bone.parent.position);

                if (localTip.magnitude < 1e-5f)
                {
                    Log.Warning("Ragdoll: bone '" + spec.Name + "' has no length to measure.");
                    return false;
                }

                AddCollider(bone, localTip, spec, tip != null);

                var body = bone.gameObject.AddComponent<Rigidbody>();
                body.mass = spec.Mass;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.isKinematic = true;      // the controller wakes them on death
                // Neighbouring capsules necessarily overlap on a body this stocky. Left
                // unclamped, PhysX resolves that overlap by hurling them apart on the first
                // frame of the ragdoll, which reads as the corpse exploding.
                body.maxDepenetrationVelocity = 3f;
                bodies[spec.Name] = body;

                built.Add(bone);
            }

            // Joints come second: a CharacterJoint needs its parent's Rigidbody to already exist.
            foreach (BoneSpec spec in Skeleton)
            {
                if (spec.Parent == null) continue;

                Transform bone = bones[spec.Name];
                Transform tip = null;
                if (spec.Tip != null) bones.TryGetValue(spec.Tip, out tip);

                Vector3 direction = (tip != null
                    ? bone.InverseTransformPoint(tip.position)
                    : -bone.InverseTransformPoint(bone.parent.position)).normalized;

                var joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = bodies[spec.Parent];
                joint.anchor = Vector3.zero;              // bone pivots are already at the joint
                joint.axis = direction;                   // twist runs along the limb
                joint.swingAxis = Perpendicular(direction);
                joint.enablePreprocessing = false;        // preprocessing snaps stretched joints

                joint.lowTwistLimit = new SoftJointLimit { limit = -spec.Twist };
                joint.highTwistLimit = new SoftJointLimit { limit = spec.Twist };
                joint.swing1Limit = new SoftJointLimit { limit = spec.Swing1 };
                joint.swing2Limit = new SoftJointLimit { limit = spec.Swing2 };
            }

            // Corpses live on their own physics layer so they do not shove players around or
            // collide with the character controller. Take whichever one the borrowed rig used.
            int layer = RagdollLayer(controller, rig.gameObject.layer);
            foreach (Transform bone in built) bone.gameObject.layer = layer;

            controller.bones = built.ToArray();

            AnimatorField.SetValue(controller, animator);

            Log.Info("Ragdoll built on Kris's own skeleton: " + built.Count +
                     " bones, layer " + layer + ", animator '" + animator.name + "'.");
            return true;
        }

        /// <summary>
        /// Reads the layer off the bones the rig was going to ragdoll, before they are replaced.
        /// </summary>
        private static int RagdollLayer(RagdollController controller, int fallback)
        {
            if (controller.bones == null) return fallback;

            foreach (Transform bone in controller.bones)
            {
                if (bone != null) return bone.gameObject.layer;
            }

            return fallback;
        }

        /// <summary>
        /// A capsule down the length of the bone, or a sphere for the head. Sizes are in the
        /// bone's own local space, which is what colliders expect - the scale on the way up to
        /// the rig then applies itself.
        /// </summary>
        private static void AddCollider(Transform bone, Vector3 localTip, BoneSpec spec, bool hasTip)
        {
            float length = localTip.magnitude;
            float radius = length * spec.RadiusFactor;

            if (!hasTip)
            {
                // Sat a little past the pivot so the ball covers the skull rather than the neck.
                var sphere = bone.gameObject.AddComponent<SphereCollider>();
                sphere.radius = radius;
                sphere.center = localTip.normalized * (length * 1.1f);
                return;
            }

            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            // Bones point down one local axis; whichever dominates is the capsule's axis.
            Vector3 abs = new Vector3(Mathf.Abs(localTip.x), Mathf.Abs(localTip.y), Mathf.Abs(localTip.z));
            capsule.direction = abs.x >= abs.y && abs.x >= abs.z ? 0 : (abs.y >= abs.z ? 1 : 2);
            capsule.center = localTip * 0.5f;
            // Unity folds the two hemispheres into the height, so a short bone would otherwise
            // come out as a sphere pinched at both ends.
            capsule.height = Mathf.Max(length, radius * 2.1f);
        }

        /// <summary>Any unit vector at right angles to <paramref name="direction"/>.</summary>
        private static Vector3 Perpendicular(Vector3 direction)
        {
            Vector3 axis = Vector3.Cross(direction, Vector3.up);
            if (axis.sqrMagnitude < 0.01f) axis = Vector3.Cross(direction, Vector3.forward);
            return axis.normalized;
        }

        /// <summary>Every transform under the model, by name - the bones are plain transforms.</summary>
        private static Dictionary<string, Transform> MapBones(Transform model)
        {
            var map = new Dictionary<string, Transform>();
            foreach (Transform transform in model.GetComponentsInChildren<Transform>(true))
            {
                if (!map.ContainsKey(transform.name)) map[transform.name] = transform;
            }
            return map;
        }
    }
}
