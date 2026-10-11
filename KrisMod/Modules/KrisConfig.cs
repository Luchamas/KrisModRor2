using BepInEx.Configuration;
using UnityEngine;

namespace KrisMod.Modules
{
    /// <summary>
    /// Every number a player might reasonably want to tweak lives here so balance passes
    /// do not require a recompile.
    ///
    /// Not here, on purpose: anything that belongs to the model or the asset bundle (model scale,
    /// hitbox placement solved against the rig, clip lengths). Those are constants next to the
    /// code they constrain - a config entry is written once into the player's local file and
    /// never updated again, so a value that has to track the bundle goes stale silently.
    /// </summary>
    internal static class KrisConfig
    {
        internal static ConfigEntry<float> DisplayModelScale;
        internal static ConfigEntry<bool> ModelRagdoll;
        internal static ConfigEntry<bool> ShowItemDisplays;
        internal static ConfigEntry<float> SoundVolume;

        internal static ConfigEntry<float> BaseHealth;
        internal static ConfigEntry<float> BaseDamage;
        internal static ConfigEntry<float> BaseMoveSpeed;
        internal static ConfigEntry<float> BaseArmor;

        internal static ConfigEntry<float> TpBarLeftMargin;
        internal static ConfigEntry<float> TpBarHeight;

        internal static ConfigEntry<float> TensionMax;
        internal static ConfigEntry<float> TensionGrazeRadius;
        internal static ConfigEntry<float> TensionGrazePerSecond;
        internal static ConfigEntry<float> TensionPerHit;

        internal static ConfigEntry<float> SlashDamage;
        internal static ConfigEntry<float> SlashDuration;
        internal static ConfigEntry<float> SlashRange;

        internal static ConfigEntry<float> DefendDamageReduction;
        internal static ConfigEntry<float> DefendTensionPerHit;
        internal static ConfigEntry<float> DefendCooldown;

        internal static ConfigEntry<float> SoulTensionCost;
        internal static ConfigEntry<float> SoulCooldown;
        internal static ConfigEntry<float> SoulRange;
        internal static ConfigEntry<float> SoulSpeed;
        internal static ConfigEntry<float> SoulPullSpeed;
        internal static ConfigEntry<float> SoulSlashDamage;

        internal static ConfigEntry<float> ActCooldown;
        internal static ConfigEntry<float> ActMenuTimeout;
        internal static ConfigEntry<float> ProceedChance;

        internal static void Init(ConfigFile config)
        {
            DisplayModelScale = config.Bind("00 - General", "Character Select Model Scale", 1f,
                "Extra scale for the character-select mannequin only, multiplied on top of the " +
                "in-game size. In-game size is unaffected.");

            ModelRagdoll = config.Bind("00 - General", "Model Ragdoll", true,
                "Build a real ragdoll on Kris's own skeleton, so the corpse goes limp. Set false " +
                "and the corpse simply holds its last pose.");

            ShowItemDisplays = config.Bind("00 - General", "Show Item Displays", false,
                "Draw picked-up items on Kris's body. Off by default: the only placements " +
                "available are Loader's, positioned against Loader's skeleton, so items sit wrong " +
                "on Kris. Cosmetic only - items always work either way.");

            SoundVolume = config.Bind("00 - General", "Sound Volume", 0.5f,
                "Volume of Kris's sound clips, 0 to 1. Set to 0 to mute them.");

            TpBarLeftMargin = config.Bind("00 - General", "TP Bar Left Margin", 60f,
                "Distance from the left edge of the screen to the TP bar, in HUD units.");
            TpBarHeight = config.Bind("00 - General", "TP Bar Height", 338f,
                "On-screen height of the TP bar, in HUD units. The width follows the artwork's " +
                "aspect ratio, and the bar stays centred vertically.");

            // Not "Base Health" (120) and "Base Armor" (10): existing .cfg files carry those
            // keys, which would override the melee-tier defaults forever. Both are now orphans.
            BaseHealth = config.Bind("01 - Stats", "Max Health", 160f,
                "Health at level 1. Each level adds 30% of this.");
            BaseDamage = config.Bind("01 - Stats", "Base Damage", 12f, "Damage at level 1.");
            BaseMoveSpeed = config.Bind("01 - Stats", "Base Move Speed", 7f, "Movement speed in m/s.");
            BaseArmor = config.Bind("01 - Stats", "Armor", 20f, "Armor at every level.");

            TensionMax = config.Bind("02 - Tension (TP)", "Max TP", 100f, "Maximum stored Tension Points.");
            TensionGrazeRadius = config.Bind("02 - Tension (TP)", "Graze Radius", 14f,
                "Enemies within this radius feed TP, mirroring DELTARUNE's grazing.");
            TensionGrazePerSecond = config.Bind("02 - Tension (TP)", "Graze TP per Second", 4f,
                "TP per second while at least one enemy is inside the graze radius.");
            TensionPerHit = config.Bind("02 - Tension (TP)", "TP per Hit", 1.2f, "TP granted per enemy hit.");

            // Not "Damage": that key was written into existing .cfg files at 1 (100%), which
            // would override the new default forever. The old entry is now an inert orphan.
            SlashDamage = config.Bind("03 - Primary: Slash", "Damage Coefficient", 2.5f,
                "Damage coefficient per slash. 2.5 means 250% of base damage.");
            SlashDuration = config.Bind("03 - Primary: Slash", "Slash Duration", 0.5f,
                "Seconds per slash before attack speed is applied.");
            SlashRange = config.Bind("03 - Primary: Slash", "Reach", 4f,
                "How far in front of Kris the sword reaches, in meters.");

            // Replaces "Bonus Armor", "TP Gain Multiplier" and "TP per Second", which old .cfg
            // files still carry as inert orphans.
            DefendDamageReduction = config.Bind("04 - Secondary: Defend", "Damage Reduction", 0.9f,
                "Fraction of incoming damage the raised shield blocks, 0 to 1. 0.9 means 90% less damage taken.");
            DefendTensionPerHit = config.Bind("04 - Secondary: Defend", "TP per Hit Taken", 5f,
                "TP gained for each hit taken behind the raised shield. The shield gives no TP otherwise. " +
                "Damage over time does not count.");
            DefendCooldown = config.Bind("04 - Secondary: Defend", "Cooldown", 1f,
                "Seconds after lowering the shield before it can be raised again.");

            // Not "TP Cost": existing .cfg files carry it at 25, which would override this forever.
            SoulTensionCost = config.Bind("05 - Utility: SOUL", "TP Cost (%)", 15f,
                "Percent of a full TP bar spent when the SOUL is thrown.");
            SoulCooldown = config.Bind("05 - Utility: SOUL", "Cooldown", 2f,
                "Cooldown in seconds, on top of the TP cost.");
            SoulRange = config.Bind("05 - Utility: SOUL", "Range", 45f,
                "How far the SOUL travels before it stops, in meters. It also stops at walls and enemies.");
            SoulSpeed = config.Bind("05 - Utility: SOUL", "SOUL Speed", 110f,
                "How fast the SOUL flies out, in m/s.");
            SoulPullSpeed = config.Bind("05 - Utility: SOUL", "Pull Speed", 60f,
                "How fast Kris is pulled to the SOUL, in m/s.");
            SoulSlashDamage = config.Bind("05 - Utility: SOUL", "Cut Damage Coefficient", 4f,
                "Damage of the cut Kris makes on catching the SOUL, as a multiple of base damage.");

            ActCooldown = config.Bind("06 - Special: ACT", "Cooldown", 8f,
                "Cooldown in seconds after an ACT is performed.");
            ActMenuTimeout = config.Bind("06 - Special: ACT", "Menu Timeout", 6f,
                "Seconds the ACT menu stays open before closing on its own.");
            ProceedChance = config.Bind("06 - Special: ACT", "Proceed Chance (%)", 5f,
                "Percent chance that PROCEED is one of the four ACTs offered each time the menu opens. " +
                "100 offers it every time.");
        }
    }
}
