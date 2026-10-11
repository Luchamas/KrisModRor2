using R2API;
using UnityEngine;

namespace KrisMod.Modules
{
    /// <summary>Every language token the survivor uses.</summary>
    internal static class Tokens
    {
        internal const string Prefix = "KRIS_";

        internal const string BodyName = Prefix + "NAME";
        internal const string BodySubtitle = Prefix + "SUBTITLE";
        internal const string BodyDescription = Prefix + "DESCRIPTION";
        internal const string BodyOutro = Prefix + "OUTRO_FLAVOR";
        internal const string BodyFailure = Prefix + "OUTRO_FAILURE";
        internal const string BodyLore = Prefix + "LORE";

        internal const string PassiveName = Prefix + "PASSIVE_NAME";
        internal const string PassiveDescription = Prefix + "PASSIVE_DESCRIPTION";

        internal const string PrimaryName = Prefix + "PRIMARY_NAME";
        internal const string PrimaryDescription = Prefix + "PRIMARY_DESCRIPTION";

        internal const string SecondaryName = Prefix + "SECONDARY_NAME";
        internal const string SecondaryDescription = Prefix + "SECONDARY_DESCRIPTION";

        internal const string UtilityName = Prefix + "UTILITY_NAME";
        internal const string UtilityDescription = Prefix + "UTILITY_DESCRIPTION";

        internal const string SpecialName = Prefix + "SPECIAL_NAME";
        internal const string SpecialDescription = Prefix + "SPECIAL_DESCRIPTION";

        internal static void Init()
        {
            Add(BodyName, "Kris");
            Add(BodySubtitle, "The Lightner");
            Add(BodyDescription,
                "Kris is a sword-and-shield fighter who stores up TENSION and spends it on their SOUL and their ACTs.<style=cSub>\n\n" +
                "< ! > Standing near enemies and landing hits builds TP, and so does taking hits behind your shield.\n\n" +
                "< ! > DEFEND blocks " + Percent(KrisTuning.DefendDamageReduction) + " of the damage you take, " +
                "but you cannot swing while the shield is up.\n\n" +
                "< ! > Throw your SOUL at a wall or an enemy to be pulled straight to it. It costs " +
                Mathf.RoundToInt(KrisTuning.SoulTensionCost) + "% TP.\n\n" +
                "< ! > ACT freezes you in place and opens a menu of four ACTs. Pick one with the number " +
                "keys 1-4, or jump to back out.\n\n</style>");
            Add(BodyOutro, "..and so they left, their SOUL still their own.");
            Add(BodyFailure, "..and so they vanished, with no one left to control them.");
            Add(BodyLore,
                "It's what they call \"you.\"\n\n" +
                "A human from a small town, who fell through the floor of a supply closet and came " +
                "out the other side holding a sword. In the dark, they are a hero. In the light, they " +
                "are a quiet kid who does not say much.\n\n" +
                "Nobody ever asks them what they want. They have learned to answer anyway.");

            Add(PassiveName, "TENSION");
            Add(PassiveDescription,
                "Being near enemies and landing hits builds <style=cIsUtility>TP</style>, up to " +
                "<style=cIsUtility>100%</style>. Your SOUL and your ACTs spend it.");

            Add(PrimaryName, "FIGHT");
            Add(PrimaryDescription,
                "Slash with your sword for <style=cIsDamage>" + Percent(KrisTuning.SlashDamage) +
                "</style> damage.");

            Add(SecondaryName, "DEFEND");
            Add(SecondaryDescription,
                "Hold to raise your shield. While it is up, take <style=cIsUtility>" +
                Percent(KrisTuning.DefendDamageReduction) + " less damage</style>, and every hit you " +
                "take grants <style=cIsUtility>" + Mathf.RoundToInt(KrisTuning.DefendTensionPerHit) +
                "% TP</style>. You stand your ground: no moving, jumping or attacking.");

            Add(UtilityName, "SOUL");
            Add(UtilityDescription,
                "Costs <style=cIsUtility>" + Mathf.RoundToInt(KrisTuning.SoulTensionCost) +
                "% TP</style>. Hold to aim your SOUL, release to tear it out and throw it. " +
                "Once it lands, you are <style=cIsUtility>pulled</style> to it, then catch it with a vertical cut for " +
                "<style=cIsDamage>" + Mathf.RoundToInt(KrisTuning.SoulSlashDamage * 100f) + "% damage</style>.");

            Add(SpecialName, "ACT");
            Add(SpecialDescription,
                "Stand still and choose one of <style=cIsUtility>four ACTs</style>. Each one costs " +
                "<style=cIsUtility>TP</style> and grants a buff or unleashes an attack.");

            Content.KrisActs.AddTokens();
        }

        internal static void Add(string token, string value) => LanguageAPI.Add(token, value);

        /// <summary>A coefficient as the game writes it: 2.5 is "250%".</summary>
        private static string Percent(float coefficient) => Mathf.RoundToInt(coefficient * 100f) + "%";
    }
}
