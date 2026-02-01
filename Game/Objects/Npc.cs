using Ow.Game.Movements;
using Ow.Game.Objects.AI;
using Ow.Game.Objects.Collectables;
using Ow.Game.Objects.Players;
using Ow.Game.Objects.Players.Managers;
using Ow.Managers;
using Ow.Net.netty.commands;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ow.Game.Objects
{
    class Npc : Character
    {
        private const int CubikonShipId = 80;
        private const int ProtegitShipId = 81;
        private const int ProtegitWaveSize = 15;
        private const int ProtegitWaveRespawnThreshold = 5;

        public NpcAI NpcAI { get; set; }
        public bool Attacking = false;
        public bool AllowRespawn { get; private set; }
        public DateTime? DespawnTime { get; private set; }
        public int? DefenderOwnerId { get; private set; }

        public Npc(int id, Ship ship, Spacemap spacemap, Position position, bool? allowRespawn = null, DateTime? despawnTime = null) : base(id, ship.Name, 0, ship, position, spacemap, GameManager.GetClan(0))
        {
            Spacemap.AddCharacter(this);

            ShieldAbsorption = 0.8;

            Damage = ship.Damage;
            MaxHitPoints = ship.BaseHitpoints;
            CurrentHitPoints = MaxHitPoints;
            MaxShieldPoints = ship.BaseShieldPoints;
            CurrentShieldPoints = MaxShieldPoints;

            AllowRespawn = allowRespawn ?? ship.Respawnable;
            if (ship.Id == ProtegitShipId)
                AllowRespawn = false;
            DespawnTime = despawnTime;

            NpcAI = new NpcAI(this);

            Program.TickManager.AddTick(this);
        }

        public override void Tick()
        {
            if (HandleDespawn())
                return;

            Movement.ActualPosition(this);
            NpcAI.TickAI();
            CheckShieldPointsRepair();
            Storage.Tick();
            RefreshAttackers();

            if (Attacking)
                Attack();
        }

        private bool HandleDespawn()
        {
            if (!DespawnTime.HasValue || Destroyed)
                return false;

            if (DespawnTime.Value > DateTime.Now)
                return false;

            ForceDespawn();
            return true;
        }

        private void ForceDespawn()
        {
            if (Destroyed)
                return;

            MainAttacker = null;
            Attackers.Clear();
            AllowRespawn = false;
            DespawnTime = null;
            Destroy(null, DestructionType.MISC);
        }

        public DateTime lastAttackTime = new DateTime();
        public void Attack()
        {
            var target = SelectedCharacter;

            if (!TargetDefinition(target, false)) return;

            if (target is Player player && player.AttackManager.EmpCooldown.AddMilliseconds(TimeManager.EMP_DURATION) > DateTime.Now)
                return;

            var missProbability = 0.1;
            if (target is Player targetPlayer)
            {
                if (targetPlayer.Storage.underPLD8)
                    missProbability += 0.5;

                missProbability += targetPlayer.EvasionChance;
            }

            missProbability = Math.Min(1.0, missProbability);
            var damage = AttackManager.RandomizeDamage(Damage, missProbability);

            if (lastAttackTime.AddSeconds(1) < DateTime.Now)
            {
                if (target is Player && (target as Player).Storage.Spectrum)
                    damage -= Maths.GetPercentage(damage, 50);

                int damageShd = 0, damageHp = 0;

                double shieldAbsorb = System.Math.Abs(target.ShieldAbsorption - 0);

                if (shieldAbsorb > 1)
                    shieldAbsorb = 1;

                if ((target.CurrentShieldPoints - damage) >= 0)
                {
                    damageShd = (int)(damage * shieldAbsorb);
                    damageHp = damage - damageShd;
                }
                else
                {
                    int newDamage = damage - target.CurrentShieldPoints;
                    damageShd = target.CurrentShieldPoints;
                    damageHp = (int)(newDamage + (damageShd * shieldAbsorb));
                }

                if ((target.CurrentHitPoints - damageHp) < 0)
                {
                    damageHp = target.CurrentHitPoints;
                }

                if (target is Player && !(target as Player).Attackable())
                {
                    damage = 0;
                    damageShd = 0;
                    damageHp = 0;
                }

                if (target is Player && (target as Player).Storage.Sentinel)
                    damageShd -= Maths.GetPercentage(damageShd, 30);

                var laserRunCommand = AttackLaserRunCommand.write(Id, target.Id, 0, false, false);
                SendCommandToInRangePlayers(laserRunCommand);

                if (damage == 0)
                {
                    var attackMissedCommandToInRange = AttackMissedCommand.write(new AttackTypeModule(AttackTypeModule.LASER), target.Id, 1);
                    SendCommandToInRangePlayers(attackMissedCommandToInRange);
                }
                else
                {
                    var attackHitCommand =
                        AttackHitCommand.write(new AttackTypeModule(AttackTypeModule.LASER), Id,
                             target.Id, target.CurrentHitPoints,
                             target.CurrentShieldPoints, target.CurrentNanoHull,
                             damage > damageShd ? damage : damageShd, false);

                    SendCommandToInRangePlayers(attackHitCommand);
                }

                if (damageHp >= target.CurrentHitPoints || target.CurrentHitPoints <= 0)
                    target.Destroy(this, DestructionType.NPC);
                else
                    target.CurrentHitPoints -= damageHp;

                target.CurrentShieldPoints -= damageShd;
                target.LastCombatTime = DateTime.Now;

                lastAttackTime = DateTime.Now;

                target.UpdateStatus();
            }
        }

        public DateTime lastShieldRepairTime = new DateTime();
        private void CheckShieldPointsRepair()
        {
            if (LastCombatTime.AddSeconds(10) >= DateTime.Now || lastShieldRepairTime.AddSeconds(1) >= DateTime.Now || CurrentShieldPoints == MaxShieldPoints) return;

            int repairShield = MaxShieldPoints / 10;
            CurrentShieldPoints += repairShield;
            UpdateStatus();

            lastShieldRepairTime = DateTime.Now;
        }

        public void Respawn()
        {
            LastCombatTime = DateTime.Now.AddSeconds(-999);
            CurrentHitPoints = MaxHitPoints;
            CurrentShieldPoints = MaxShieldPoints;
            SetPosition(Position.Random(Spacemap, 0, 20800, 0, 12800));
            Spacemap.AddCharacter(this);
            Attackers.Clear();
            MainAttacker = null;
            Destroyed = false;
            AllowRespawn = Ship.Respawnable && Ship.Id != ProtegitShipId;
            DespawnTime = null;
            DefenderOwnerId = null;

        }

        public void TrySpawnDefenders(Player attacker)
        {
            if (Ship?.Id != CubikonShipId || Spacemap == null)
                return;

            var defenderShip = GameManager.GetShip(ProtegitShipId);
            if (defenderShip == null)
                return;

            var defenderCount = Spacemap.Characters.Values.OfType<Npc>()
                .Count(npc => npc.Ship?.Id == ProtegitShipId && npc.DefenderOwnerId == Id);

            if (defenderCount > ProtegitWaveRespawnThreshold)
                return;

            for (int i = 0; i < ProtegitWaveSize; i++)
            {
                var spawnPosition = ClampToMap(Position.GetPosOnCircle(Position, Randoms.random.Next(300, 700)));
                var defender = new Npc(Randoms.CreateRandomID(), defenderShip, Spacemap, spawnPosition, false, DateTime.Now.AddMinutes(15));
                defender.DefenderOwnerId = Id;
                if (attacker != null)
                    defender.ReceiveAttack(attacker);
            }
        }

        public void HandleDefenderWaveOnDeath(DestructionType destructionType)
        {
            if (Ship?.Id != ProtegitShipId || Spacemap == null || DefenderOwnerId == null)
                return;

            if (destructionType == DestructionType.MISC)
                return;

            var cubikon = Spacemap.Characters.Values.OfType<Npc>()
                .FirstOrDefault(npc => npc.Id == DefenderOwnerId.Value && npc.Ship?.Id == CubikonShipId && !npc.Destroyed);

            if (cubikon == null)
                return;

            var attacker = cubikon.MainAttacker as Player;
            cubikon.TrySpawnDefenders(attacker);
        }

        private Position ClampToMap(Position position)
        {
            var min = Spacemap.Limits[0];
            var max = Spacemap.Limits[1];

            var clampedX = Math.Min(Math.Max(position.X, min.X), max.X);
            var clampedY = Math.Min(Math.Max(position.Y, min.Y), max.Y);

            return new Position(clampedX, clampedY);
        }

        public void ReceiveAttack(Character character)
        {
            Selected = character;
            Attacking = true;
        }

        public override int Speed
        {
            get
            {
                var value = Ship.BaseSpeed;

                if (Storage.underR_IC3)
                    value -= value;

                return value;
            }
        }

        public override byte[] GetShipCreateCommand()
        {
            return ShipCreateCommand.write(
                Id,
                Convert.ToString(Ship.Id),
                3,
                "",
                Ship.Name,
                Position.X,
                Position.Y,
                FactionId,
                0,
                0,
                false,
                new ClanRelationModule(ClanRelationModule.AT_WAR),
                0,
                false,
                true,
                false,
                ClanRelationModule.AT_WAR,
                ClanRelationModule.AT_WAR,
                new List<VisualModifierCommand>(),
                new class_11d(class_11d.DEFAULT)
                );
        }
    }
}
