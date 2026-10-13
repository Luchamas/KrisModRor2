using System;
using System.Collections.Generic;
using System.Reflection;
using KrisMod.Content;
using RoR2;

namespace KrisMod.Modules
{
    /// <summary>
    /// Keeps a player out of a lobby when their game numbers its content differently from the
    /// host's, and logs the fingerprint it compares so two players can see why.
    ///
    /// Online, the game does not name bodies, entity states, items or buffs: it sends their index
    /// in its catalogs. Picking Kris sends "body #N"; a slash sends "state #M". Each game builds its
    /// catalogs from its own install, so a mod one player has and another lacks - or a different
    /// version of one - shifts the indices after it, and the other game reads a valid number that
    /// means something else. Nothing throws where the mistake is made, only where the wrong thing
    /// runs. That is the reading of the host log from the two-Kris lobby this was written for: a
    /// Solus Wing heat pod (HeaterPodController.Awake) spawned as a player's body on respawn, and
    /// Clay Templar's FireSonicBoom and a Shrine of Rebirth state decoded from the client's own
    /// state messages. None of those stack traces passes through this mod.
    ///
    /// The game already turns a player away at connect when their network mod list hashes
    /// differently from the host's (ClientAuthData.modHash), with a "mod mismatch" message that
    /// lists the host's entries. R2API puts every mod that hard-depends on it on that list, by GUID
    /// and version - but nothing else, and never the content itself. So Kris adds one more entry,
    /// <see cref="EntryPrefix"/> plus the fingerprint, and a game whose catalogs differ no longer
    /// hashes like the host's. The same fingerprint is in the "Multiplayer check" line of every
    /// player's log, so the one in the kick message can be compared against it.
    ///
    /// ⚠ Everything hashed here must come out the same on two identical installs, or identical
    /// players are refused: catalog order and names only - nothing localised, nothing timed,
    /// nothing that depends on DLC ownership or settings.
    /// </summary>
    internal static class ContentCheck
    {
        /// <summary>The network mod list entry, before the fingerprint: "com.deltarune.KrisMod.content;1A2B3C4D".</summary>
        private const string EntryPrefix = KrisPlugin.Guid + ".content;";

        private static readonly FieldInfo StateTypesField =
            typeof(EntityStateCatalog).GetField("stateIndexToType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        internal static void Init()
        {
            // Not before: the catalogs are only complete once the game has loaded. Nobody can
            // connect to anything before then, so the hash a connection sends always includes it.
            RoR2Application.onLoad += AddToNetworkModList;
        }

        private static void AddToNetworkModList()
        {
            try
            {
                string key = Fingerprint(out string details);

                // The setter rehashes the list; this is how R2API adds its entries too.
                var list = new List<string>(NetworkModCompatibilityHelper.networkModList);
                list.Add(EntryPrefix + key);
                NetworkModCompatibilityHelper.networkModList = list;

                Log.Info("Multiplayer check: content " + key + " (" + details + "). Every player in a lobby " +
                         "must log the same content key. A game whose key differs from the host's is refused " +
                         "with a mod mismatch naming '" + EntryPrefix + key + "', because its mods or game " +
                         "version differ and it would be handed the wrong characters and skills.");
            }
            catch (Exception e)
            {
                // Without the entry, lobbies are checked by R2API's list alone, as before 1.0.2.
                Log.Error("Could not add the content check to the network mod list - lobbies with different " +
                          "content will not be refused: " + e);
            }
        }

        /// <summary>
        /// One hash over the body, entity state, item and buff catalogs, in catalog order.
        /// <paramref name="details"/> says what went into it, catalog by catalog, and where Kris sits.
        /// </summary>
        private static string Fingerprint(out string details)
        {
            var bodies = new List<string>();
            for (int i = 0; i < BodyCatalog.bodyCount; i++) bodies.Add(BodyCatalog.GetBodyName((BodyIndex)i));

            var items = new List<string>();
            for (int i = 0; i < ItemCatalog.itemCount; i++)
            {
                ItemDef item = ItemCatalog.GetItemDef((ItemIndex)i);
                items.Add(item != null ? item.name : null);
            }

            var buffs = new List<string>();
            for (int i = 0; i < BuffCatalog.buffCount; i++)
            {
                BuffDef buff = BuffCatalog.GetBuffDef((BuffIndex)i);
                buffs.Add(buff != null ? buff.name : null);
            }

            // Read straight off the catalog: it has no public count, and the order is the point.
            // Unreadable comes out the same on every copy of the same game, so it still compares.
            var states = new List<string>();
            int fightState = -1;
            var stateTypes = StateTypesField != null ? StateTypesField.GetValue(null) as Type[] : null;
            if (stateTypes != null)
            {
                foreach (Type type in stateTypes) states.Add(type != null ? type.FullName : null);
                fightState = Array.IndexOf(stateTypes, typeof(SkillStates.KrisSlash));
            }
            else
            {
                Log.Warning("EntityStateCatalog.stateIndexToType is gone - the content check leaves out entity states.");
            }

            string[] parts =
            {
                "bodies " + Describe(bodies),
                "entity states " + Describe(states),
                "items " + Describe(items),
                "buffs " + Describe(buffs),
            };

            details = string.Join(", ", parts) + "; Kris is body #" + (int)BodyCatalog.FindBodyIndex(KrisBody.BodyName) +
                      ", FIGHT is state #" + fightState;
            return Hash(parts).ToString("X8");
        }

        /// <summary>How many names, and their hash.</summary>
        private static string Describe(List<string> names) => names.Count + " [" + Hash(names).ToString("X8") + "]";

        /// <summary>FNV-1a over the names in order.</summary>
        private static uint Hash(IEnumerable<string> names)
        {
            uint hash = 2166136261;
            foreach (string name in names)
            {
                string s = name ?? string.Empty;
                for (int i = 0; i < s.Length; i++) hash = (hash ^ s[i]) * 16777619;
                // A separator, so ["ab", "c"] and ["a", "bc"] do not hash alike.
                hash = (hash ^ '\n') * 16777619;
            }
            return hash;
        }
    }
}
