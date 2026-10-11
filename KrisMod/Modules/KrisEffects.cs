using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace KrisMod.Modules
{
    /// <summary>
    /// One gate for every visual effect the mod spawns.
    ///
    /// EffectManager.SpawnEffect only accepts prefabs that are in the EffectCatalog. It resolves
    /// them through EffectCatalog.FindEffectIndexFromPrefab, which is nothing more than
    /// prefab.GetComponent&lt;EffectComponent&gt;()?.effectIndex - so a prefab that carries no
    /// EffectComponent comes back Invalid, the call draws nothing at all, and the game logs
    /// "Unable to SpawnEffect from prefab named '...'" once per attempt.
    ///
    /// Not every vanilla VFX worth borrowing is catalogued. The melee swing prefabs are the
    /// obvious trap: vanilla never spawns them through EffectManager at all, it instantiates
    /// them by hand as a child of a muzzle transform (see BasicMeleeAttack.BeginMeleeAttackEffect),
    /// so CrocoSlash and its siblings look perfectly usable in the catalog browser and are not.
    ///
    /// So: catalogued prefabs take the EffectManager path they were built for - networked,
    /// pooled, coloured from the EffectData. Everything else is instantiated locally rather than
    /// dropped on the floor. Kris's skill states run on every client, not just the authority,
    /// so a local copy is still seen by everyone watching - which is exactly how vanilla plays
    /// its own swings.
    /// </summary>
    internal static class KrisEffects
    {
        /// <summary>Floor and ceiling for a hand-instantiated effect's time on screen.</summary>
        private const float MinLifetime = 1f;
        private const float MaxLifetime = 8f;

        // Catalog membership is fixed once content has loaded and the answer costs a
        // GetComponent, so each prefab is asked at most once.
        private static readonly Dictionary<GameObject, bool> Catalogued =
            new Dictionary<GameObject, bool>();

        /// <summary>
        /// Plays <paramref name="prefab"/> at <paramref name="data"/>, through the effect
        /// network when the prefab is catalogued and locally when it is not.
        /// </summary>
        internal static void Spawn(GameObject prefab, EffectData data, bool transmit)
        {
            if (prefab == null || data == null) return;

            if (IsCatalogued(prefab))
            {
                EffectManager.SpawnEffect(prefab, data, transmit);
                return;
            }

            SpawnDetached(prefab, data.origin, data.rotation, data.scale);
        }

        /// <summary>
        /// Instantiates <paramref name="prefab"/> in world space and gives it an end. For VFX
        /// that are not EffectCatalog entries, which have no EffectComponent to read the
        /// EffectData - so position, rotation and scale are applied to the transform by hand and
        /// any tint in the EffectData is lost.
        /// </summary>
        internal static GameObject SpawnDetached(GameObject prefab, Vector3 position,
                                                 Quaternion rotation, float scale)
        {
            if (prefab == null) return null;

            // EffectData starts out with an identity rotation, but a caller reaching this
            // directly can hand over default(Quaternion), which Instantiate rejects as invalid.
            if (rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f)
            {
                rotation = Quaternion.identity;
            }

            GameObject instance = Object.Instantiate(prefab, position, rotation);

            // EffectData.scale is a multiplier over the prefab's own scale, not a size.
            if (scale > 0f) instance.transform.localScale = prefab.transform.localScale * scale;

            // Nothing else is going to reclaim this. The pooled path returns catalogued effects
            // to EffectManager when they finish; a hand-made copy just sits there, so it is
            // destroyed a little after its particles are done.
            Object.Destroy(instance, Lifetime(instance));
            return instance;
        }

        /// <summary>How long the effect's own particles need, clamped to something sane.</summary>
        private static float Lifetime(GameObject instance)
        {
            float longest = 0f;

            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
            }

            return Mathf.Clamp(longest, MinLifetime, MaxLifetime);
        }

        private static bool IsCatalogued(GameObject prefab)
        {
            bool known;
            if (Catalogued.TryGetValue(prefab, out known)) return known;

            bool catalogued = EffectCatalog.FindEffectIndexFromPrefab(prefab) != EffectIndex.Invalid;

            // Before the catalog is built every prefab reports Invalid, so an early answer is
            // not worth keeping - ask again next time.
            if (EffectCatalog.effectCount <= 0) return catalogued;

            Catalogued[prefab] = catalogued;

            if (!catalogued)
            {
                Log.Info("'" + prefab.name + "' is not an EffectCatalog entry, so it cannot go " +
                         "through EffectManager. Instantiating it locally instead.");
            }

            return catalogued;
        }
    }
}
