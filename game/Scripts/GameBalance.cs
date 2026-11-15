using Calabash.Combat;
using Calabash.Movement;
using UnityEngine;

namespace Calabash.Game
{
    /// <summary>
    /// Темп и сложность игры. Модуль движения по умолчанию быстрый, как арена-шутер;
    /// в игре Ждан тяжелее: медленнее бегает, короче дэш, меньше зарядов, слайд гаснет.
    /// Огонь прогорает быстрее, убийства кормят меньше. Вызывается при сборке сцен (GameSetup).
    /// </summary>
    public static class GameBalance
    {
        public static void Apply(GameObject player)
        {
            var m = player.GetComponent<ZhdanMovement>();
            if (m != null)
            {
                m.runSpeed = 7.5f;
                m.groundAcceleration = 55f;
                m.groundDeceleration = 50f;
                m.groundOverspeedDeceleration = 30f;
                m.airAcceleration = 28f;
                m.airOverspeedDecay = 3f;
                m.jumpSpeed = 12f;                 // ~1,8 м при гравитации 40

                m.staminaCharges = 2;
                m.staminaRegenPerSecond = 0.45f;   // заряд за ~2,2 с
                m.dashSpeed = 26f;
                m.dashDuration = 0.2f;             // ~5 м
                m.dashExitSpeed = 8f;
                m.dashJumpSpeed = 18f;

                m.slideMinSpeed = 10f;
                m.slideStartBoost = 2f;
                m.slideMaxSpeed = 18f;
                m.slideFriction = 7f;              // слайд гаснет за ~1 с
                m.slideStopSpeed = 4f;

                m.slamBounceBaseSpeed = 13f;
                m.slamBounceSpeedPerMeter = 0.25f;
                m.slamBounceMaxSpeed = 20f;

                m.maxWallJumps = 1;
                m.wallJumpUpSpeed = 11f;
                m.wallJumpAwaySpeed = 8f;
            }
            var c = player.GetComponent<ZhdanCombat>();
            if (c != null)
            {
                c.fuelPerKill = 12f;
                c.fuelPerDamage = 0.04f;
            }
        }
    }
}
