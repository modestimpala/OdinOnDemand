using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Config;

namespace OdinOnDemand.Patches
{
    [HarmonyPatch(typeof(Trader), "GetAvailableItems")]
    public class HaldorCustomPatch
    {
        private static void Postfix(ref List<Trader.TradeItem> __result)
        {
            List<Trader.TradeItem> newItems = new List<Trader.TradeItem>();
            
            if (OODConfig.SkaldsGirdleEnabled.Value)
            {
                var skald = PrefabManager.Instance.GetPrefab("skaldsgirdle");
                if (skald != null)
                {
                    newItems.Add(new Trader.TradeItem
                    {
                        m_prefab = skald.GetComponent<ItemDrop>(),
                        m_stack = 1,
                        m_price = OODConfig.SkaldsGirdleCost.Value
                    });
                }
            }

            __result = __result.Concat(newItems).ToList();
        }
    }
}
