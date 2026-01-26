using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ow.Utils;

namespace Ow.Net.netty.commands
{
    class JumpCPUPriceMappingModule
    {
        public const short ID = 3574;

        public List<int> mapIdList;     
        public int price = 0;
        public short currencyType = PriceModule.URIDIUM;

        public JumpCPUPriceMappingModule(int price, List<int> mapIdList, short currencyType = PriceModule.URIDIUM)
        {
            this.price = price;
            this.mapIdList = mapIdList;
            this.currencyType = currencyType;
        }

        public byte[] write()
        {
            var param1 = new ByteArray(ID);
            param1.writeInt(this.mapIdList.Count);
            foreach(var id in this.mapIdList)
            {
                param1.writeInt(id << 11 | id >> 21);
            }
            param1.write(new PriceModule(currencyType, price).write());
            return param1.Message.ToArray();
        }
    }
}
