using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ow.Game.Movements;
using Ow.Game.Objects.Players.Managers;
using Ow.Game.Objects.Stations;
using Ow.Managers;
using Ow.Managers.MySQLManager;
using Ow.Net.netty.commands;
using Ow.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ow.Game.Objects.Collectables
{
    class GoldBooty : Collectable
    {
        public GoldBooty(Position position, Spacemap spacemap, bool respawnable, Player toPlayer = null) : base(AssetTypeModule.BOXTYPE_PIRATE_BOOTY, position, spacemap, respawnable, toPlayer) { }

        public override void Reward(Player player)
        {
            var roll = Randoms.random.NextDouble();

            if (roll < 0.25)
            {
                GrantItem(player, "lf4", "Kaptál egy LF-4 típusú lézert");
            }
            else if (roll < 0.5)
            {
                GrantItem(player, "havoc", "Kaptál egy HAVOC típusú dróndizájnt");
            }
            else if (roll < 0.75)
            {
                GrantItem(player, "hercules", "Kaptál egy HERCULES típusú dróndizájnt");
            }
            else
            {
                GrantRandomBooster(player);
            }

            if (player?.Equipment?.Items?.BootyKeys != null && player.Equipment.Items.BootyKeys.TryConsume(BootyKeyType.Gold))
            {
                QueryManager.SavePlayer.BootyKeys(player);
                player.SendPacket($"0|A|BK|{player.Equipment.Items.BootyKeys.TotalKeys}");
            }
        }

        public override byte[] GetCollectableCreateCommand()
        {
            return CreateBoxCommand.write("PIRATE_BOOTY_GOLD", Hash, Position.Y, Position.X);
        }

        private void GrantRandomBooster(Player player)
        {
            var hours = Randoms.random.NextDouble() <= 0.1 ? 10 : 1;
            var boosterTypes = Randoms.random.NextDouble() <= 0.25 ? new int[] { 1, 16, 9, 11, 6, 3 } : new int[] { 0, 15, 8, 10, 5, 2 };
            var boosterType = boosterTypes[Randoms.random.Next(boosterTypes.Length)];

            player.BoosterManager.Add((BoosterType)boosterType, hours);
            player.SendPacket($"0|A|STD|Kaptál egy boostert: {(BoosterType)boosterType} ({hours}h)");
        }

        private void GrantItem(Player player, string itemKey, string message)
        {
            UpdateItemsInventory(player, itemKey);
            player.SendPacket($"0|A|STD|{message}");
        }

        private void UpdateItemsInventory(Player player, string itemKey)
        {
            try
            {
                using (var mySqlClient = SqlDatabaseManager.GetClient())
                {
                    var itemsRow = mySqlClient.ExecuteQueryRow($"SELECT items FROM player_equipment WHERE userId = {player.Id}");
                    var itemsJson = itemsRow?["items"]?.ToString();
                    var items = string.IsNullOrEmpty(itemsJson) ? new JObject() : JsonConvert.DeserializeObject<JObject>(itemsJson) ?? new JObject();

                    IncrementItem(items, itemKey);
                    IncrementItem(items, $"{itemKey}Count");

                    var serializedItems = JsonConvert.SerializeObject(items).Replace("'", "\\'");
                    mySqlClient.ExecuteNonQuery($"UPDATE player_equipment SET items = '{serializedItems}' WHERE userId = {player.Id}");
                }
            }
            catch (Exception e)
            {
                Logger.Log("error_log", $"- [GoldBooty.cs] UpdateItemsInventory exception: {e}");
            }
        }

        private void IncrementItem(JObject items, string key)
        {
            if (items[key] == null || items[key].Type != JTokenType.Integer)
                items[key] = 0;

            items[key] = items[key].Value<int>() + 1;
        }
    }
}
