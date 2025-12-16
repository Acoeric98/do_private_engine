using Ow.Game.Movements;
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
    class Pet : Character
    {
        private const int KAMIKAZE_COOLDOWN_SECONDS = 30;

        private static readonly HashSet<short> DisabledGears = new HashSet<short>
        {
            PetGearTypeModule.AUTO_RESOURCE_COLLECTION,
            PetGearTypeModule.ENEMY_LOCATOR,
            PetGearTypeModule.RESOURCE_LOCATOR,
            PetGearTypeModule.RESOURCE_SYSTEM_LOCATOR,
            PetGearTypeModule.TRADE_POD,
            PetGearTypeModule.TRADE_MODULE,
            PetGearTypeModule.HP_LINK,
            PetGearTypeModule.SHIELD_SACRIFICE,
            PetGearTypeModule.COMBO_SHIP_REPAIR,
            PetGearTypeModule.COMBO_GUARD
        };

        public Player Owner { get; set; }

        public override int Speed
        {
            get
            {
                return (int)(Owner.Speed * 1.25);
            }
        }

        public bool Activated = false;
        public bool GuardModeActive = false;
        public bool AutoLootActive = false;
        public bool ResourceCollectorActive = false;
        public bool EnemyLocatorActive = false;
        public bool ResourceLocatorActive = false;
        public bool TradePodActive = false;
        public bool RepairActive = false;
        public bool KamikazeActive = false;
        public bool ComboShipRepairActive = false;
        public bool ComboGuardActive = false;
        public bool ShieldSacrificeActive = false;
        public bool ResourceSystemLocatorActive = false;
        public bool HpLinkActive = false;
        public short GearId = PetGearTypeModule.PASSIVE;
        private Character _kamikazeTarget;
        private DateTime _kamikazeDetonationTime = DateTime.MinValue;
        private bool _kamikazeArming;
        private DateTime _kamikazeCooldownEndTime = DateTime.MinValue;
        private bool _kamikazeReadyNotified = true;
        private bool _kamikazeGearEnabled = true;
        private Collectable _activeCollectableTarget;
        private Character _shieldSacrificeTarget;
        private DateTime _shieldSacrificeDetonationTime = DateTime.MinValue;
        private bool _shieldSacrificeArming;

        private class PetAbility
        {
            public short GearType { get; }
            public string Name { get; }
            public string Description { get; }

            public PetAbility(short gearType, string name, string description)
            {
                GearType = gearType;
                Name = name;
                Description = description;
            }
        }

        private readonly Dictionary<short, PetAbility> _abilities = new Dictionary<short, PetAbility>();

        private void RegisterAbility(short gearType, string name, string description)
        {
            _abilities[gearType] = new PetAbility(gearType, name, description);
        }

        private DateTime _comboShipRepairEndTime = DateTime.MinValue;
        private DateTime _lastComboShipRepairTick = DateTime.MinValue;
        private DateTime _petRepairEndTime = DateTime.MinValue;
        private DateTime _lastPetRepairTick = DateTime.MinValue;
        private DateTime _hpLinkEndTime = DateTime.MinValue;
        private DateTime _hpLinkCooldownEndTime = DateTime.MinValue;
        private int _lastOwnerHitpoints;
        private DateTime _lastLocatorPing = DateTime.MinValue;
        private bool _shieldSacrificeTriggered = false;

        private void AddHpLinkVisuals()
        {
            Owner.AddVisualModifier(VisualModifierCommand.GREEN_GLOW, 0, "", 0, true);
            AddVisualModifier(VisualModifierCommand.GREEN_GLOW, 0, "", 0, true);
        }

        private void RemoveHpLinkVisuals()
        {
            Owner.RemoveVisualModifier(VisualModifierCommand.GREEN_GLOW);
            RemoveVisualModifier(VisualModifierCommand.GREEN_GLOW);
        }

        private void AddShieldSacrificeVisuals()
        {
            Owner.AddVisualModifier(VisualModifierCommand.RED_GLOW, 0, "", 0, true);
            AddVisualModifier(VisualModifierCommand.RED_GLOW, 0, "", 0, true);
        }

        private void RemoveShieldSacrificeVisuals()
        {
            Owner.RemoveVisualModifier(VisualModifierCommand.RED_GLOW);
            RemoveVisualModifier(VisualModifierCommand.RED_GLOW);
        }

        public Pet(Player player) : base(Randoms.CreateRandomID(), "P.E.T 15", player.FactionId, GameManager.GetShip(22), player.Position, player.Spacemap, player.Clan)
        {
            Name = player.PetName;
            Owner = player;

            ShieldAbsorption = 0.8;
            Damage = 15000;
            CurrentHitPoints = 50000;
            MaxHitPoints = 50000;
            MaxShieldPoints = 50000;
            CurrentShieldPoints = MaxShieldPoints;

            InitializeAbilities();
        }

        public override void Tick()
        {
            if (Activated)
            {
                HandleKamikazeCooldownReady();
                CheckShieldPointsRepair();
                CheckGuardMode();
                var collecting = CheckAutoLoot();
                CheckComboShipRepair();
                CheckPetRepair();
                CheckHpLink();
                CheckShieldSacrifice();
                CheckKamikaze();
                CheckLocators();
                if (!collecting && !IsAbilityNavigating())
                    Follow(Owner);
                Movement.ActualPosition(this);
            }
        }

        private bool IsGearDisabled(short gearId) => DisabledGears.Contains(gearId);

        public bool CheckAutoLoot()
        {
            if (!AutoLootActive && !ResourceCollectorActive)
            {
                _activeCollectableTarget = null;
                return false;
            }

            var range = ResourceCollectorActive ? 3000 : 700;
            if (_activeCollectableTarget == null || _activeCollectableTarget.Disposed || _activeCollectableTarget.Character != null || Position.DistanceTo(_activeCollectableTarget.Position) > range)
            {
                _activeCollectableTarget = Spacemap.Objects.Values.OfType<Collectable>()
                    .Where(x => !x.Disposed && x.Character == null && Position.DistanceTo(x.Position) <= range)
                    .OrderBy(x => Position.DistanceTo(x.Position))
                    .FirstOrDefault();
            }

            if (_activeCollectableTarget == null) return false;

            var distance = Position.DistanceTo(_activeCollectableTarget.Position);
            if (distance <= 200)
            {
                _activeCollectableTarget.Collect(this);
                _activeCollectableTarget = null;
            }
            else
            {
                Movement.Move(this, _activeCollectableTarget.Position);
                return true;
            }

            return Collecting;
        }

        public DateTime lastShieldRepairTime = new DateTime();
        private void CheckShieldPointsRepair()
        {
            if (LastCombatTime.AddSeconds(10) >= DateTime.Now || lastShieldRepairTime.AddSeconds(1) >= DateTime.Now || CurrentShieldPoints == MaxShieldPoints) return;

            int repairShield = MaxShieldPoints / 25;
            CurrentShieldPoints += repairShield;
            UpdateStatus();

            lastShieldRepairTime = DateTime.Now;
        }

        public DateTime lastAttackTime = new DateTime();
        public DateTime lastRSBAttackTime = new DateTime();
        public void CheckGuardMode()
        {
            if (GuardModeActive)
            {
                foreach (var enemy in Owner.InRangeCharacters.Values)
                {
                    if (Owner.SelectedCharacter != null && Owner.SelectedCharacter != this)
                    {
                        if ((Owner.AttackingOrUnderAttack(5) || Owner.LastAttackTime(5)) || ((enemy is Player && (enemy as Player).LastAttackTime(5)) && enemy.SelectedCharacter == Owner))
                            Attack(Owner.SelectedCharacter);
                    }
                    else
                    {
                        if (((enemy is Player && (enemy as Player).LastAttackTime(5)) && enemy.SelectedCharacter == Owner))
                            Attack(enemy);
                    }
                }
            }
        }

        private void Attack(Character target)
        {
            if (!Owner.TargetDefinition(target, false)) return;
            if ((Owner.Settings.InGameSettings.selectedLaser == AmmunitionManager.RSB_75 ? lastRSBAttackTime : lastAttackTime).AddSeconds(Owner.Settings.InGameSettings.selectedLaser == AmmunitionManager.RSB_75 ? 3 : 1) < DateTime.Now)
            {
                int damageShd = 0, damageHp = 0;

                if (target is Spaceball)
                {
                    var spaceball = target as Spaceball;
                    spaceball.AddDamage(this, Damage);
                }

                double shieldAbsorb = System.Math.Abs(target.ShieldAbsorption - 1);

                if (shieldAbsorb > 1)
                    shieldAbsorb = 1;

                if ((target.CurrentShieldPoints - Damage) >= 0)
                {
                    damageShd = (int)(Damage * shieldAbsorb);
                    damageHp = Damage - damageShd;
                }
                else
                {
                    int newDamage = Damage - target.CurrentShieldPoints;
                    damageShd = target.CurrentShieldPoints;
                    damageHp = (int)(newDamage + (damageShd * shieldAbsorb));
                }

                if ((target.CurrentHitPoints - damageHp) < 0)
                {
                    damageHp = target.CurrentHitPoints;
                }

                if (target is Player && !(target as Player).Attackable())
                {
                    Damage = 0;
                    damageShd = 0;
                    damageHp = 0;
                }

                if (Invisible)
                {
                    Invisible = false;
                    string cloakPacket = "0|n|INV|" + Id + "|0";
                    SendPacketToInRangePlayers(cloakPacket);
                }

                if (target is Player && (target as Player).Storage.Sentinel)
                    damageShd -= Maths.GetPercentage(damageShd, 30);

                var laserRunCommand = AttackLaserRunCommand.write(Id, target.Id, Owner.AttackManager.GetSelectedLaser(), false, false);
                SendCommandToInRangePlayers(laserRunCommand);

                var attackHitCommand =
                        AttackHitCommand.write(new AttackTypeModule(AttackTypeModule.LASER), Id,
                                             target.Id, target.CurrentHitPoints,
                                             target.CurrentShieldPoints, target.CurrentNanoHull,
                                             Damage > damageShd ? Damage : damageShd, false);

                SendCommandToInRangePlayers(attackHitCommand);

                if (damageHp >= target.CurrentHitPoints || target.CurrentHitPoints == 0)
                    target.Destroy(this, DestructionType.PET);
                else
                    target.CurrentHitPoints -= damageHp;

                target.CurrentShieldPoints -= damageShd;
                target.LastCombatTime = DateTime.Now;

                if (Owner.Settings.InGameSettings.selectedLaser == AmmunitionManager.RSB_75)
                    lastRSBAttackTime = DateTime.Now;
                else
                    lastAttackTime = DateTime.Now;

                target.UpdateStatus();
            }
        }

        private void CheckComboShipRepair()
        {
            if (!ComboShipRepairActive) return;

            if (_comboShipRepairEndTime <= DateTime.Now)
            {
                ComboShipRepairActive = false;
                return;
            }

            if (_lastComboShipRepairTick.AddSeconds(1) <= DateTime.Now)
            {
                var healAmount = 25000;
                var missingHp = Owner.MaxHitPoints - Owner.CurrentHitPoints;
                if (missingHp > 0)
                {
                    Owner.CurrentHitPoints += Math.Min(healAmount, missingHp);
                    Owner.UpdateStatus();
                }

                _lastComboShipRepairTick = DateTime.Now;
            }
        }

        private void CheckPetRepair()
        {
            if (!RepairActive) return;

            if (_petRepairEndTime <= DateTime.Now)
            {
                RepairActive = false;
                return;
            }

            if (_lastPetRepairTick.AddSeconds(1) <= DateTime.Now)
            {
                var healAmount = 12000;
                var missingHp = MaxHitPoints - CurrentHitPoints;
                if (missingHp > 0)
                {
                    CurrentHitPoints += Math.Min(healAmount, missingHp);
                    UpdateStatus();
                }

                _lastPetRepairTick = DateTime.Now;
            }
        }

        private void CheckHpLink()
        {
            if (!HpLinkActive)
            {
                _lastOwnerHitpoints = Owner.CurrentHitPoints;
                return;
            }

            if (_hpLinkEndTime <= DateTime.Now)
            {
                HpLinkActive = false;
                _hpLinkCooldownEndTime = DateTime.Now.AddSeconds(240);
                RemoveHpLinkVisuals();
                Owner.SendPacket("0|A|STM|msg_pet_hp_link_deactivated");
                return;
            }

            if (_lastOwnerHitpoints > 0 && Owner.CurrentHitPoints < _lastOwnerHitpoints)
            {
                var redirectedDamage = _lastOwnerHitpoints - Owner.CurrentHitPoints;
                var transferable = Math.Min(redirectedDamage, CurrentHitPoints);
                Owner.CurrentHitPoints += transferable;
                CurrentHitPoints -= transferable;

                if (CurrentHitPoints <= 0)
                {
                    CurrentHitPoints = 0;
                    UpdateStatus();
                    Deactivate(true, true);
                    HpLinkActive = false;
                    RemoveHpLinkVisuals();
                    return;
                }

                Owner.UpdateStatus();
                UpdateStatus();
            }

            _lastOwnerHitpoints = Owner.CurrentHitPoints;
        }

        private void CheckKamikaze()
        {
            if (!KamikazeActive)
            {
                ResetKamikazeState();
                return;
            }

            if (IsKamikazeOnCooldown())
            {
                ResetKamikazeState();
                return;
            }

            var ownerCritical = Owner.CurrentHitPoints < (Owner.MaxHitPoints * 0.2);
            var petCritical = CurrentHitPoints < (MaxHitPoints * 0.2);

            if (!ownerCritical && !petCritical)
            {
                ResetKamikazeState();
                return;
            }

            if (_kamikazeTarget == null || _kamikazeTarget.Destroyed || _kamikazeTarget.Spacemap != Spacemap)
            {
                var selectedTarget = Owner.SelectedCharacter;

                if (selectedTarget == this || selectedTarget == Owner)
                    selectedTarget = null;

                _kamikazeTarget = selectedTarget ?? Owner.InRangeCharacters.Values.FirstOrDefault(x => x != Owner && x != this);
                _kamikazeArming = false;
                _kamikazeDetonationTime = DateTime.MinValue;
            }

            if (_kamikazeTarget == null) return;

            var distance = Position.DistanceTo(_kamikazeTarget.Position);
            if (distance > 450)
            {
                _kamikazeArming = false;
                _kamikazeDetonationTime = DateTime.MinValue;
                Movement.Move(this, _kamikazeTarget.Position);
                return;
            }

            if (!_kamikazeArming)
            {
                StopMovement();
                _kamikazeArming = true;
                _kamikazeDetonationTime = DateTime.Now.AddSeconds(2);
                return;
            }

            if (_kamikazeDetonationTime <= DateTime.Now)
            {
                var ownerSelectedCharacter = Owner.SelectedCharacter;
                var ownerWasAttacking = Owner.AttackManager.Attacking;

                foreach (var character in Owner.InRangeCharacters.Values)
                {
                    if (character == Owner || character == this) continue;

                    if (Position.DistanceTo(character.Position) <= 450)
                    {
                        var damage = 75000;
                        character.CurrentHitPoints -= Math.Min(damage, character.CurrentHitPoints);
                        character.UpdateStatus();

                        if (character.CurrentHitPoints <= 0)
                            character.Destroy(Owner, DestructionType.PLAYER);
                    }
                }

                _kamikazeCooldownEndTime = DateTime.Now.AddSeconds(KAMIKAZE_COOLDOWN_SECONDS);
                _kamikazeReadyNotified = false;
                UpdateKamikazeGearAvailability(false);
                Deactivate(true, true);
                ResetKamikazeState();

                if (Owner.SelectedCharacter == null
                    && ownerSelectedCharacter != null
                    && !ownerSelectedCharacter.Destroyed
                    && ownerSelectedCharacter != Owner
                    && ownerSelectedCharacter.Spacemap == Owner.Spacemap)
                {
                    Owner.SelectEntity(ownerSelectedCharacter.Id);

                    if (ownerWasAttacking && Owner.SelectedCharacter == ownerSelectedCharacter)
                    {
                        Owner.EnableAttack(Owner.Settings.InGameSettings.selectedLaser);
                    }
                }
            }
        }

        private void CheckLocators()
        {
            if (!EnemyLocatorActive && !ResourceLocatorActive && !ResourceSystemLocatorActive) return;

            if (_lastLocatorPing.AddSeconds(3) > DateTime.Now) return;

            if (EnemyLocatorActive)
            {
                var npcs = Spacemap.Characters.Values.OfType<Npc>().Count(x => Position.DistanceTo(x.Position) <= 2000);
                Owner.SendPacket($"0|A|STD|npc_locator:{npcs}");
            }

            if (ResourceLocatorActive || ResourceSystemLocatorActive)
            {
                var range = ResourceSystemLocatorActive ? 5000 : 2000;
                var resources = Spacemap.Objects.Values.OfType<Collectable>().Count(x => Position.DistanceTo(x.Position) <= range);
                Owner.SendPacket($"0|A|STD|resource_locator:{resources}");
            }

            _lastLocatorPing = DateTime.Now;
        }

        private void CheckShieldSacrifice()
        {
            if (!ShieldSacrificeActive)
            {
                RemoveShieldSacrificeVisuals();
                ResetShieldSacrificeState();
                return;
            }

            if (_shieldSacrificeTarget == null || _shieldSacrificeTarget.Destroyed || _shieldSacrificeTarget.Spacemap != Spacemap)
            {
                _shieldSacrificeTarget = Owner.SelectedCharacter is Player player && player.Clan == Owner.Clan ? player : null;
                _shieldSacrificeArming = false;
                _shieldSacrificeDetonationTime = DateTime.MinValue;
            }

            if (_shieldSacrificeTarget == null) return;

            var distance = Position.DistanceTo(_shieldSacrificeTarget.Position);
            if (distance > 450)
            {
                _shieldSacrificeArming = false;
                _shieldSacrificeDetonationTime = DateTime.MinValue;
                Movement.Move(this, _shieldSacrificeTarget.Position);
                return;
            }

            if (!_shieldSacrificeArming)
            {
                StopMovement();
                _shieldSacrificeArming = true;
                _shieldSacrificeDetonationTime = DateTime.Now.AddSeconds(2);
                return;
            }

            if (_shieldSacrificeDetonationTime <= DateTime.Now)
            {
                var transferredShield = Owner.CurrentShieldPoints;
                if (transferredShield > 0)
                {
                    _shieldSacrificeTarget.CurrentShieldPoints = Math.Min(_shieldSacrificeTarget.MaxShieldPoints, _shieldSacrificeTarget.CurrentShieldPoints + transferredShield);
                    Owner.CurrentShieldPoints = 0;
                    _shieldSacrificeTarget.UpdateStatus();
                    Owner.UpdateStatus();
                }

                Deactivate(true, true);
                ShieldSacrificeActive = false;
                RemoveShieldSacrificeVisuals();
                ResetShieldSacrificeState();
            }
        }

        private void HandleTradeModule()
        {
            Owner.SendPacket("0|A|STD|trade_module:activated");
        }

        public void Activate()
        {
            if (!Activated && !Owner.Settings.InGameSettings.petDestroyed)
            {
                Activated = true;

                CurrentHitPoints = 2500;

                SetPosition(Owner.Position);
                Spacemap = Owner.Spacemap;
                Invisible = Owner.Invisible;

                Owner.SendPacket("0|A|STM|msg_pet_activated");

                Initialization(GearId);

                Spacemap.AddCharacter(this);
                Program.TickManager.AddTick(this);
            }
            else
            {
                Deactivate();
            }
        }

        public void RepairDestroyed()
        {
            if (Owner.Settings.InGameSettings.petDestroyed)
            {
                var cost = Owner.Premium ? 0 : 250;

                if (Owner.Data.uridium >= cost)
                {
                    Destroyed = false;
                    Owner.ChangeData(DataType.URIDIUM, cost, ChangeType.DECREASE);
                    Owner.SendCommand(PetRepairCompleteCommand.write());
                    Owner.Settings.InGameSettings.petDestroyed = false;
                    GearId = PetGearTypeModule.PASSIVE;
                    QueryManager.SavePlayer.Settings(Owner, "inGameSettings", Owner.Settings.InGameSettings);
                } else Owner.SendPacket("0|A|STM|ttip_pet_repair_disabled_through_money");
            }
        }

        public void Deactivate(bool direct = false, bool destroyed = false)
        {
            if (Activated)
            {
                if (LastCombatTime.AddSeconds(10) < DateTime.Now || direct)
                {
                    RemoveHpLinkVisuals();
                    RemoveShieldSacrificeVisuals();
                    Owner.SendPacket("0|PET|D");

                    if (destroyed)
                    {
                        Owner.Settings.InGameSettings.petDestroyed = true;
                        QueryManager.SavePlayer.Settings(Owner, "inGameSettings", Owner.Settings.InGameSettings);

                        Owner.SendPacket("0|PET|Z");
                        CurrentShieldPoints = 0;
                        UpdateStatus();

                        Owner.SendCommand(PetInitializationCommand.write(true, true, false));
                        Owner.SendCommand(PetUIRepairButtonCommand.write(true, 250));
                    }
                    else Owner.SendPacket("0|A|STM|msg_pet_deactivated");

                    Activated = false;

                    Deselection();
                    Spacemap.RemoveCharacter(this);
                    InRangeCharacters.Clear();
                    Program.TickManager.RemoveTick(this);
                }
                else
                {
                    Owner.SendPacket("0|A|STM|msg_pet_in_combat");
                }
            }
        }

        private void Initialization(short gearId = PetGearTypeModule.PASSIVE)
        {
            HandleKamikazeCooldownReady();
            Owner.SendCommand(PetStatusCommand.write(Id, 15, 27000000, 27000000, CurrentHitPoints, MaxHitPoints, CurrentShieldPoints, MaxShieldPoints, 50000, 50000, Speed, Name));
            foreach (var ability in _abilities.Values.Where(ability => !IsGearDisabled(ability.GearType)))
            {
                // Ensure the client treats every gear as owned/available by reporting at least one copy.
                var enabled = ability.GearType != PetGearTypeModule.KAMIKAZE || !IsKamikazeOnCooldown();
                Owner.SendCommand(PetGearAddCommand.write(new PetGearTypeModule(ability.GearType), 3, 1, enabled));

                if (ability.GearType == PetGearTypeModule.KAMIKAZE)
                {
                    _kamikazeGearEnabled = enabled;
                }
            }
            SwitchGear(gearId);
        }

        private void Follow(Character character)
        {
            var distance = Position.DistanceTo(character.Position);
            if (distance < 450 && character.Moving) return;

            if (character.Moving)
            {
                Movement.Move(this, character.Position);
            }
            else if (Math.Abs(distance - 300) > 250 && !Moving)
                Movement.Move(this, Position.GetPosOnCircle(character.Position, 250));
        }

        public void SwitchGear(short gearId)
        {
            if (!Activated)
                Activate();

            if (IsGearDisabled(gearId))
            {
                GearId = PetGearTypeModule.PASSIVE;
                Owner.SendCommand(PetGearSelectCommand.write(new PetGearTypeModule(GearId), new List<int>()));
                return;
            }

            if (gearId == PetGearTypeModule.KAMIKAZE && IsKamikazeOnCooldown())
            {
                Owner.SendPacket("0|A|STD|A kamikaze modul újratöltés alatt áll.");
                Owner.SendCommand(PetGearSelectCommand.write(new PetGearTypeModule(GearId), new List<int>()));
                return;
            }

            ResetState();

            switch (gearId)
            {
                case PetGearTypeModule.PASSIVE:
                    GuardModeActive = false;
                    break;
                case PetGearTypeModule.GUARD:
                    GuardModeActive = true;
                    break;
                case PetGearTypeModule.AUTO_LOOT:
                    AutoLootActive = true;
                    break;
                case PetGearTypeModule.AUTO_RESOURCE_COLLECTION:
                    ResourceCollectorActive = true;
                    break;
                case PetGearTypeModule.ENEMY_LOCATOR:
                    EnemyLocatorActive = true;
                    break;
                case PetGearTypeModule.RESOURCE_LOCATOR:
                    ResourceLocatorActive = true;
                    break;
                case PetGearTypeModule.TRADE_POD:
                    TradePodActive = true;
                    break;
                case PetGearTypeModule.REPAIR_PET:
                    RepairActive = true;
                    _petRepairEndTime = DateTime.Now.AddSeconds(15);
                    _lastPetRepairTick = DateTime.MinValue;
                    break;
                case PetGearTypeModule.KAMIKAZE:
                    KamikazeActive = true;
                    break;
                case PetGearTypeModule.COMBO_SHIP_REPAIR:
                    ComboShipRepairActive = true;
                    _comboShipRepairEndTime = DateTime.Now.AddSeconds(5);
                    _lastComboShipRepairTick = DateTime.MinValue;
                    Owner.SendPacket("0|A|STM|msg_pet_combo_ship_repair_activated");
                    break;
                case PetGearTypeModule.COMBO_GUARD:
                    ComboGuardActive = true;
                    if (!_shieldSacrificeTriggered)
                    {
                        var shieldBoost = Maths.GetPercentage(Owner.MaxShieldPoints, 20);
                        Owner.CurrentShieldPoints = Math.Min(Owner.MaxShieldPoints, Owner.CurrentShieldPoints + shieldBoost);
                        Owner.UpdateStatus();
                        _shieldSacrificeTriggered = true;
                    }
                    break;
                case PetGearTypeModule.SHIELD_SACRIFICE:
                    ShieldSacrificeActive = true;
                    AddShieldSacrificeVisuals();
                    break;
                case PetGearTypeModule.TRADE_MODULE:
                    TradePodActive = true;
                    HandleTradeModule();
                    break;
                case PetGearTypeModule.RESOURCE_SYSTEM_LOCATOR:
                    ResourceSystemLocatorActive = true;
                    break;
                case PetGearTypeModule.HP_LINK:
                    if (_hpLinkCooldownEndTime <= DateTime.Now)
                    {
                        HpLinkActive = true;
                        _hpLinkEndTime = DateTime.Now.AddSeconds(20);
                        _lastOwnerHitpoints = Owner.CurrentHitPoints;
                        AddHpLinkVisuals();
                        Owner.SendPacket("0|A|STM|msg_pet_hp_link_activated");
                    }
                    break;
            }
            GearId = gearId;

            Owner.SendCommand(PetGearSelectCommand.write(new PetGearTypeModule(gearId), new List<int>()));
        }

        private void ResetState()
        {
            RemoveHpLinkVisuals();
            RemoveShieldSacrificeVisuals();
            GuardModeActive = false;
            AutoLootActive = false;
            ResourceCollectorActive = false;
            EnemyLocatorActive = false;
            ResourceLocatorActive = false;
            TradePodActive = false;
            RepairActive = false;
            KamikazeActive = false;
            ComboShipRepairActive = false;
            ComboGuardActive = false;
            ShieldSacrificeActive = false;
            ResourceSystemLocatorActive = false;
            HpLinkActive = false;
            _shieldSacrificeTriggered = false;
            ResetKamikazeState();
            ResetShieldSacrificeState();
            _activeCollectableTarget = null;
        }

        private bool IsKamikazeOnCooldown()
        {
            return _kamikazeCooldownEndTime > DateTime.Now;
        }

        private void HandleKamikazeCooldownReady()
        {
            if (_kamikazeCooldownEndTime == DateTime.MinValue || _kamikazeCooldownEndTime > DateTime.Now) return;

            _kamikazeCooldownEndTime = DateTime.MinValue;
            UpdateKamikazeGearAvailability(true);

            if (!_kamikazeReadyNotified)
            {
                Owner.SendPacket("0|A|STD|A kamikaze modul ismét bevethető.");
                _kamikazeReadyNotified = true;
            }
        }

        private void UpdateKamikazeGearAvailability(bool enabled)
        {
            if (_kamikazeGearEnabled == enabled) return;

            _kamikazeGearEnabled = enabled;

            Owner.SendCommand(PetGearAddCommand.write(
                new PetGearTypeModule(PetGearTypeModule.KAMIKAZE),
                3,
                1,
                enabled));
        }

        private bool IsAbilityNavigating()
        {
            return (_activeCollectableTarget != null && (AutoLootActive || ResourceCollectorActive))
                   || _kamikazeTarget != null
                   || _shieldSacrificeTarget != null;
        }

        private void StopMovement()
        {
            Moving = false;
            Destination = Position;
            OldPosition = Position;
            Direction = Position;
            MovementStartTime = DateTime.Now;
            MovementTime = 0;
        }

        private void ResetKamikazeState()
        {
            _kamikazeTarget = null;
            _kamikazeDetonationTime = DateTime.MinValue;
            _kamikazeArming = false;
        }

        public void NotifyOwnerAttacked(Character attacker)
        {
            if (!KamikazeActive || attacker == null || attacker.Destroyed) return;

            if (_kamikazeTarget == null)
            {
                _kamikazeTarget = attacker;
                _kamikazeArming = false;
                _kamikazeDetonationTime = DateTime.MinValue;
            }
        }

        private void ResetShieldSacrificeState()
        {
            _shieldSacrificeTarget = null;
            _shieldSacrificeDetonationTime = DateTime.MinValue;
            _shieldSacrificeArming = false;
        }

        private void InitializeAbilities()
        {
            RegisterAbility(PetGearTypeModule.PASSIVE, "Passzív", "A P.E.T. nem hajt végre aktív műveletet.");
            RegisterAbility(PetGearTypeModule.GUARD, "Őr mód", "A P.E.T. megvédi a gazdáját és az őt támadó ellenségeket célozza.");
            RegisterAbility(PetGearTypeModule.AUTO_LOOT, "G-AL3 — Auto Loot Module III", "Automatikusan felismeri és begyűjti a közelben található bónusz- és rakománydobozokat (700 egység).");
            RegisterAbility(PetGearTypeModule.AUTO_RESOURCE_COLLECTION, "G-AR3 — Resource Collector Module III", "Automatikus nyersanyaggyűjtés 3000 egységen belül.");
            RegisterAbility(PetGearTypeModule.ENEMY_LOCATOR, "G-EL3 — Enemy Locator Module III", "Felderíti a rendszerben tartózkodó NPC-ket és kijelzi számukat.");
            RegisterAbility(PetGearTypeModule.RESOURCE_LOCATOR, "G-RL3 — Resource Locator Module III", "Megmutatja a környéken található nyersanyagokat.");
            RegisterAbility(PetGearTypeModule.TRADE_POD, "G-TRA3 — Trade Module III", "A rakomány azonnali eladása +30% bónusszal.");
            RegisterAbility(PetGearTypeModule.REPAIR_PET, "G-REP3 — PET Repair Module III", "15 másodpercig másodpercenként 12 000 HP-val javítja a P.E.T.-et.");
            RegisterAbility(PetGearTypeModule.KAMIKAZE, "G-KK3 — Kamikaze Module III", "Vészhelyzetben 75 000 sebzést okozó robbanást indít 450 egységes sugarú körben.");
            RegisterAbility(PetGearTypeModule.COMBO_SHIP_REPAIR, "C-SR3 — Ship Repair Module III", "Aktiválás után 5 másodpercig másodpercenként 25 000 életerőt állít helyre a hajón.");
            RegisterAbility(PetGearTypeModule.COMBO_GUARD, "C-MG3 — Modular Guard System III", "Azonnali pajzserősítést biztosító védelmi mód.");
            RegisterAbility(PetGearTypeModule.SHIELD_SACRIFICE, "G-SF1 — Shield Sacrifice Module I", "Pajzsenergiát továbbít szövetségesnek, majd a P.E.T. leáll.");
            RegisterAbility(PetGearTypeModule.RESOURCE_SYSTEM_LOCATOR, "G-RL3 — Resource Locator Module III", "Rendszerszintű nyersanyag bemérés 5000 egységig.");
            RegisterAbility(PetGearTypeModule.HP_LINK, "G-HPL — HP Link P.E.T. Gear", "20 másodpercig az űrhajót érő életerő-sebzést a P.E.T.-re terheli át. Újratöltés: 240 másodperc.");
        }

        public override byte[] GetShipCreateCommand() { return null; }
    }
}
