using RoR2;

namespace KrisMod.Modules
{
    /// <summary>
    /// Small helper around <see cref="DamageTypeCombo"/>. Recent RoR2 builds replaced the plain
    /// <see cref="DamageType"/> field on attacks with this packed struct; building it explicitly
    /// keeps every call site readable and avoids relying on implicit conversions.
    /// </summary>
    internal static class KrisDamage
    {
        internal static DamageTypeCombo Of(DamageType type, DamageSource source)
        {
            var combo = default(DamageTypeCombo);
            combo.damageType = type;
            combo.damageSource = source;
            return combo;
        }
    }
}
