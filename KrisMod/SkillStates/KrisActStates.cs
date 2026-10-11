using EntityStates;
using KrisMod.Components;
using KrisMod.Content;
using KrisMod.Modules;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace KrisMod.SkillStates
{
    /// <summary>
    /// Base for every ACT: Kris stays planted for <see cref="Duration"/> while it plays out.
    /// Runs on the Body machine, entered from <see cref="KrisActMenu"/> once the TP is paid.
    /// </summary>
    public abstract class BaseKrisAct : BaseKrisState, IKrisActState
    {
        /// <summary>How long Kris is held in place for this ACT.</summary>
        protected abstract float Duration { get; }

        /// <summary>
        /// The pose this ACT plays. By default every ACT finishes the menu's gesture (ACT 2 into
        /// ACT 2 END); one with an animation of its own, like X-SLASH's cuts, names that instead.
        /// </summary>
        protected virtual string Pose => KrisAnimations.ActEnd;

        /// <summary>
        /// This ACT's own sound category, or null for none. Every item uses the item sound
        /// whatever this says, so an ACT of <see cref="ActKind.Item"/> need not override it.
        /// </summary>
        protected virtual string Sound => null;

        /// <summary>The sound that follows the chime: the item sound, <see cref="Sound"/>, or null.</summary>
        private string followUp;

        /// <summary>When <see cref="followUp"/> is due, in fixedAge.</summary>
        private float followUpAt;

        /// <summary>
        /// Called by the menu on the authority before any TP is paid, with the state not yet
        /// entered. False refuses the pick - the menu flashes it and charges nothing - for an ACT
        /// that would have nothing to act on, like PROCEED with no enemy in front.
        /// </summary>
        internal virtual bool Prepare(CharacterBody body, Ray aim) => true;

        public override void OnEnter()
        {
            base.OnEnter();
            PlayPose(Pose, 0.08f, KrisAnimations.Act);

            Ray aim = GetAimRay();
            LockFacing(aim.direction);

            // Every ACT chimes on being picked, and its own sound follows once the chime is done.
            // Not both at once: the Windows playback path holds one sound at a time, so the
            // second would cut the chime off before it was heard.
            int chime = Sounds.PickIndex(Sounds.ActSelect);
            Sounds.PlayAt(Sounds.ActSelect, chime, gameObject);

            KrisAct act = KrisActs.For(GetType());
            followUp = act != null && act.Kind == ActKind.Item ? Sounds.ActItem : Sound;
            followUpAt = Sounds.AudibleLength(Sounds.ActSelect, chime);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            if (followUp != null && fixedAge >= followUpAt) PlayFollowUp();

            if (characterMotor != null)
            {
                characterMotor.velocity = new Vector3(0f, Mathf.Min(0f, characterMotor.velocity.y), 0f);
                characterMotor.moveDirection = Vector3.zero;
            }

            if (fixedAge >= Duration && isAuthority) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            // An ACT shorter than the chime still gets its sound.
            if (followUp != null) PlayFollowUp();
            ReleasePose();
            base.OnExit();
        }

        private void PlayFollowUp()
        {
            Sounds.PlayAt(followUp, gameObject);
            followUp = null;
        }

        /// <summary>
        /// A little burst on Kris, so an ACT with no attack still visibly happened. Played
        /// locally by default, as every client runs the state; <paramref name="transmit"/> is for
        /// the server alone, when only it knows what the burst should look like.
        /// </summary>
        protected void Flourish(Color color, bool transmit = false)
        {
            if (characterBody == null) return;
            Burst(characterBody.corePosition, 1.5f, color, transmit);
        }

        protected static void Burst(Vector3 position, float scale, Color color, bool transmit = false)
        {
            if (KrisAssets.BuffEffect == null) return;
            KrisEffects.Spawn(KrisAssets.BuffEffect, new EffectData
            {
                origin = position,
                scale = scale,
                color = color,
            }, transmit);
        }

        /// <summary>
        /// <paramref name="buff"/> on Kris and every living ally within <paramref name="radius"/>.
        /// Server only; buffs replicate.
        /// </summary>
        protected void BuffAllies(BuffDef buff, float duration, float radius)
        {
            if (!NetworkServer.active || buff == null || characterBody == null) return;

            float radiusSqr = radius * radius;
            var members = TeamComponent.GetTeamMembers(GetTeam());
            for (int i = 0; i < members.Count; i++)
            {
                CharacterBody ally = members[i] != null ? members[i].body : null;
                if (ally == null || ally.healthComponent == null || !ally.healthComponent.alive) continue;
                if (ally != characterBody && (ally.corePosition - characterBody.corePosition).sqrMagnitude > radiusSqr) continue;
                ally.AddTimedBuff(buff, duration);
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }

    /// <summary>The healing items: a random share of max health, rolled by the server.</summary>
    public abstract class BaseHealAct : BaseKrisAct
    {
        protected abstract float MinFraction { get; }
        protected abstract float MaxFraction { get; }

        protected override float Duration => 0.6f;

        public override void OnEnter()
        {
            base.OnEnter();
            Flourish(new Color(0.4f, 1f, 0.4f));
            if (NetworkServer.active && healthComponent != null)
            {
                healthComponent.HealFraction(Random.Range(MinFraction, MaxFraction), default(ProcChainMask));
            }
        }
    }

    /// <summary>DARK CANDY: a small heal.</summary>
    public class DarkCandyAct : BaseHealAct
    {
        internal const float MinHeal = 0.15f;
        internal const float MaxHeal = 0.3f;
        protected override float MinFraction => MinHeal;
        protected override float MaxFraction => MaxHeal;
    }

    /// <summary>TV DINNER: a middling heal.</summary>
    public class TvDinnerAct : BaseHealAct
    {
        internal const float MinHeal = 0.3f;
        internal const float MaxHeal = 0.5f;
        protected override float MinFraction => MinHeal;
        protected override float MaxFraction => MaxHeal;
    }

    /// <summary>SCARLIXIR: very nearly a full heal.</summary>
    public class ScarlixirAct : BaseHealAct
    {
        internal const float MinHeal = 0.7f;
        internal const float MaxHeal = 0.8f;
        protected override float MinFraction => MinHeal;
        protected override float MaxFraction => MaxHeal;
    }

    /// <summary>
    /// PIROUETTE: Kris spins, and the server picks what comes of it - one of the other buffs,
    /// a little heal, a debuff, or a stumble that hurts. The burst is the server's, transmitted, since only it knows
    /// whether to tint it for good luck or bad.
    /// </summary>
    public class PirouetteAct : BaseKrisAct
    {
        private const float SpinTime = 0.6f;
        private const float HealFraction = 0.15f;
        private const float StumbleFraction = 0.15f;

        private static readonly Color Lucky = new Color(1f, 0.85f, 0.3f);
        private static readonly Color Unlucky = new Color(0.55f, 0.35f, 0.85f);

        private Vector3 startForward;

        protected override float Duration => 0.8f;
        protected override string Pose => KrisAnimations.Pirouette;
        protected override string Sound => Sounds.Pirouette;

        public override void OnEnter()
        {
            base.OnEnter();

            Vector3 aim = GetAimRay().direction;
            startForward = new Vector3(aim.x, 0f, aim.z);
            if (startForward.sqrMagnitude < 0.001f) startForward = transform.forward;
            startForward.Normalize();

            if (NetworkServer.active) Roll();
        }

        public override void Update()
        {
            base.Update();
            // One full turn, quick out of the gate and settling back where Kris started.
            float t = Mathf.Clamp01(age / SpinTime);
            float eased = 1f - (1f - t) * (1f - t);
            LockFacing(Quaternion.Euler(0f, 360f * eased, 0f) * startForward);
        }

        /// <summary>
        /// The five good outcomes (cases 0-4) each weigh <see cref="GoodWeight"/>, the six bad ones
        /// (5-10) <see cref="BadWeight"/>: a slight lean toward luck, 25 in 49 good overall.
        /// </summary>
        private const int GoodOutcomes = 5;
        private const int BadOutcomes = 6;
        private const int GoodWeight = 5;
        private const int BadWeight = 4;

        private static int PickOutcome()
        {
            int good = GoodOutcomes * GoodWeight;
            int r = Random.Range(0, good + BadOutcomes * BadWeight);
            return r < good ? r / GoodWeight : GoodOutcomes + (r - good) / BadWeight;
        }

        private void Roll()
        {
            switch (PickOutcome())
            {
                case 0: Give(Buffs.Courage, Buffs.Duration, true); break;
                case 1: Give(Buffs.HoldBreath, Buffs.Duration, true); break;
                case 2: Give(Buffs.PetalFeather, Buffs.Duration, true); break;
                case 3: Give(Buffs.Nimble, Buffs.Duration, true); break;
                case 4:
                    if (healthComponent != null) healthComponent.HealFraction(HealFraction, default(ProcChainMask));
                    Flourish(new Color(0.4f, 1f, 0.4f), true);
                    break;
                case 5: Give(Buffs.Dizzy, Buffs.Duration, false); break;
                case 6: Give(Buffs.Sluggish, Buffs.Duration, false); break;
                case 7: Give(Buffs.Clumsy, Buffs.Duration, false); break;
                case 8: Give(Buffs.Exposed, Buffs.Duration, false); break;
                case 9: Give(Buffs.Rooted, Buffs.RootedDuration, false); break;
                default:
                    Stumble();
                    Flourish(Unlucky, true);
                    break;
            }
        }

        /// <summary>
        /// The spin ends in a fall: a flat share of max health, through armor and blocks so it is
        /// the same hurt every time, and never the killing blow - a menu gamble is not a death.
        /// </summary>
        private void Stumble()
        {
            if (healthComponent == null) return;
            healthComponent.TakeDamage(new DamageInfo
            {
                damage = healthComponent.fullCombinedHealth * StumbleFraction,
                position = characterBody.corePosition,
                damageType = DamageType.NonLethal | DamageType.BypassArmor | DamageType.BypassBlock,
                procCoefficient = 0f,
                damageColorIndex = DamageColorIndex.Default,
            });
        }

        private void Give(BuffDef buff, float duration, bool lucky)
        {
            if (buff != null) characterBody.AddTimedBuff(buff, duration);
            Flourish(lucky ? Lucky : Unlucky, true);
        }
    }

    /// <summary>X-SLASH: two crossing cuts in front of Kris.</summary>
    public class XSlashAct : BaseKrisAct
    {
        internal const float DamagePerCut = 5f;

        private const float FirstCut = 0.15f;
        private const float SecondCut = 0.4f;
        private const float Radius = 4.5f;
        private const float Reach = 3f;

        // The smear's arc (Components/SlashArcVfx): it starts once the blade is moving, about 0.05 s
        // before each cut's hit, and its head reaches the end as the blade finishes - frames 6-14 and
        // 21-29 of the 46-frame clip (posetool xslash_frames). Move these with the clip.
        private const float ArcLead = 0.05f;
        private const float ArcReveal = 0.11f;
        // Long enough that the first cut is still fading when the second lands, so the two read as an X.
        private const float ArcFade = 0.35f;
        private const float ArcRadius = 2.2f;
        private const float ArcWidth = 0.8f;
        // The blade's heading in each cut's plane, 0 ahead and 90 straight up the tipped plane: it
        // chambers at 105, a little behind overhead, and finishes at -55, low and ahead. The smear
        // stops short of the low end, because further down it would go into the floor.
        private const float ArcFrom = 100f;
        private const float ArcTo = -45f;

        private int cutsDone;
        private int arcsDone;

        protected override float Duration => 0.75f;
        protected override string Pose => KrisAnimations.XSlash;

        /// <summary>One sound for both cuts. It lands just after the first, once the chime is done.</summary>
        protected override string Sound => Sounds.XSlash;

        public override void FixedUpdate()
        {
            // The planes match the clip (KrisRig/posetool.py SWINGS "XA", "XB"). A minus roll tips
            // the plane's top toward Kris's right, so the first cut comes down from over their right
            // shoulder to low on the left. A plus roll tips it toward their left, so the second comes
            // down from over the left shoulder to low on the right.
            if (arcsDone == 0 && fixedAge >= FirstCut - ArcLead) Smear(-35f);
            if (arcsDone == 1 && fixedAge >= SecondCut - ArcLead) Smear(35f);
            if (cutsDone == 0 && fixedAge >= FirstCut) Cut(KrisAssets.FightHitB);
            if (cutsDone == 1 && fixedAge >= SecondCut) Cut(KrisAssets.FightHitA);
            base.FixedUpdate();
        }

        private Vector3 Forward()
        {
            Ray aim = GetAimRay();
            Vector3 forward = new Vector3(aim.direction.x, 0f, aim.direction.z);
            if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
            return forward.normalized;
        }

        /// <summary>
        /// The blade's crescent, the same one FIGHT draws, but on a diagonal plane instead of a level
        /// one. Every client runs this state, so each builds its own and nothing is networked.
        /// Merc's whirlwind was used here before. It is a full ring, so rolling it changed nothing
        /// you could see, and neither cut looked diagonal.
        /// </summary>
        private void Smear(float roll)
        {
            arcsDone++;

            Vector3 forward = Forward();
            Quaternion rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 0f, roll);
            Vector3 centre = characterBody.corePosition + forward * 1f + Vector3.up * 0.5f;

            SlashArcVfx arc = SlashArcVfx.Spawn(null, centre, rotation, ArcFrom, ArcTo, ArcReveal);
            arc.radius = ArcRadius;
            arc.width = ArcWidth;
            arc.fade = ArcFade;
        }

        /// <param name="mark">The cut mark on each target. Its slant on screen matches the cut:
        /// FightHitB is "/" for the first cut, FightHitA is "\" for the second.</param>
        private void Cut(GameObject mark)
        {
            cutsDone++;

            if (!isAuthority) return;

            Vector3 forward = Forward();
            Vector3 centre = characterBody.corePosition + forward * Reach;

            var blast = new BlastAttack
            {
                attacker = gameObject,
                inflictor = gameObject,
                teamIndex = GetTeam(),
                baseDamage = damageStat * DamagePerCut,
                baseForce = 800f,
                position = centre,
                radius = Radius,
                falloffModel = BlastAttack.FalloffModel.None,
                procCoefficient = 1f,
                crit = RollCrit(),
                damageType = KrisDamage.Of(DamageType.Generic, DamageSource.Special),
                damageColorIndex = DamageColorIndex.Default,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
            };
            // The blast spawns this on every hurtbox it hits, through EffectManager, so other clients see it too.
            if (mark != null) blast.impactEffect = EffectCatalog.FindEffectIndexFromPrefab(mark);

            BlastAttack.Result result = blast.Fire();
            if (tension != null) tension.AddForHits(result.hitCount);
        }
    }

    /// <summary>COURAGE: more armor for Kris and every ally close by.</summary>
    public class CourageAct : BaseKrisAct
    {
        internal const float BuffDuration = Buffs.Duration;
        internal const float Radius = 40f;

        protected override float Duration => 0.6f;
        protected override string Sound => Sounds.ActBasic;

        public override void OnEnter()
        {
            base.OnEnter();
            Flourish(KrisAssets.TpOrange);
            BuffAllies(Buffs.Courage, BuffDuration, Radius);
        }
    }

    /// <summary>HOLD BREATH: faster on foot for a while.</summary>
    public class HoldBreathAct : BaseKrisAct
    {
        internal const float BuffDuration = Buffs.Duration;

        protected override float Duration => 0.5f;
        protected override string Sound => Sounds.ActBasic;

        public override void OnEnter()
        {
            base.OnEnter();
            Flourish(KrisAssets.KrisBlue);
            if (NetworkServer.active && Buffs.HoldBreath != null) characterBody.AddTimedBuff(Buffs.HoldBreath, BuffDuration);
        }
    }

    /// <summary>PETAL FEATHER: a second jump for a while.</summary>
    public class PetalFeatherAct : BaseKrisAct
    {
        internal const float BuffDuration = Buffs.Duration;

        protected override float Duration => 0.6f;
        protected override string Sound => Sounds.PetalFeather;

        public override void OnEnter()
        {
            base.OnEnter();
            Flourish(new Color(1f, 0.75f, 0.85f));
            if (NetworkServer.active && Buffs.PetalFeather != null) characterBody.AddTimedBuff(Buffs.PetalFeather, BuffDuration);
        }
    }

    /// <summary>
    /// BANISH and UNLEASH: the SOUL leaves Kris's chest, rises over their head and flares,
    /// stunning every enemy in the radius, then sinks back in.
    ///
    /// The heart and its light are local, built by every client from the state alone; the stun
    /// is the server's.
    /// </summary>
    public abstract class BaseSoulShineAct : BaseKrisAct
    {
        /// <summary>Stun reach, in metres.</summary>
        protected abstract float StunRadius { get; }
        /// <summary>How long the stun holds, in seconds.</summary>
        protected abstract float StunSeconds { get; }
        /// <summary>Damage dealt to everything in the radius at the flare, as a multiple of base damage.</summary>
        protected abstract float DamageCoefficient { get; }
        /// <summary>Size of the heart at its peak, over the SOUL skill's.</summary>
        protected abstract float SoulScale { get; }
        /// <summary>Peak brightness and reach of the heart's light.</summary>
        protected abstract float LightIntensity { get; }
        protected abstract float LightRange { get; }

        /// <summary>Seconds for the heart to climb from the chest.</summary>
        protected virtual float RiseTime => 0.45f;
        /// <summary>How long flying enemies in the radius are slammed down and held on the ground; 0 for not at all.</summary>
        protected virtual float RootSeconds => 0f;

        /// <summary>BANISH and UNLEASH share one sound.</summary>
        protected override string Sound => Sounds.SoulShine;
        /// <summary>How far above the chest the heart stops.</summary>
        private const float RiseHeight = 2.2f;
        /// <summary>Seconds the heart takes to sink back into the chest at the end.</summary>
        private const float ReturnTime = 0.3f;

        private static readonly Color Glow = new Color(1f, 0.35f, 0.35f);

        private SoulVisual soul;
        private Light glow;
        private bool flared;

        public override void OnEnter()
        {
            base.OnEnter();

            soul = SoulVisual.Create(SoulOrigin());
            glow = soul.gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = Glow;
            glow.range = LightRange;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;
        }

        public override void Update()
        {
            base.Update();
            if (soul == null) return;

            Vector3 chest = SoulOrigin();
            float rise = Mathf.Clamp01(age / RiseTime);
            float eased = 1f - (1f - rise) * (1f - rise);
            soul.transform.position = chest + Vector3.up * (RiseHeight * eased);

            // Grows on the way up, then swells once more as it flares.
            float flare = flared ? Mathf.Clamp01((age - RiseTime) / 0.15f) : 0f;
            float size = Mathf.Lerp(1f, SoulScale, eased) * (1f + 0.35f * flare * (1f - Mathf.Clamp01((age - RiseTime - 0.15f) / 0.4f)));
            soul.transform.localScale = Vector3.one * size;

            if (glow != null)
            {
                // A soft glow while rising, a flash at the flare, then a steady shine that dims
                // as the heart is about to sink back.
                float fade = 1f - Mathf.Clamp01((age - (Duration - 0.3f)) / 0.3f);
                float flash = flared ? Mathf.Max(0f, 1f - (age - RiseTime) / 0.5f) : 0f;
                glow.intensity = LightIntensity * (0.25f * eased + 0.75f * flare + flash * 1.5f) * fade;
            }
        }

        public override void FixedUpdate()
        {
            if (!flared && fixedAge >= RiseTime) Flare();
            base.FixedUpdate();
        }

        private void Flare()
        {
            flared = true;

            Vector3 at = soul != null ? soul.transform.position : SoulOrigin() + Vector3.up * RiseHeight;
            Burst(at, SoulScale * 1.5f, Color.white);
            Burst(at, StunRadius * 0.25f, KrisAssets.SoulRed);

            // Fired by the authority like X-SLASH's cuts, so the hits pay TP to the player who
            // owns Kris; the blast itself is settled on the server either way.
            if (isAuthority)
            {
                var blast = new BlastAttack
                {
                    attacker = gameObject,
                    inflictor = gameObject,
                    teamIndex = GetTeam(),
                    baseDamage = damageStat * DamageCoefficient,
                    baseForce = 0f,
                    position = at,
                    radius = StunRadius,
                    falloffModel = BlastAttack.FalloffModel.None,
                    procCoefficient = 1f,
                    crit = RollCrit(),
                    damageType = KrisDamage.Of(DamageType.Generic, DamageSource.Special),
                    damageColorIndex = DamageColorIndex.Default,
                    attackerFiltering = AttackerFiltering.NeverHitSelf,
                };

                BlastAttack.Result result = blast.Fire();
                if (tension != null) tension.AddForHits(result.hitCount);
            }

            if (!NetworkServer.active) return;

            TeamIndex team = GetTeam();
            float radiusSqr = StunRadius * StunRadius;
            var bodies = CharacterBody.readOnlyInstancesList;
            for (int i = 0; i < bodies.Count; i++)
            {
                CharacterBody other = bodies[i];
                if (other == null || other.teamComponent == null || other.teamComponent.teamIndex == team) continue;
                if (other.healthComponent == null || !other.healthComponent.alive) continue;
                if ((other.corePosition - at).sqrMagnitude > radiusSqr) continue;
                SetStateOnHurt.SetStunOnObject(other.gameObject, StunSeconds);
                if (RootSeconds > 0f) GroundPin.Pin(other, RootSeconds);
            }
        }

        public override void OnExit()
        {
            // The heart sinks home after Kris is free to move again; an ACT cut short by death
            // sends it the same way, and with no chest left it simply shrinks where it is.
            if (glow != null) Object.Destroy(glow);
            if (soul != null) soul.Absorb(FindModelChild("Chest"), ReturnTime);
            soul = null;
            base.OnExit();
        }
    }

    /// <summary>BANISH: the SOUL shines, hitting as hard as a basic slash and stunning enemies close by.</summary>
    public class BanishAct : BaseSoulShineAct
    {
        internal const float Radius = 15f;
        internal const float StunTime = 3f;
        internal static float Damage => KrisConfig.SlashDamage.Value;

        protected override float Duration => 1.3f;
        protected override float StunRadius => Radius;
        protected override float StunSeconds => StunTime;
        protected override float DamageCoefficient => Damage;
        protected override float SoulScale => 2f;
        protected override float LightIntensity => 6f;
        protected override float LightRange => 18f;
    }

    /// <summary>
    /// UNLEASH: BANISH, brighter - twice the reach, the stun and the damage - and every flying
    /// enemy it reaches is slammed to the ground and rooted there.
    /// </summary>
    public class UnleashAct : BaseSoulShineAct
    {
        internal const float Radius = 30f;
        internal const float StunTime = 6f;
        internal const float RootTime = Buffs.Duration;
        internal static float Damage => BanishAct.Damage * 2f;

        protected override float RootSeconds => RootTime;

        protected override float Duration => 1.7f;
        protected override float RiseTime => 0.6f;
        protected override float StunRadius => Radius;
        protected override float StunSeconds => StunTime;
        protected override float DamageCoefficient => Damage;
        protected override float SoulScale => 3.5f;
        protected override float LightIntensity => 12f;
        protected override float LightRange => 35f;
    }

    /// <summary>
    /// PROCEED: the nearest enemy in front of Kris dies. The menu has the authority pick the
    /// target before any TP is paid (see <see cref="Prepare"/>), so a pick with nothing to
    /// kill costs nothing; the target then travels with the state to every client, and the
    /// server does the killing.
    /// </summary>
    public class ProceedAct : BaseKrisAct
    {
        private const float Range = 60f;
        private const float ConeHalfAngle = 45f;
        private const float StrikeAt = 0.35f;

        private GameObject target;
        private bool struck;

        protected override float Duration => 0.9f;
        protected override string Pose => KrisAnimations.XSlash;
        protected override string Sound => Sounds.Proceed;

        internal override bool Prepare(CharacterBody body, Ray aim)
        {
            if (body == null || body.teamComponent == null) return false;

            var search = new BullseyeSearch
            {
                teamMaskFilter = TeamMask.GetEnemyTeams(body.teamComponent.teamIndex),
                filterByLoS = true,
                searchOrigin = aim.origin,
                searchDirection = aim.direction,
                sortMode = BullseyeSearch.SortMode.Distance,
                maxDistanceFilter = Range,
                maxAngleFilter = ConeHalfAngle,
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(body.gameObject);

            foreach (HurtBox hurtBox in search.GetResults())
            {
                if (hurtBox == null || hurtBox.healthComponent == null || !hurtBox.healthComponent.alive) continue;
                target = hurtBox.healthComponent.gameObject;
                return true;
            }
            return false;
        }

        public override void OnEnter()
        {
            base.OnEnter();
            if (target != null) LockFacing(target.transform.position - transform.position);
        }

        public override void FixedUpdate()
        {
            if (!struck && fixedAge >= StrikeAt) Strike();
            base.FixedUpdate();
        }

        private void Strike()
        {
            struck = true;
            if (target == null) return;

            HealthComponent victim = target.GetComponent<HealthComponent>();
            if (victim == null || !victim.alive) return;

            Vector3 at = victim.body != null ? victim.body.corePosition : target.transform.position;
            // Snow, not steel: a frost blast on the target and a gust of snow swirling down round
            // it. Local on every client, since every client runs this.
            float size = victim.body != null ? Mathf.Clamp(victim.body.bestFitRadius, 1f, 8f) : 1.5f;
            if (KrisAssets.ProceedFrostEffect != null)
            {
                KrisEffects.Spawn(KrisAssets.ProceedFrostEffect, new EffectData
                {
                    origin = at,
                    scale = 2f + size * 2f,
                    color = SnowfallVfx.Ice,
                }, false);
            }
            SnowfallVfx.Spawn(at, 1.5f + size * 1.5f, 2.5f + size * 1.5f, 160);
            Burst(at, 3f, SnowfallVfx.Ice);

            if (!NetworkServer.active) return;

            // Far more than it has, past armor, blocks and one-shot protection. Something
            // that is invulnerable right now (a boss between phases) still shrugs it off.
            victim.TakeDamage(new DamageInfo
            {
                attacker = gameObject,
                inflictor = gameObject,
                damage = (victim.fullCombinedHealth + victim.barrier) * 10f,
                position = at,
                force = Vector3.zero,
                crit = true,
                procCoefficient = 0f,
                damageType = KrisDamage.Of(DamageType.BypassArmor | DamageType.BypassBlock | DamageType.BypassOneShotProtection,
                                           DamageSource.Special),
                damageColorIndex = DamageColorIndex.WeakPoint,
            });
        }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(target);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            target = reader.ReadGameObject();
        }
    }
}
