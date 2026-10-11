using KrisMod.Components;
using RoR2;
using RoR2.Skills;

namespace KrisMod.Content
{
    /// <summary>
    /// A skill that additionally requires stored TP. The skill icon greys out until the meter is
    /// high enough, which is the only feedback most players need.
    /// </summary>
    public class TensionSkillDef : SkillDef
    {
        /// <summary>Percent of a full bar, like every TP cost - see TensionController.CostToPoints.</summary>
        public float requiredTension = 25f;

        public override bool CanExecute(GenericSkill skillSlot) =>
            base.CanExecute(skillSlot) && HasEnoughTension(skillSlot);

        public override bool IsReady(GenericSkill skillSlot) =>
            base.IsReady(skillSlot) && HasEnoughTension(skillSlot);

        private bool HasEnoughTension(GenericSkill skillSlot)
        {
            CharacterBody body = skillSlot != null ? skillSlot.characterBody : null;
            if (body == null) return false;

            // The meter only carries a real value on the machine that owns the body. Elsewhere,
            // never block: the authority already decided, and its state is what gets replicated.
            if (!body.hasEffectiveAuthority) return true;

            var meter = body.GetComponent<TensionController>();
            return meter != null && meter.CanSpend(requiredTension);
        }
    }
}
