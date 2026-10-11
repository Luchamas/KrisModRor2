using R2API;
using RoR2;
using UnityEngine;

namespace KrisMod.Modules
{
    /// <summary>Custom buffs and the stat hook that gives them meaning.</summary>
    internal static class Buffs
    {
        /// <summary>
        /// Up for exactly as long as DEFEND is held (see SkillStates.KrisDefend). The root comes
        /// from the stat hook here; the damage reduction and the TP for hits taken are
        /// Components.DefendGuard, and because buffs replicate, the server that cuts the damage
        /// and the client that owns the meter both see it without anything extra networked.
        /// </summary>
        internal static BuffDef Defend;

        /// <summary>The COURAGE ACT, on Kris and every ally near: more armor for a while.</summary>
        internal static BuffDef Courage;

        /// <summary>The HOLD BREATH ACT: faster on foot for a while.</summary>
        internal static BuffDef HoldBreath;

        /// <summary>The PETAL FEATHER item: an extra jump in the air for a while.</summary>
        internal static BuffDef PetalFeather;

        /// <summary>A PIROUETTE gone right: quicker swings.</summary>
        internal static BuffDef Nimble;

        /// <summary>A PIROUETTE gone wrong: slower on foot.</summary>
        internal static BuffDef Dizzy;

        /// <summary>A PIROUETTE gone wrong: weaker hits.</summary>
        internal static BuffDef Clumsy;

        /// <summary>A PIROUETTE gone wrong: slower swings.</summary>
        internal static BuffDef Sluggish;

        /// <summary>A PIROUETTE gone wrong: less armor.</summary>
        internal static BuffDef Exposed;

        /// <summary>
        /// A PIROUETTE gone wrong: no jumping, for <see cref="RootedDuration"/> rather than the
        /// usual <see cref="Duration"/>. Not a stat - Components.RootedGuard spends the jumps.
        /// </summary>
        internal static BuffDef Rooted;

        /// <summary>Seconds every timed buff and debuff lasts, whichever ACT gives it. DEFEND is
        /// not timed: it lasts as long as the guard is held. Rooted is the one exception.</summary>
        internal const float Duration = 15f;

        internal const float RootedDuration = 8f;

        internal const float CourageArmorBonus = 40f;
        internal const float HoldBreathSpeedBonus = 0.4f;
        internal const int PetalFeatherJumps = 1;
        internal const float NimbleAttackSpeedBonus = 0.3f;
        internal const float DizzySpeedPenalty = 0.3f;
        internal const float ClumsyDamagePenalty = 0.25f;
        internal const float SluggishAttackSpeedPenalty = 0.25f;
        internal const float ExposedArmorPenalty = 20f;

        internal static void Init()
        {
            Defend = Create("bdKrisDefend", KrisAssets.ShieldPink, KrisAssets.IconDefend);
            Courage = Create("bdKrisCourage", KrisAssets.TpOrange, KrisAssets.StatusIcon("Defense", true));
            HoldBreath = Create("bdKrisHoldBreath", KrisAssets.KrisBlue, KrisAssets.StatusIcon("MoveSpeed", true));
            // The supplied icon is already coloured, so it is drawn untinted.
            PetalFeather = Create("bdKrisPetalFeather", Color.white, KrisAssets.IconPetalFeather);
            Nimble = Create("bdKrisNimble", new Color(1f, 0.85f, 0.3f), KrisAssets.StatusIcon("AttackSpeed", true));
            Dizzy = Create("bdKrisDizzy", new Color(0.6f, 0.4f, 0.9f), KrisAssets.StatusIcon("MoveSpeed", false), debuff: true);
            Clumsy = Create("bdKrisClumsy", new Color(0.5f, 0.5f, 0.5f), KrisAssets.StatusIcon("Attack", false), debuff: true);
            Sluggish = Create("bdKrisSluggish", new Color(0.6f, 0.5f, 0.35f), KrisAssets.StatusIcon("AttackSpeed", false), debuff: true);
            Exposed = Create("bdKrisExposed", new Color(0.85f, 0.35f, 0.35f), KrisAssets.StatusIcon("Defense", false), debuff: true);
            // No Icons/Status/Jump art yet: until there is, this falls back to the ACT icon.
            Rooted = Create("bdKrisRooted", new Color(0.45f, 0.65f, 0.3f), KrisAssets.StatusIcon("Jump", false), debuff: true);

            RecalculateStatsAPI.GetStatCoefficients += ApplyStats;
        }

        private static BuffDef Create(string name, Color color, Sprite icon, bool debuff = false)
        {
            var buff = ScriptableObject.CreateInstance<BuffDef>();
            buff.name = name;
            buff.buffColor = color;
            buff.canStack = false;
            buff.isDebuff = debuff;
            buff.isHidden = false;
            buff.iconSprite = icon;
            ContentAddition.AddBuffDef(buff);
            return buff;
        }

        private static void ApplyStats(CharacterBody body, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (body == null) return;

            if (Defend != null && body.HasBuff(Defend))
            {
                // Rooted, not slowed: DEFEND is a planted guard. It was a 40% slow once, which
                // had Kris gliding about in a pose drawn for standing still. A design constant
                // rather than config, so no .cfg written by the old version can bring the glide
                // back (its "Move Speed Penalty" entry is now an inert orphan).
                args.moveSpeedRootCount += 1;
            }

            if (Courage != null && body.HasBuff(Courage)) args.armorAdd += CourageArmorBonus;
            if (HoldBreath != null && body.HasBuff(HoldBreath)) args.moveSpeedMultAdd += HoldBreathSpeedBonus;
            if (PetalFeather != null && body.HasBuff(PetalFeather)) args.jumpCountAdd += PetalFeatherJumps;
            if (Dizzy != null && body.HasBuff(Dizzy)) args.moveSpeedReductionMultAdd += DizzySpeedPenalty;
            if (Clumsy != null && body.HasBuff(Clumsy)) args.damageMultAdd -= ClumsyDamagePenalty;
            if (Nimble != null && body.HasBuff(Nimble)) args.attackSpeedMultAdd += NimbleAttackSpeedBonus;
            if (Sluggish != null && body.HasBuff(Sluggish)) args.attackSpeedMultAdd -= SluggishAttackSpeedPenalty;
            if (Exposed != null && body.HasBuff(Exposed)) args.armorAdd -= ExposedArmorPenalty;
        }
    }
}
