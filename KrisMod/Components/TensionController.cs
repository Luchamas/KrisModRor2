using KrisMod.Modules;
using RoR2;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// Kris's TP meter, DELTARUNE's Tension Points - the same meter Flowery carries: it fills
    /// from grazing (enemies close by) and from hits, and the SOUL and the ACTs spend it.
    ///
    /// DEFEND is the one addition, and it lives in <see cref="DefendGuard"/>: a hit taken behind
    /// the shield pays out TP through <see cref="Add"/>. The shield adds nothing here otherwise.
    ///
    /// Deliberately authority-local: only the client that owns the body ever mutates the value,
    /// and every spender (TensionSkillDef, the SOUL, the ACT menu) decides on that client.
    /// </summary>
    public class TensionController : MonoBehaviour
    {
        private const float GrazeCheckInterval = 0.25f;

        private CharacterBody body;
        private float grazeCheckTimer;
        private bool grazing;

        /// <summary>Current TP. Only meaningful on the authority.</summary>
        public float Tension { get; private set; }

        public float MaxTension => Mathf.Max(1f, KrisConfig.TensionMax.Value);

        public float Fraction => Mathf.Clamp01(Tension / MaxTension);

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        private void FixedUpdate()
        {
            if (body == null || !body.hasEffectiveAuthority) return;

            grazeCheckTimer -= Time.fixedDeltaTime;
            if (grazeCheckTimer <= 0f)
            {
                grazeCheckTimer = GrazeCheckInterval;
                grazing = AnyEnemyWithinGrazeRadius();
            }

            if (grazing) Add(KrisConfig.TensionGrazePerSecond.Value * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Grants TP. Silently ignored off-authority so callers do not need to branch.
        /// </summary>
        public void Add(float amount)
        {
            if (amount <= 0f) return;
            if (body != null && !body.hasEffectiveAuthority) return;
            Tension = Mathf.Min(MaxTension, Tension + amount);
        }

        /// <summary>Grants the per-hit reward for <paramref name="hitCount"/> enemies.</summary>
        public void AddForHits(int hitCount)
        {
            if (hitCount <= 0) return;
            Add(KrisConfig.TensionPerHit.Value * hitCount);
        }

        /// <summary>
        /// Costs are given in percent of a full bar, the way DELTARUNE prices them, so a larger
        /// Max TP in config does not make every ACT cheaper.
        /// </summary>
        public float CostToPoints(float percent) => percent * 0.01f * MaxTension;

        public bool CanSpend(float percent) => Tension + 0.001f >= CostToPoints(percent);

        /// <summary>Takes <paramref name="percent"/> of a bar if it is there; false and untouched if not.</summary>
        public bool TrySpend(float percent)
        {
            if (body != null && !body.hasEffectiveAuthority) return false;
            if (!CanSpend(percent)) return false;
            Tension = Mathf.Max(0f, Tension - CostToPoints(percent));
            return true;
        }

        private bool AnyEnemyWithinGrazeRadius()
        {
            float radiusSqr = KrisConfig.TensionGrazeRadius.Value * KrisConfig.TensionGrazeRadius.Value;
            Vector3 origin = body.corePosition;
            TeamIndex myTeam = body.teamComponent != null ? body.teamComponent.teamIndex : TeamIndex.Player;

            var instances = CharacterBody.readOnlyInstancesList;
            for (int i = 0; i < instances.Count; i++)
            {
                CharacterBody other = instances[i];
                if (other == null || other == body) continue;
                if (other.teamComponent == null || other.teamComponent.teamIndex == myTeam) continue;
                if (other.healthComponent == null || !other.healthComponent.alive) continue;
                if ((other.corePosition - origin).sqrMagnitude > radiusSqr) continue;
                return true;
            }

            return false;
        }
    }
}
