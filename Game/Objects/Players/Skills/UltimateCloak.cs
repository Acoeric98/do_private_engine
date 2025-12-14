using Ow.Game.Objects.Players.Managers;
using System;
using System.Collections.Generic;

namespace Ow.Game.Objects.Players.Skills
{
    class UltimateCloak : Skill
    {
        public override string LootId { get => SkillManager.SPEARHEAD_ULTIMATE_CLOAK; }

        public override int Duration { get => TimeManager.SPEARHEAD_ULTIMATE_CLOAK_DURATION; }
        public override int Cooldown { get => TimeManager.SPEARHEAD_ULTIMATE_CLOAK_COOLDOWN; }

        public UltimateCloak(Player player) : base(player) { }

        public override void Tick()
        {
            if (Active && (cooldown.AddMilliseconds(Duration) < DateTime.Now || !Player.Invisible))
                Disable();
        }

        public override void Send()
        {
            var spearheadIds = new List<int> { Ship.SPEARHEAD, Ship.SPEARHEAD_ELITE, Ship.SPEARHEAD_VETERAN };

            if (spearheadIds.Contains(Player.Ship.Id) && (cooldown.AddMilliseconds(Duration + Cooldown) < DateTime.Now || Player.Storage.GodMode))
            {
                Player.CpuManager.EnableCloak();

                Player.SendCooldown(LootId, Duration, true);
                Active = true;
                cooldown = DateTime.Now;
            }
        }

        public override void Disable()
        {
            Player.CpuManager.DisableCloak();
            Player.SendCooldown(LootId, Cooldown);
            Active = false;
        }
    }
}
