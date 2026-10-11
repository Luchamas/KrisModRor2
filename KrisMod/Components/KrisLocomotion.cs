using RoR2;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// Drives Kris's "Idle" state, which is Mercenary's locomotion retargeted onto them: his idle,
    /// his four directional runs and his sprint, blended the way his own animator blends them
    /// (KrisSetup.BuildIdleState builds the trees).
    ///
    /// This component is the only thing that writes their parameters: which way Kris is going
    /// relative to where they face, whether they are sprinting, and how fast the cycle should
    /// play. Being blends inside one state rather than states of their own keeps this out of
    /// every skill's way: it never plays anything, so it can never take a pose off a slash - it
    /// only changes what Idle looks like whenever Idle is showing.
    ///
    /// In the air it does play something - Merc's "Jump" and "Air" - but only ever in place of Idle:
    /// while a skill holds a pose of its own this leaves the animator alone, and when the skill gives
    /// Idle back mid-air, this puts them back into Air.
    ///
    /// It runs on every client and reads the body's velocity, facing, grounding and sprint flag,
    /// so nothing is sent. ⚠ Of those, the velocity is NOT replicated: CharacterNetworkTransform
    /// hands a remote body its position, aim, stick and grounding, and never touches
    /// CharacterMotor.velocity, which sits at whatever it last was (zero, on a body that has
    /// never had authority here). Read straight off the motor, every other player's Kris stood
    /// in Idle sliding across the floor, and never jumped. On a body this machine does not
    /// control, the velocity is measured off its position instead (<see cref="TrackRemoteVelocity"/>).
    /// </summary>
    public class KrisLocomotion : MonoBehaviour
    {
        /// <summary>
        /// Speed, as a fraction of the move speed stat, at which they start to run at all.
        ///
        /// Not zero: the motor keeps a few centimetres per second of drift on a body that is
        /// standing still - sliding down a shallow slope, settling after a landing, being pushed
        /// by a teammate - and running into that would have them pedal gently while idling.
        /// </summary>
        private const float MoveFloor = 0.15f;

        /// <summary>
        /// Speed, as a fraction of the move speed stat, at which the run is all the way in.
        ///
        /// Under 1, because the motor accelerates into the stick rather than jumping to it: Merc's
        /// animator is fed the stick itself, which is at 1 the moment it is pushed, and waiting for
        /// the velocity to reach a full 1 would leave Kris half-standing through every start.
        /// </summary>
        private const float MoveCeiling = 0.8f;

        /// <summary>
        /// How long the blend takes to catch up with a change of velocity, in seconds.
        ///
        /// The parameters feed blend trees, which follow them exactly and instantly, so this is
        /// the only smoothing there is. Short enough to answer the stick, long enough that
        /// clipping a wall - which stops the motor dead for a frame or two - does not snap them
        /// upright and back, and that swinging the camera round does not flick the legs from one
        /// direction's run to the other's.
        /// </summary>
        private const float Smoothing = 0.14f;

        /// <summary>
        /// Mercenary's run and sprint state speeds, as multiples of his walkSpeed parameter (which
        /// the game feeds the body's moveSpeed stat, sprint already included). The clips export_fbx
        /// bakes from posetool.merc_frames are his cycles at the length they play at x1 - 0.8 s for
        /// RunF/RunB/Sprint, 0.667 s sideways - so these rates make Kris's feet cover the ground
        /// at exactly his pace. Re-sample a clip at another length and these stop matching: the
        /// feet then skate.
        /// </summary>
        private const float RunRate = 0.18f;
        private const float SprintRate = 0.16f;

        /// <summary>
        /// Merc's Air blend, measured off his animator: (vertical speed, weight of his rising pose).
        /// His animator blends the two on velocity.y through a tree of its own, which is not a straight
        /// line - most of the way to "rising" already at a standstill, so the top of an arc still
        /// looks like the jump. KrisSetup's Air is a plain 0..1 blend, so the curve is applied here.
        /// </summary>
        private static readonly Vector2[] AirCurve =
        {
            new Vector2(-30f, 0f), new Vector2(-20f, 0.26f), new Vector2(-10f, 0.53f), new Vector2(-5f, 0.66f),
            new Vector2(-2f, 0.74f), new Vector2(0f, 0.79f), new Vector2(2f, 0.84f), new Vector2(5f, 0.92f),
            new Vector2(10f, 1f),
        };

        /// <summary>
        /// How long they must be off the ground, without having jumped, before the air pose comes in.
        /// Running down stairs or over a bump unground the motor for a frame or two; Merc's own fade
        /// into Air is slow enough to hide that, a CrossFade restarted on every step would not be.
        /// </summary>
        private const float LedgeGrace = 0.1f;

        /// <summary>Merc's own fades: ~10 frames off a ledge, ~5 on landing, and a jump that cuts straight in.</summary>
        private const float FallFade = 0.17f;
        private const float LandFade = 0.08f;
        private const float JumpFade = 0.05f;

        /// <summary>
        /// Upward speed that counts as a jump when the body leaves the ground. Only the authority
        /// sees jumpCount go up; every other client sees a body leave the ground rising, which is this.
        /// </summary>
        private const float JumpLiftSpeed = 3f;

        /// <summary>
        /// How long after a jump the ground is ignored. The motor still reports grounded for a
        /// physics step after the jump's velocity is set, and that would land them straight out of it.
        /// </summary>
        private const float JumpGroundIgnore = 0.15f;

        /// <summary>
        /// Smoothing on a remote body's measured velocity, in seconds. Its position comes in as
        /// snapshots, interpolated - steady between them, a kink at each - and this only has to
        /// iron out the kinks: the blend's own Smoothing does the rest, and the Air blend and the
        /// jump test want the vertical speed as fresh as it can be.
        /// </summary>
        private const float RemoteSmoothing = 0.05f;

        /// <summary>
        /// How long a remote body's position may hold still before it counts as stopped. Not one
        /// frame: when the snapshots are applied per physics step, a frame rate above the physics
        /// rate sees the same position two or three frames running while the body is moving.
        /// </summary>
        private const float RemoteStillTime = 0.1f;

        /// <summary>
        /// A remote body that "moved" faster than this was teleported - a stage start, a
        /// teleporter, a snapshot catching up after a hitch - and the step is not a velocity.
        /// </summary>
        private const float RemoteTeleportSpeed = 150f;

        private CharacterBody body;
        private CharacterMotor motor;
        private CharacterDirection direction;
        private ModelLocator modelLocator;

        private Animator animator;
        private int moveXParameter = -1;
        private int moveZParameter = -1;
        private int sprintParameter = -1;
        private int locoSpeedParameter = -1;
        private bool resolved;

        private int airUpParameter;
        private int idleState;
        private int jumpState;
        private int airState;
        private bool hasAir;
        private int lastJumpCount;
        private float airTime;
        private float sinceJump = 1f;
        private int skillPoseFrame = -1;

        /// <summary>
        /// Called by BaseKrisState.PlayPose: a skill has just asked for a pose, which the animator has
        /// not picked up yet. Nothing here may CrossFade in this frame.
        /// </summary>
        public void SkillPosed()
        {
            skillPoseFrame = Time.frameCount;
        }

        private int gestureLayer = -1;
        private float gestureTarget;
        private float gestureRate;

        /// <summary>
        /// Ramps the Gesture layer's weight to <paramref name="target"/> over <paramref name="seconds"/>.
        /// Run from here rather than by the skill, because the fade out outlives the skill's state.
        /// </summary>
        public void FadeGesture(int layer, float target, float seconds)
        {
            gestureLayer = layer;
            gestureTarget = target;
            gestureRate = seconds > 0.001f ? 1f / seconds : float.PositiveInfinity;
        }

        private void UpdateGesture(float dt)
        {
            if (gestureLayer < 0 || gestureLayer >= animator.layerCount) return;
            float weight = animator.GetLayerWeight(gestureLayer);
            if (weight == gestureTarget) return;
            animator.SetLayerWeight(gestureLayer, Mathf.MoveTowards(weight, gestureTarget, gestureRate * dt));
        }

        /// <summary>
        /// How long the SOUL aim's braced stance takes to come in or go, on being asked for and on
        /// landing or taking off. The stillness itself rides on the smoothed move, which already eases.
        /// </summary>
        private const float BraceFade = 0.1f;

        private int braceLayer = -1;
        private bool braceWanted;
        private float braceOn;
        private float braceGrounded;

        /// <summary>
        /// Called by SoulAim: while on, the Brace layer (the author's whole-body Utility Aim) is
        /// weighted by how still Kris stands - all of it standing, none of it at a run or in the
        /// air, where the Gesture layer's planted SoulAim rides over the legs instead.
        /// </summary>
        public void Brace(bool on)
        {
            braceWanted = on;
        }

        private void UpdateBrace(float dt)
        {
            if (braceLayer < 0 || braceLayer >= animator.layerCount) return;
            float step = dt / BraceFade;
            braceOn = Mathf.MoveTowards(braceOn, braceWanted ? 1f : 0f, step);
            bool grounded = motor == null || motor.isGrounded;
            braceGrounded = Mathf.MoveTowards(braceGrounded, grounded ? 1f : 0f, step);
            float still = 1f - Mathf.Clamp01(move.magnitude);
            animator.SetLayerWeight(braceLayer, braceOn * braceGrounded * still);
        }

        private Vector2 move;
        private Vector2 moveVelocity;
        private float sprint;
        private float sprintVelocity;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            motor = GetComponent<CharacterMotor>();
            direction = GetComponent<CharacterDirection>();
            modelLocator = GetComponent<ModelLocator>();
        }

        private Vector3 remoteVelocity;
        private Vector3 remoteRaw;
        private Vector3 lastPosition;
        private bool hasLastPosition;
        private float sinceMoved;

        /// <summary>
        /// The body's velocity: the motor's own where this machine moves the body, measured off its
        /// position everywhere else, where the motor's is never written (see the class summary).
        /// </summary>
        private Vector3 Velocity
        {
            get
            {
                if (motor == null) return Vector3.zero;
                return body == null || body.hasEffectiveAuthority ? motor.velocity : remoteVelocity;
            }
        }

        /// <summary>
        /// Measures a remote body's velocity off its position: each step it moves, over the time since
        /// it last moved - not over one frame, since the position may only change once per physics
        /// step - then lightly smoothed. Runs every frame whether or not the animator is up yet, so
        /// the estimate is warm by the time it is.
        /// </summary>
        private void TrackRemoteVelocity(float dt)
        {
            if (body == null || body.hasEffectiveAuthority)
            {
                hasLastPosition = false;
                return;
            }

            Vector3 position = transform.position;
            if (!hasLastPosition)
            {
                lastPosition = position;
                hasLastPosition = true;
                sinceMoved = 0f;
                remoteRaw = remoteVelocity = Vector3.zero;
                return;
            }

            sinceMoved += dt;
            Vector3 step = position - lastPosition;
            if (step.sqrMagnitude > 1e-8f)
            {
                Vector3 raw = step / Mathf.Max(sinceMoved, 1e-4f);
                if (raw.sqrMagnitude < RemoteTeleportSpeed * RemoteTeleportSpeed) remoteRaw = raw;
                lastPosition = position;
                sinceMoved = 0f;
            }
            else if (sinceMoved > RemoteStillTime)
            {
                remoteRaw = Vector3.zero;
            }

            remoteVelocity = Vector3.Lerp(remoteVelocity, remoteRaw, 1f - Mathf.Exp(-dt / RemoteSmoothing));
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            TrackRemoteVelocity(dt);

            if (!ResolveAnimator()) return;
            if (!animator.isActiveAndEnabled) return;

            move = Vector2.SmoothDamp(move, WantedMove(), ref moveVelocity, Smoothing, Mathf.Infinity, dt);
            float wantedSprint = body != null && body.isSprinting && move.sqrMagnitude > 0.01f ? 1f : 0f;
            sprint = Mathf.SmoothDamp(sprint, wantedSprint, ref sprintVelocity, Smoothing, Mathf.Infinity, dt);

            // One rate for the whole state - a blend tree runs every child on one normalised clock -
            // so it goes back to 1 as they stop, or Merc's 2 s breath would play at a runner's pace.
            float walk = body != null ? Mathf.Max(0.01f, body.moveSpeed) : 7f;
            float runRate = Mathf.Lerp(RunRate, SprintRate, sprint) * walk;
            float rate = Mathf.Lerp(1f, runRate, Mathf.Clamp01(move.magnitude));

            animator.SetFloat(moveXParameter, move.x);
            animator.SetFloat(moveZParameter, move.y);
            animator.SetFloat(sprintParameter, sprint);
            animator.SetFloat(locoSpeedParameter, rate);

            UpdateAir(dt);
            UpdateGesture(dt);
            UpdateBrace(dt);
        }

        /// <summary>
        /// Merc's jump and air: Jump on a jump, Air when they leave the ground any other way, back to
        /// Idle on landing - each only while Idle, Jump or Air is the pose showing (or the one being
        /// faded into), so a skill's pose is never taken away.
        /// </summary>
        private void UpdateAir(float dt)
        {
            if (!hasAir || motor == null) return;

            bool grounded = motor.isGrounded;
            float upSpeed = Velocity.y;
            sinceJump += dt;
            airTime = grounded ? 0f : airTime + dt;

            // Authority: jumpCount goes up on every jump, double jumps included. Everyone else: the body
            // left the ground rising. Not both for one jump - the first one starts sinceJump.
            // ⚠ The count alone is not a jump: DEFEND writes it to maxJumpCount every tick to spend
            // the jumps (KrisDefend.HoldGround), and reading that as a jump played Jump over the
            // shield. A real jump sets the vertical speed in the same step it counts, so both are asked.
            // ⚠ "Left the ground" is any frame within LedgeGrace of it, not the one frame it happened:
            // a remote body's grounding arrives in its snapshot while its speed is measured off the
            // position after, so on the frame it lifts off it has not been seen rising yet.
            bool rising = upSpeed > JumpLiftSpeed;
            bool tookOff = !grounded && airTime <= LedgeGrace;
            bool jumped = sinceJump > JumpGroundIgnore && rising &&
                          (motor.jumpCount > lastJumpCount || tookOff);
            lastJumpCount = motor.jumpCount;
            if (jumped) sinceJump = 0f;

            animator.SetFloat(airUpParameter, AirBlend(upSpeed));

            AnimatorStateInfo info = animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0)
                : animator.GetCurrentAnimatorStateInfo(0);
            int showing = info.shortNameHash;
            if (showing != idleState && showing != jumpState && showing != airState) return;

            // ⚠ A skill's CrossFade only shows up in the state info once the animator has updated,
            // which is after this Update - so in the frame a skill poses, Idle still looks like it is
            // showing, and a CrossFade from here (the later call wins) would throw the pose away.
            if (skillPoseFrame == Time.frameCount) return;

            if (jumped)
            {
                animator.CrossFadeInFixedTime(jumpState, JumpFade, 0, 0f);
            }
            else if (grounded && sinceJump > JumpGroundIgnore)
            {
                if (showing != idleState) animator.CrossFadeInFixedTime(idleState, LandFade, 0);
            }
            else if (!grounded && showing == idleState && airTime > LedgeGrace)
            {
                animator.CrossFadeInFixedTime(airState, FallFade, 0);
            }
        }

        /// <summary>Vertical speed to the Air blend, along <see cref="AirCurve"/>.</summary>
        private static float AirBlend(float upSpeed)
        {
            if (upSpeed <= AirCurve[0].x) return AirCurve[0].y;
            for (int i = 1; i < AirCurve.Length; i++)
            {
                if (upSpeed <= AirCurve[i].x)
                    return Mathf.Lerp(AirCurve[i - 1].y, AirCurve[i].y, Mathf.InverseLerp(AirCurve[i - 1].x, AirCurve[i].x, upSpeed));
            }
            return AirCurve[AirCurve.Length - 1].y;
        }

        /// <summary>
        /// Where they are going, in their own frame: x right, y forward, length 0 standing to 1
        /// running - Merc's rightSpeed and forwardSpeed.
        ///
        /// Their own frame is the way the body faces, which the game turns to the aim when they
        /// are not sprinting - so strafing and backing off show the side and back runs, as Merc's
        /// do - and to the move when they are, so the sprint is always forward.
        ///
        /// Horizontal speed only. OMEGA flies in three dimensions and a dash can throw them
        /// straight up; climbing is not running.
        /// </summary>
        private Vector2 WantedMove()
        {
            if (motor == null || body == null) return Vector2.zero;

            Vector3 velocity = Velocity;
            velocity.y = 0f;
            float walk = Mathf.Max(0.01f, body.moveSpeed);
            float amount = Mathf.Clamp01(Mathf.InverseLerp(MoveFloor, MoveCeiling, velocity.magnitude / walk));
            if (amount <= 0f) return Vector2.zero;

            Vector3 forward = Facing();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector2 local = new Vector2(Vector3.Dot(velocity, right), Vector3.Dot(velocity, forward));
            return local.sqrMagnitude > 1e-6f ? local.normalized * amount : Vector2.zero;
        }

        /// <summary>The flat direction the body faces: CharacterDirection's, else the model's.</summary>
        private Vector3 Facing()
        {
            Vector3 forward = direction != null ? direction.forward
                            : modelLocator != null && modelLocator.modelTransform != null ? modelLocator.modelTransform.forward
                            : transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        }

        /// <summary>
        /// Finds Kris's own animator and the four parameters on it, once.
        ///
        /// The model is instantiated at spawn rather than built into the prefab, so this cannot
        /// happen in Awake; and their Animator lives on their model, a child of mdlKris, not on
        /// the model object ModelLocator points at (the same reason BaseKrisState has
        /// GetKrisAnimator). A body wearing the Loader placeholder has no such animator and no
        /// such parameters, and simply never runs.
        /// </summary>
        private bool ResolveAnimator()
        {
            if (animator != null) return resolved;

            Transform model = modelLocator != null ? modelLocator.modelTransform : null;
            if (model == null) return false;

            Transform custom = KrisModelEnforcer.FindCustom(model);
            if (custom == null) return false;

            animator = custom.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                animator = null;
                return false;
            }

            // ⚠ Written by hash, never gated on Animator.parameterCount / GetParameter. Those can
            // report an empty list on an animator that is plainly running its controller (they
            // did, on every spawn, even after isInitialized) - and gating on them switched this
            // off and froze Kris's legs twice. SetFloat on a name the controller really lacks
            // costs a Unity warning and nothing else, so an old bundle still just hovers.
            moveXParameter = Animator.StringToHash(SkillStates.KrisAnimations.MoveXParameter);
            moveZParameter = Animator.StringToHash(SkillStates.KrisAnimations.MoveZParameter);
            sprintParameter = Animator.StringToHash(SkillStates.KrisAnimations.SprintParameter);
            locoSpeedParameter = Animator.StringToHash(SkillStates.KrisAnimations.LocoSpeedParameter);
            airUpParameter = Animator.StringToHash(SkillStates.KrisAnimations.AirUpParameter);
            idleState = Animator.StringToHash(SkillStates.KrisAnimations.Idle);
            jumpState = Animator.StringToHash(SkillStates.KrisAnimations.Jump);
            airState = Animator.StringToHash(SkillStates.KrisAnimations.Air);
            // States, unlike parameters, are asked for: CrossFade to a state the controller lacks is
            // an error every frame, and a bundle from before the jump simply keeps Idle in the air.
            hasAir = animator.HasState(0, jumpState) && animator.HasState(0, airState);
            // -1 on a bundle from before the brace: SoulAim then keeps the planted pose standing too.
            braceLayer = animator.GetLayerIndex(SkillStates.KrisAnimations.BraceLayer);
            resolved = true;

            Modules.Log.Info("Kris locomotion: driving MoveX/MoveZ/Sprint/LocoSpeed on '" + animator.name +
                             "' (controller " + animator.runtimeAnimatorController.name +
                             ", initialized=" + animator.isInitialized + ", " + DescribeParameters() + ")" +
                             (hasAir ? "; Jump/Air on AirUp" : "; no Jump/Air states - Idle in the air"));
            return true;
        }

        /// <summary>What the animator says its parameters are - only ever logged, never trusted.</summary>
        private string DescribeParameters()
        {
            AnimatorControllerParameter[] parameters = animator.parameters;
            if (parameters.Length == 0) return "parameter list empty";
            var names = new System.Text.StringBuilder(parameters.Length + " parameters:");
            foreach (AnimatorControllerParameter p in parameters) names.Append(' ').Append(p.name);
            return names.ToString();
        }
    }
}
