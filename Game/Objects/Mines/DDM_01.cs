using Ow.Game.Events;
using Ow.Game.Movements;
using Ow.Game.Objects;
using Ow.Game.Objects.Players.Managers;
using Ow.Managers;
using Ow.Net.netty.commands;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ow.Game.Objects.Mines
{
    class DDM_01 : Mine
    {
        public DDM_01(Player player, Spacemap spacemap, Position position, int mineTypeId) : base(player, spacemap, position, mineTypeId) { }

        public override void Action(Attackable target)
        {
            var damage = Maths.GetPercentage(target.MaxHitPoints, 20);

            if (target is Player playerTarget)
                damage += Maths.GetPercentage(damage, playerTarget.GetSkillPercentage("Detonation"));

            if (Lance)
                damage += Maths.GetPercentage(damage, 50);

            AttackManager.Damage(Player, target, DamageType.MINE, damage, true, true, false, false);
        }
    }
}
