using KrisMod.Modules;
using RoR2;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// What the raised shield does besides rooting Kris: it blocks most of every hit, and each
    /// hit it takes pays out TP - the only TP the shield gives.
    ///
    /// Two halves on two machines, both reading the Defend buff, which replicates. The cut is on
    /// the server, where damage is dealt: HealthComponent hands every incoming hit to the body's
    /// <see cref="IOnIncomingDamageServerReceiver"/>s before armor is applied. The TP is paid on
    /// the client that owns Kris, the only machine that keeps the meter (see
    /// <see cref="TensionController"/>); it hears about the hit through the damage notification
    /// the server sends every client, the same one damage numbers are drawn from.
    /// </summary>
    public class DefendGuard : MonoBehaviour, IOnIncomingDamageServerReceiver
    {
        /// <summary>
        /// Least time between two TP payouts, so a volley that lands as a dozen hits at once, or a
        /// laser that ticks many times a second, does not fill the bar in a moment.
        /// </summary>
        private const float PayoutInterval = 0.25f;

        private CharacterBody body;
        private TensionController tension;
        private float nextPayout;

        private bool Raised => Buffs.Defend != null && body != null && body.HasBuff(Buffs.Defend);

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            tension = GetComponent<TensionController>();
        }

        private void OnEnable() => GlobalEventManager.onClientDamageNotified += OnClientDamageNotified;

        private void OnDisable() => GlobalEventManager.onClientDamageNotified -= OnClientDamageNotified;

        /// <summary>
        /// Server: cuts a hit taken while the shield is up. Damage that bypasses blocking, such
        /// as the void fog, goes through untouched, as it does past Tougher Times.
        /// </summary>
        public void OnIncomingDamageServer(DamageInfo damageInfo)
        {
            if (!Raised || !Blockable(damageInfo.damageType)) return;
            damageInfo.damage *= 1f - Mathf.Clamp01(KrisConfig.DefendDamageReduction.Value);
        }

        /// <summary>
        /// Owning client: TP for a hit the shield took. Damage-over-time ticks do not count - a
        /// burn would otherwise pay out every tick for one hit.
        /// </summary>
        private void OnClientDamageNotified(DamageDealtMessage message)
        {
            if (message == null || message.victim != gameObject) return;
            if (tension == null || body == null || !body.hasEffectiveAuthority) return;
            if (!Raised || message.damage <= 0f) return;
            if (!Blockable(message.damageType) || (message.damageType.damageType & DamageType.DoT) != 0) return;

            if (Time.time < nextPayout) return;
            nextPayout = Time.time + PayoutInterval;
            tension.Add(KrisConfig.DefendTensionPerHit.Value);
        }

        private static bool Blockable(DamageTypeCombo type) => (type.damageType & DamageType.BypassBlock) == 0;
    }
}
