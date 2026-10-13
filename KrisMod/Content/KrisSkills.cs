using System;
using System.Reflection;
using EntityStates;
using KrisMod.Modules;
using KrisMod.SkillStates;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace KrisMod.Content
{
    /// <summary>Builds Kris's four skills plus the passive, and wires them onto the body.</summary>
    internal static class KrisSkills
    {
        private static readonly FieldInfo SkillFamilyField =
            typeof(GenericSkill).GetField("_skillFamily", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static SkillDef Fight;
        internal static SkillDef Defend;
        internal static SkillDef Soul;
        internal static SkillDef Act;

        internal static void RegisterEntityStates()
        {
            bool added;
            ContentAddition.AddEntityState<KrisSlash>(out added);
            ContentAddition.AddEntityState<KrisDefend>(out added);
            ContentAddition.AddEntityState<SoulAim>(out added);
            ContentAddition.AddEntityState<SoulPull>(out added);
            ContentAddition.AddEntityState<SoulSlash>(out added);
            ContentAddition.AddEntityState<KrisActMenu>(out added);
            ContentAddition.AddEntityState<ActWeaponLock>(out added);
            ContentAddition.AddEntityState<KrisSpawnState>(out added);
            KrisActs.RegisterStates();
        }

        internal static void Build(GameObject bodyPrefab)
        {
            var skillLocator = bodyPrefab.GetComponent<SkillLocator>();
            if (skillLocator == null)
            {
                Log.Error("Kris body has no SkillLocator - skills were not applied.");
                return;
            }

            BuildPassive(skillLocator);

            Fight = BuildPrimary();
            Defend = BuildSecondary();
            Soul = BuildUtility();
            Act = BuildSpecial();

            AssignFamily(skillLocator.primary, "KrisPrimaryFamily", Fight);
            AssignFamily(skillLocator.secondary, "KrisSecondaryFamily", Defend);
            AssignFamily(skillLocator.utility, "KrisUtilityFamily", Soul);
            AssignFamily(skillLocator.special, "KrisSpecialFamily", Act);
        }

        private static void BuildPassive(SkillLocator skillLocator)
        {
            skillLocator.passiveSkill.enabled = true;
            skillLocator.passiveSkill.skillNameToken = Tokens.PassiveName;
            skillLocator.passiveSkill.skillDescriptionToken = Tokens.PassiveDescription;
            skillLocator.passiveSkill.icon = KrisAssets.IconPassive;
        }

        private static SkillDef BuildPrimary()
        {
            // SteppedSkillDef feeds a step index into the state: forehand, backhand, forehand...
            var skill = Create<SteppedSkillDef>("KrisFight", Tokens.PrimaryName, Tokens.PrimaryDescription,
                                                KrisAssets.IconFight, typeof(KrisSlash), "Weapon");
            skill.stepCount = 2;
            skill.stepGraceDuration = 0.5f;
            skill.baseRechargeInterval = 0f;
            skill.baseMaxStock = 1;
            skill.interruptPriority = InterruptPriority.Any;
            skill.isCombatSkill = true;
            skill.mustKeyPress = false;
            skill.cancelSprintingOnActivation = true;
            return skill;
        }

        private static SkillDef BuildSecondary()
        {
            // Held, on "Weapon" so it shuts FIGHT out. Cooldown starts when the shield comes down.
            var skill = Create<SkillDef>("KrisDefend", Tokens.SecondaryName, Tokens.SecondaryDescription,
                                         KrisAssets.IconDefend, typeof(KrisDefend), "Weapon");
            skill.baseRechargeInterval = KrisTuning.DefendCooldown;
            skill.baseMaxStock = 1;
            skill.interruptPriority = InterruptPriority.Skill;
            skill.isCombatSkill = false;
            skill.mustKeyPress = false;
            skill.cancelSprintingOnActivation = true;
            skill.beginSkillCooldownOnSkillEnd = true;
            return skill;
        }

        private static SkillDef BuildUtility()
        {
            // The aim is on "Weapon" so Kris can walk while aiming; it hands the throw to "Body".
            var skill = Create<TensionSkillDef>("KrisSoul", Tokens.UtilityName, Tokens.UtilityDescription,
                                                KrisAssets.IconSoul, typeof(SoulAim), "Weapon");
            skill.requiredTension = KrisTuning.SoulTensionCost;
            skill.baseRechargeInterval = KrisTuning.SoulCooldown;
            skill.baseMaxStock = 1;
            skill.interruptPriority = InterruptPriority.PrioritySkill;
            skill.isCombatSkill = false;
            skill.mustKeyPress = true;
            skill.cancelSprintingOnActivation = false;
            skill.beginSkillCooldownOnSkillEnd = true;
            return skill;
        }

        private static SkillDef BuildSpecial()
        {
            // Opening the menu is free; each ACT has its own TP price, checked in the menu.
            var skill = Create<SkillDef>("KrisAct", Tokens.SpecialName, Tokens.SpecialDescription,
                                         KrisAssets.IconAct, typeof(KrisActMenu), "Body");
            skill.baseRechargeInterval = KrisTuning.ActCooldown;
            skill.baseMaxStock = 1;
            skill.interruptPriority = InterruptPriority.PrioritySkill;
            skill.isCombatSkill = true;
            skill.mustKeyPress = true;
            skill.cancelSprintingOnActivation = true;
            skill.beginSkillCooldownOnSkillEnd = true;
            return skill;
        }

        private static T Create<T>(string name, string nameToken, string descriptionToken, Sprite icon,
                                   Type activationState, string stateMachine) where T : SkillDef
        {
            var skill = ScriptableObject.CreateInstance<T>();
            // SkillDef shadows ScriptableObject.name with a read-only property.
            ((ScriptableObject)skill).name = name;
            skill.skillName = name;
            skill.skillNameToken = nameToken;
            skill.skillDescriptionToken = descriptionToken;
            skill.icon = icon;
            skill.activationState = new SerializableEntityStateType(activationState);
            skill.activationStateMachineName = stateMachine;
            skill.rechargeStock = 1;
            skill.requiredStock = 1;
            skill.stockToConsume = 1;
            skill.resetCooldownTimerOnUse = false;
            skill.fullRestockOnAssign = true;
            skill.dontAllowPastMaxStocks = false;
            skill.canceledFromSprinting = false;
            skill.forceSprintDuringState = false;

            ContentAddition.AddSkillDef(skill);
            return skill;
        }

        private static void AssignFamily(GenericSkill slot, string familyName, params SkillDef[] skills)
        {
            if (slot == null)
            {
                Log.Warning("Missing skill slot for " + familyName);
                return;
            }

            var family = ScriptableObject.CreateInstance<SkillFamily>();
            ((ScriptableObject)family).name = familyName;
            family.variants = new SkillFamily.Variant[skills.Length];

            for (int i = 0; i < skills.Length; i++)
            {
                family.variants[i] = new SkillFamily.Variant
                {
                    skillDef = skills[i],
                    unlockableDef = null,
                    viewableNode = new ViewablesCatalog.Node(skills[i].skillNameToken, false, null),
                };
            }

            ContentAddition.AddSkillFamily(family);

            slot.skillName = skills[0].skillName;
            SetSkillFamily(slot, family);
            slot.SetBaseSkill(skills[0]);
        }

        /// <summary>
        /// GenericSkill only exposes its family read-only; assigning it is standard practice for
        /// custom survivors, so the reflection is isolated here.
        /// </summary>
        private static void SetSkillFamily(GenericSkill slot, SkillFamily family)
        {
            if (SkillFamilyField == null)
            {
                Log.Error("GenericSkill._skillFamily is missing - the game's layout changed.");
                return;
            }
            SkillFamilyField.SetValue(slot, family);
        }
    }
}
