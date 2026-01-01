using Ow.Game.Movements;
using Ow.Game.Objects.Collectables;
using Ow.Game.Objects.Players.Managers;
using Ow.Game.Ticks;
using Ow.Managers;
using Ow.Net.netty;
using Ow.Net.netty.commands;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ow.Game.Objects
{
    abstract class Collectable : Object, Tick
    {
        public int CollectableId { get; set; }
        public string Hash { get; set; }
        public bool Respawnable { get; set; }
        public Character Character { get; set; }
        public Player ToPlayer { get; set; }
        public bool Disposed = false;

        public int Seconds => CollectableId == AssetTypeModule.BOXTYPE_PIRATE_BOOTY ? 5 : -1;

        public Collectable(int collectableId, Position position, Spacemap spacemap, bool respawnable, Player toPlayer) : base(Randoms.CreateRandomID(), position, spacemap)
        {
            Hash = Randoms.GenerateHash(10);
            CollectableId = collectableId;
            Respawnable = respawnable;
            ToPlayer = toPlayer;

            if (this is CargoBox)
            {
                Program.TickManager.AddTick(this);
                disposeTime = DateTime.Now;
            }
        }

        public DateTime collectTime = new DateTime();
        public DateTime disposeTime = new DateTime();
        public void Tick()
        {
            if (!Disposed)
            {
                if (this is CargoBox && disposeTime.AddMinutes(2) < DateTime.Now)
                    Dispose();

                if (Character != null && Character.Collecting)
                {
                    if (!Character.Moving)
                    {
                        var bootyKeyType = GetBootyKeyType();
                        bool needsKey = bootyKeyType.HasValue;

                        if (!needsKey || (needsKey && Character is Player player && player.Equipment?.Items?.BootyKeys != null && player.Equipment.Items.BootyKeys.GetKeyCount(bootyKeyType.Value) >= 1))
                        {
                            if (collectTime.AddSeconds(Seconds) < DateTime.Now)
                            {
                                Reward(Character is Pet pet ? pet.Owner : Character as Player);
                                Dispose();
                            }
                        }
                        else CancelCollection();
                    }
                    else CancelCollection();
                }
            }

            /*
            if (!Disposed)
                foreach (var character in Spacemap.Characters.Values)
                {
                    if (character.Position.X == Position.X && character.Position.Y == Position.Y && character is Pet)
                        Collect(character as Pet);
                }
                */
        }

        private BootyKeyType? GetBootyKeyType()
        {
            if (this is GreenBooty)
                return BootyKeyType.Green;
            if (this is RedBooty)
                return BootyKeyType.Red;
            if (this is BlueBooty)
                return BootyKeyType.Blue;
            if (this is SilverBooty)
                return BootyKeyType.Silver;
            if (this is GoldBooty)
                return BootyKeyType.Gold;
            return null;
        }

        private string GetAutoBuyNotActiveMessage(BootyKeyType keyType)
        {
            switch (keyType)
            {
                case BootyKeyType.Blue:
                    return "0|A|STM|msg_booty-key-blue_auto_buy_not_active";
                case BootyKeyType.Silver:
                    return "0|A|STM|msg_booty-key-silver_auto_buy_not_active";
                case BootyKeyType.Red:
                    return "0|A|STM|msg_booty-key-red_auto_buy_not_active";
                case BootyKeyType.Green:
                    return "0|A|STM|msg_booty-key-green_auto_buy_not_active";
                case BootyKeyType.Gold:
                    return "0|A|STM|msg_booty-key-gold_auto_buy_not_active";
                default:
                    return string.Empty;
            }
        }

        public void CancelCollection()
        {
            Character.Collecting = false;

            var packet = $"0|{ServerCommands.SET_ATTRIBUTE}|{ServerCommands.ASSEMBLE_COLLECTION_BEAM_CANCELLED}|{(Character is Pet ? 1 : 0)}|{Character.Id}";

            if (Character is Player player)
            {
                //player.SendPacket($"0|LM|ST|SLC");
                player.SendPacket(packet);
            }
            else if (Character is Pet pet)
                pet.SendPacketToInRangePlayers(packet);


		    if (Character is Player p)
            {
                var bootyKeyType = GetBootyKeyType();
                if (bootyKeyType.HasValue && (p.Equipment?.Items?.BootyKeys?.GetKeyCount(bootyKeyType.Value) ?? 0) <= 0)
                {
                    var message = GetAutoBuyNotActiveMessage(bootyKeyType.Value);
                    if (!string.IsNullOrEmpty(message))
                        p.SendPacket(message);
                }
            }
/*
sdfsdf
*/
            
            Character = null;

            Program.TickManager.RemoveTick(this);
        }

        public void Collect(Character character)
        {
            if (Disposed) return;

            Character = character;
            Character.Collecting = true;
            Character.Moving = false;
            collectTime = DateTime.Now;

            var packet = $"0|{ServerCommands.SET_ATTRIBUTE}|{ServerCommands.ASSEMBLE_COLLECTION_BEAM_ACTIVE}|{(Character is Pet ? 1 : 0)}|{Character.Id}|{Seconds}";

            if (Character is Player player)
            {
                //player.SendPacket($"0|LM|ST|SLA|{Seconds}");
                player.SendPacket(packet);
            }
            else if (Character is Pet pet)
                pet.SendPacketToInRangePlayers(packet);

            Program.TickManager.AddTick(this);
        }

        public void Dispose()
        {
            Disposed = true;
            Character = null;
            Spacemap.Objects.TryRemove(Id, out var collectable);
            Program.TickManager.RemoveTick(this);
            GameManager.SendCommandToMap(Spacemap.Id, DisposeBoxCommand.write(Hash, true));

            if (Respawnable)
                Respawn();
        }

        public void Respawn()
        {
            Position = Position.Random(Spacemap, 0, 20800, 0, 12800);
            Spacemap.Objects.TryAdd(Id, this);

            if (this is CargoBox)
                Program.TickManager.AddTick(this);

            foreach (var gameSession in GameManager.GameSessions.Values.Where(x => x.Player.Storage.InRangeObjects.ContainsKey(Id)))
                gameSession?.Player.Storage.InRangeObjects.TryRemove(Id, out var obj);

            Disposed = false;
        }

        public abstract void Reward(Player player);

        public abstract byte[] GetCollectableCreateCommand();
    }
}
