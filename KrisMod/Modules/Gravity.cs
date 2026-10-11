using RoR2;

namespace KrisMod.Modules
{
    /// <summary>
    /// Turning gravity off on a CharacterMotor.
    ///
    /// <c>CharacterMotor.useGravity</c> has an internal setter - the game derives it from
    /// <c>gravityParameters</c>. Granting a "channeled anti-gravity" is the supported public
    /// route, and because it is a counter it composes with anything else that is suspending
    /// gravity at the same time instead of stomping it.
    /// </summary>
    internal static class Gravity
    {
        internal static void Suspend(CharacterMotor motor)
        {
            if (motor == null) return;

            CharacterGravityParameters parameters = motor.gravityParameters;
            parameters.channeledAntiGravityGranterCount++;
            motor.gravityParameters = parameters;
        }

        internal static void Restore(CharacterMotor motor)
        {
            if (motor == null) return;

            CharacterGravityParameters parameters = motor.gravityParameters;
            if (parameters.channeledAntiGravityGranterCount > 0)
            {
                parameters.channeledAntiGravityGranterCount--;
            }
            motor.gravityParameters = parameters;
        }
    }
}
