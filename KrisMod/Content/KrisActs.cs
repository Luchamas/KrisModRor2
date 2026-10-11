using System;
using System.Collections.Generic;
using KrisMod.Modules;
using KrisMod.SkillStates;
using R2API;

namespace KrisMod.Content
{
    /// <summary>What an ACT does, for the menu's label colour and the sound it makes.</summary>
    internal enum ActKind
    {
        Attack,
        Buff,
        /// <summary>An item used rather than an ACT performed; plays the item sound after the selection one.</summary>
        Item,
    }

    /// <summary>One entry in the ACT pool: its name, its TP price and the state that performs it.</summary>
    internal sealed class KrisAct
    {
        internal string Id;
        internal string Name;
        internal string Description;
        /// <summary>Percent of a full TP bar, as DELTARUNE prices things.</summary>
        internal float Cost;
        internal ActKind Kind;
        /// <summary>An entity state deriving from <see cref="BaseKrisAct"/>, run on the Body machine.</summary>
        internal Type State;
        /// <summary>
        /// A rare ACT is not shuffled in with the rest: each time the menu opens it rolls this
        /// (0..1) to be offered at all, and takes one of the four places if it wins.
        /// Null for an ordinary ACT.
        /// </summary>
        internal Func<float> RareChance;

        internal string NameToken => Tokens.Prefix + "ACT_" + Id + "_NAME";
        internal string DescriptionToken => Tokens.Prefix + "ACT_" + Id + "_DESCRIPTION";
    }

    /// <summary>
    /// The pool the ACT menu draws its four options from. To add an ACT: write a state deriving
    /// from BaseKrisAct, then add a line to <see cref="Pool"/>. Registration, tokens and the menu
    /// all read from this list - there is nothing else to touch.
    /// </summary>
    internal static class KrisActs
    {
        /// <summary>How many ACTs the menu offers at once - one per number key 1-4.</summary>
        internal const int MenuSize = 4;

        internal static readonly List<KrisAct> Pool = new List<KrisAct>
        {
            new KrisAct
            {
                Id = "DARKCANDY", Name = "DARK CANDY", Cost = 15f, Kind = ActKind.Item, State = typeof(DarkCandyAct),
                Description = "Heal " + Range(DarkCandyAct.MinHeal, DarkCandyAct.MaxHeal) + " of your max health. Tastes like marshmallows.",
            },
            new KrisAct
            {
                Id = "TVDINNER", Name = "TV DINNER", Cost = 30f, Kind = ActKind.Item, State = typeof(TvDinnerAct),
                Description = "Heal " + Range(TvDinnerAct.MinHeal, TvDinnerAct.MaxHeal) + " of your max health.",
            },
            new KrisAct
            {
                Id = "SCARLIXIR", Name = "SCARLIXIR", Cost = 50f, Kind = ActKind.Item, State = typeof(ScarlixirAct),
                Description = "Heal " + Range(ScarlixirAct.MinHeal, ScarlixirAct.MaxHeal) + " of your max health.",
            },
            new KrisAct
            {
                Id = "PIROUETTE", Name = "PIROUETTE", Cost = 20f, Kind = ActKind.Buff, State = typeof(PirouetteAct),
                Description = "Spin and see what happens. Could be a blessing, could be a stumble.",
            },
            new KrisAct
            {
                Id = "XSLASH", Name = "X-SLASH", Cost = 20f, Kind = ActKind.Attack, State = typeof(XSlashAct),
                Description = "Two crossing cuts for 2x" + Percent(XSlashAct.DamagePerCut) + "% damage.",
            },
            new KrisAct
            {
                Id = "COURAGE", Name = "COURAGE", Cost = 25f, Kind = ActKind.Buff, State = typeof(CourageAct),
                Description = "+" + (int)Buffs.CourageArmorBonus + " armor for you and allies within " + (int)CourageAct.Radius
                              + "m for " + (int)CourageAct.BuffDuration + " seconds.",
            },
            new KrisAct
            {
                Id = "HOLDBREATH", Name = "HOLD BREATH", Cost = 0f, Kind = ActKind.Buff, State = typeof(HoldBreathAct),
                Description = "+" + Percent(Buffs.HoldBreathSpeedBonus) + "% movement speed for " + (int)HoldBreathAct.BuffDuration + " seconds.",
            },
            new KrisAct
            {
                Id = "BANISH", Name = "BANISH", Cost = 50f, Kind = ActKind.Attack, State = typeof(BanishAct),
                Description = "Your SOUL rises and shines for " + Percent(BanishAct.Damage) + "% damage, stunning enemies within " + (int)BanishAct.Radius
                              + "m for " + (int)BanishAct.StunTime + " seconds.",
            },
            new KrisAct
            {
                Id = "UNLEASH", Name = "UNLEASH", Cost = 80f, Kind = ActKind.Attack, State = typeof(UnleashAct),
                Description = "Your SOUL blazes for " + Percent(UnleashAct.Damage) + "% damage, stunning enemies within " + (int)UnleashAct.Radius
                              + "m for " + (int)UnleashAct.StunTime + " seconds. Flying enemies are slammed to the ground and rooted for "
                              + (int)UnleashAct.RootTime + " seconds.",
            },
            new KrisAct
            {
                Id = "PETALFEATHER", Name = "PETAL FEATHER", Cost = 60f, Kind = ActKind.Buff, State = typeof(PetalFeatherAct),
                Description = "Double jump for " + (int)PetalFeatherAct.BuffDuration + " seconds.",
            },
            new KrisAct
            {
                Id = "PROCEED", Name = "PROCEED", Cost = 100f, Kind = ActKind.Attack, State = typeof(ProceedAct),
                Description = "The nearest enemy in front of you dies. Rarely offered.",
                RareChance = () => KrisTuning.ProceedChance / 100f,
            },
        };

        private static int Percent(float fraction) => (int)Math.Round(fraction * 100f);

        private static string Range(float min, float max) => Percent(min) + "-" + Percent(max) + "%";

        internal static void AddTokens()
        {
            foreach (KrisAct act in Pool)
            {
                Tokens.Add(act.NameToken, act.Name);
                Tokens.Add(act.DescriptionToken, act.Description);
            }
        }

        internal static void RegisterStates()
        {
            foreach (KrisAct act in Pool)
            {
                bool added;
                ContentAddition.AddEntityState(act.State, out added);
                if (!added) Log.Warning("ACT state " + act.State.Name + " did not register - it cannot be performed online.");
            }
        }

        /// <summary>The pool entry performed by <paramref name="state"/>, or null.</summary>
        internal static KrisAct For(Type state) => Pool.Find(act => act.State == state);

        private static readonly Random Random = new Random();

        /// <summary>
        /// Up to <see cref="MenuSize"/> distinct ACTs, in random order: every rare ACT that wins
        /// its roll, topped up from a shuffle of the ordinary ones.
        /// </summary>
        internal static KrisAct[] Draw()
        {
            var drawn = new List<KrisAct>();
            var ordinary = new List<KrisAct>();
            foreach (KrisAct act in Pool)
            {
                if (act.RareChance == null) ordinary.Add(act);
                else if (drawn.Count < MenuSize && Random.NextDouble() < act.RareChance()) drawn.Add(act);
            }

            Shuffle(ordinary);
            for (int i = 0; i < ordinary.Count && drawn.Count < MenuSize; i++) drawn.Add(ordinary[i]);

            // Again, so a rare ACT does not always sit in the first slot.
            Shuffle(drawn);
            return drawn.ToArray();
        }

        private static void Shuffle(List<KrisAct> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Next(i + 1);
                KrisAct swap = list[i];
                list[i] = list[j];
                list[j] = swap;
            }
        }
    }
}
