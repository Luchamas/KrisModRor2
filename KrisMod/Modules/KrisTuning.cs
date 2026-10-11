namespace KrisMod.Modules
{
    /// <summary>
    /// Every balance and presentation number in one place. These are fixed: the mod writes no
    /// config file, so every player gets the same Kris.
    ///
    /// Not here, on purpose: anything that belongs to the model or the asset bundle (model scale,
    /// hitbox placement solved against the rig, clip lengths). Those are constants next to the
    /// code they constrain.
    /// </summary>
    internal static class KrisTuning
    {
        // General
        /// <summary>Extra scale for the character-select mannequin only, on top of the in-game size.</summary>
        internal const float DisplayModelScale = 1f;
        /// <summary>Build a real ragdoll on Kris's own skeleton so the corpse goes limp.</summary>
        internal const bool ModelRagdoll = true;
        /// <summary>
        /// Draw picked-up items on Kris's body. Off: the only placements available are Loader's,
        /// positioned against Loader's skeleton, so items sit wrong on Kris.
        /// </summary>
        internal const bool ShowItemDisplays = false;
        /// <summary>Volume of Kris's sound clips, 0 to 1.</summary>
        internal const float SoundVolume = 0.5f;

        /// <summary>Distance from the left edge of the screen to the TP bar, in HUD units.</summary>
        internal const float TpBarLeftMargin = 60f;
        /// <summary>On-screen height of the TP bar in HUD units; the width follows the artwork.</summary>
        internal const float TpBarHeight = 338f;

        // Stats
        /// <summary>Health at level 1. Each level adds 30% of this.</summary>
        internal const float BaseHealth = 160f;
        internal const float BaseDamage = 12f;
        /// <summary>Movement speed in m/s.</summary>
        internal const float BaseMoveSpeed = 7f;
        internal const float BaseArmor = 20f;

        // Tension (TP)
        internal const float TensionMax = 100f;
        /// <summary>Enemies within this radius feed TP, mirroring DELTARUNE's grazing.</summary>
        internal const float TensionGrazeRadius = 14f;
        /// <summary>TP per second while at least one enemy is inside the graze radius.</summary>
        internal const float TensionGrazePerSecond = 4f;
        internal const float TensionPerHit = 1.2f;

        // Primary: Slash
        /// <summary>Damage coefficient per slash. 2.5 means 250% of base damage.</summary>
        internal const float SlashDamage = 2.5f;
        /// <summary>Seconds per slash before attack speed is applied.</summary>
        internal const float SlashDuration = 0.5f;
        /// <summary>How far in front of Kris the sword reaches, in meters.</summary>
        internal const float SlashRange = 4f;

        // Secondary: Defend
        /// <summary>Fraction of incoming damage the raised shield blocks, 0 to 1.</summary>
        internal const float DefendDamageReduction = 0.9f;
        /// <summary>TP gained for each hit taken behind the raised shield. Damage over time does not count.</summary>
        internal const float DefendTensionPerHit = 5f;
        /// <summary>Seconds after lowering the shield before it can be raised again.</summary>
        internal const float DefendCooldown = 1f;

        // Utility: SOUL
        /// <summary>Percent of a full TP bar spent when the SOUL is thrown.</summary>
        internal const float SoulTensionCost = 15f;
        internal const float SoulCooldown = 2f;
        /// <summary>How far the SOUL travels before it stops, in meters. It also stops at walls and enemies.</summary>
        internal const float SoulRange = 45f;
        /// <summary>How fast the SOUL flies out, in m/s.</summary>
        internal const float SoulSpeed = 110f;
        /// <summary>How fast Kris is pulled to the SOUL, in m/s.</summary>
        internal const float SoulPullSpeed = 60f;
        /// <summary>Damage of the cut Kris makes on catching the SOUL, as a multiple of base damage.</summary>
        internal const float SoulSlashDamage = 4f;

        // Special: ACT
        internal const float ActCooldown = 8f;
        /// <summary>Seconds the ACT menu stays open before closing on its own.</summary>
        internal const float ActMenuTimeout = 6f;
        /// <summary>Percent chance that PROCEED is one of the four ACTs offered each time the menu opens.</summary>
        internal const float ProceedChance = 5f;
    }
}
