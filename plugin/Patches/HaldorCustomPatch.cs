using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Managers;
using OdinOnDemand.Utils.Config;

namespace OdinOnDemand.Patches
{
    [HarmonyPatch(typeof(Trader), "GetAvailableItems")]
    public class HaldorCustomPatch
    {
        private const string SkaldsGirdle = "skaldsgirdle";

        // Only Haldor sells the girdle. Hildir, the Bog Witch and modded traders keep their own stock.
        private static void Postfix(Trader __instance, List<Trader.TradeItem> __result)
        {
            if (__result == null || !OODConfig.SkaldsGirdleEnabled.Value) return;
            if (global::Utils.GetPrefabName(__instance.gameObject) != "Haldor") return;

            var skald = PrefabManager.Instance.GetPrefab(SkaldsGirdle);
            var itemDrop = skald ? skald.GetComponent<ItemDrop>() : null;
            if (!itemDrop) return;
            if (__result.Exists(item => item.m_prefab == itemDrop)) return;

            __result.Add(new Trader.TradeItem
            {
                m_prefab = itemDrop,
                m_stack = 1,
                m_price = OODConfig.SkaldsGirdleCost.Value,
                m_requiredGlobalKey = "",
                m_buyKey = ""
            });
        }
    }
}
