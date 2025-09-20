using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using Assets.Scripts.Inventory__Items__Pickups.Items;
using Assets.Scripts.Inventory__Items__Pickups;
using System;
using Assets.Scripts.Inventory__Items__Pickups.Items.ItemImplementations;
using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Menu.Shop;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace MBAPI_Items
{
    [BepInPlugin(GUID, MODNAME, VERSION)]
    public class Plugin : BasePlugin
    {
        public const string
            MODNAME = "MBAPI_Items",
            AUTHOR = "Oksamies",
            GUID = AUTHOR + "_" + MODNAME,
            VERSION = "0.0.1";

        public Plugin()
        {
            log = Log;
        }

        public override void Load()
        {
            log.LogInfo($"Loaded {MODNAME} v{VERSION} by {AUTHOR}");
            Harmony harmony = new Harmony(GUID);
            harmony.PatchAll();
            log.LogInfo($"{GUID} is patched!");
        }

        public static ManualLogSource log;

        public enum CustomEItem
        {
            Borgar,
        }

        // Choose a unique EItem value for our custom TACO. This ID should not collide with game-defined values.
        private const int TacoItemId = 424242;
        private static readonly EItem TacoEItem = (EItem)TacoItemId;

        // Note: Avoid FieldRefAccess on IL2CPP (can fail at type init). We'll locate fields at runtime via reflection.

        // Try setting a property if fields are not present (some Il2Cpp wrappers expose properties instead of fields)
        private static void TrySetProperty<T>(object target, string propertyName, T value)
        {
            try
            {
                if (target == null) { log.LogWarning("TrySetProperty target is null"); return; }
                var type = target.GetType();
                var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (prop == null || !prop.CanWrite) return;
                prop.SetValue(target, value);
            }
            catch (Exception ex)
            {
                log.LogError($"Failed to set property '{propertyName}' on '{target?.GetType().FullName}': {ex}");
            }
        }

        private static bool s_loggedTypeLayouts = false;
        private static void LogTypeLayout(Type t, string tag)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var fields = t.GetFields(flags).Select(f => $"{(f.IsStatic ? "static " : "")}Field {f.FieldType?.FullName} {f.Name}");
                var props = t.GetProperties(flags).Select(p =>
                {
                    var canSet = (p.SetMethod != null);
                    return $"Property {p.PropertyType?.FullName} {p.Name} {(canSet ? "{set;}" : "{get;}")}";
                });
                log.LogInfo($"Type layout [{tag}] {t.FullName} -> Fields:\n  - " + string.Join("\n  - ", fields));
                log.LogInfo($"Type layout [{tag}] {t.FullName} -> Properties:\n  - " + string.Join("\n  - ", props));
            }
            catch (Exception ex)
            {
                log.LogError($"Failed to log type layout for {t?.FullName}: {ex}");
            }
        }

        private static object GetPropertyValue(object target, string propertyName)
        {
            if (target == null) return null;
            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null) return null;
            var getter = prop.GetGetMethod(true);
            if (getter == null) return null;
            try { return prop.GetValue(target); } catch { return null; }
        }

        private static bool DictionaryContainsKey(object dict, object key)
        {
            if (dict == null || key == null) return false;
            var m = dict.GetType().GetMethod("ContainsKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return false;
            try { return (bool)m.Invoke(dict, new object[] { key }); } catch { return false; }
        }

        private static void DictionarySet(object dict, object key, object value)
        {
            if (dict == null) return;
            var setItem = dict.GetType().GetMethod("set_Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (setItem != null && setItem.GetParameters().Length == 2)
            {
                setItem.Invoke(dict, new object[] { key, value });
                return;
            }
            var add = dict.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 2);
            if (add != null)
            {
                add.Invoke(dict, new object[] { key, value });
            }
        }

        private static int ListCount(object list)
        {
            if (list == null) return 0;
            var prop = list.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null) return 0;
            try { return (int)prop.GetValue(list); } catch { return 0; }
        }

        private static object ListGet(object list, int index)
        {
            if (list == null) return null;
            var m = list.GetType().GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return null;
            try { return m.Invoke(list, new object[] { index }); } catch { return null; }
        }

        private static void ListAdd(object list, object value)
        {
            if (list == null) return;
            var m = list.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { value?.GetType() ?? typeof(object) }, null);
            if (m == null)
            {
                // Fallback: find any Add with single parameter
                m = list.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.Name == "Add" && x.GetParameters().Length == 1);
            }
            if (m != null)
            {
                try { m.Invoke(list, new object[] { value }); } catch (Exception ex) { log.LogError($"List.Add failed: {ex}"); }
            }
        }

        private static Texture2D s_yellowIcon;
        private static Texture2D GetYellowIcon(int size = 16)
        {
            if (s_yellowIcon != null) return s_yellowIcon;
            try
            {
                var tex = new Texture2D(size, size);
                var c = new Color(1f, 1f, 0f, 1f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        tex.SetPixel(x, y, c);
                    }
                }
                tex.Apply();
                s_yellowIcon = tex;
                return s_yellowIcon;
            }
            catch (Exception ex)
            {
                log.LogError($"Failed to create yellow icon texture: {ex}");
                return Texture2D.whiteTexture;
            }
        }

        // Find an existing List<ItemData>-like field/property value on target; return the list object or null
        private static object FindExistingItemDataList(object target)
        {
            if (target == null) return null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = target.GetType();
            foreach (var f in t.GetFields(flags))
            {
                try
                {
                    var ft = f.FieldType;
                    if (ft == null) continue;
                    bool isList = ft.Name.Contains("List") || ft.FullName?.Contains("List`)1") == true;
                    if (!isList) continue;
                    var ga = ft.IsGenericType ? ft.GetGenericArguments() : Type.EmptyTypes;
                    if (!ga.Any(a => a == typeof(ItemData) || a.Name == nameof(ItemData))) continue;
                    var value = f.GetValue(target);
                    if (value != null) return value;
                }
                catch { }
            }
            foreach (var p in t.GetProperties(flags))
            {

                try
                {
                    var pt = p.PropertyType;
                    if (pt == null) continue;
                    bool isList = pt.Name.Contains("List") || pt.FullName?.Contains("List`)1") == true;
                    if (!isList) continue;
                    var ga = pt.IsGenericType ? pt.GetGenericArguments() : Type.EmptyTypes;
                    if (!ga.Any(a => a == typeof(ItemData) || a.Name == nameof(ItemData))) continue;
                    if (p.GetGetMethod(true) == null) continue;
                    var value = p.GetValue(target);
                    if (value != null) return value;
                }
                catch { }
            }
            return null;
        }

        [HarmonyPatch(typeof(ItemFactory), nameof(ItemFactory.CreateItem))]
        public class ItemFactoryCreateItemPatch
        {
            [HarmonyPrefix]
            internal static bool Prefix(EItem eItem, ItemInventory inventory, ref ItemBase __result)
            {
                try
                {
                    if ((int)eItem == TacoItemId)
                    {
                        // Short-circuit the factory for our custom item to avoid Unknown item type exceptions.
                        __result = new ItemBorgor(inventory);
                        log.LogInfo("[Factory.Prefix] Handled custom TACO by returning ItemBorgor placeholder");
                        return false; // Skip original
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"ItemFactory.CreateItem Prefix failed: {ex}");
                }
                return true; // Continue to original for all other items
            }

            [HarmonyPostfix]
            internal static void Postfix(EItem eItem, ItemInventory inventory, ref ItemBase __result)
            {
                log.LogInfo($"ItemFactory.CreateItem called for EItem {eItem} ({(int)eItem})");
                try
                {
                    if ((int)eItem == TacoItemId && __result == null)
                    {
                        __result = new ItemBorgor(inventory);
                        log.LogInfo("Created TACO proxy item via ItemBorgor to satisfy ItemFactory");
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"Failed to create proxy item for TACO: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(DataManager))]
        [HarmonyPatch(nameof(DataManager.Load))]
        public static class DataManagerLoadFieldsPatch
        {
            [HarmonyPrefix]
            internal static bool Prefix(DataManager __instance)
            {
                try
                {
                    if (__instance == null) return true;

                    if (!s_loggedTypeLayouts)
                    {
                        LogTypeLayout(typeof(ItemData), "ItemData");
                        LogTypeLayout(__instance.GetType(), "DataManager");
                        s_loggedTypeLayouts = true;
                    }

                    // Ensure TACO is present in unsortedItems BEFORE Load builds dictionaries
                    // Use Harmony FieldRef to read IL2CPP fields reliably
                    object listObj = FindExistingItemDataList(__instance);
                    int beforeCount = ListCount(listObj);
                    log.LogInfo($"[Prefix] unsortedItems before: {beforeCount}");
                    // Do not attempt to construct Il2Cpp lists here; rely on Awake injection or existing list

                    if (listObj != null)
                    {
                        // Check if already exists
                        int count = ListCount(listObj);
                        bool exists = false;
                        for (int i = 0; i < count; i++)
                        {
                            var it = ListGet(listObj, i) as ItemData;
                            if (it == null) continue;
                            var e = GetPropertyValue(it, "eItem");
                            if (e != null && e.Equals(TacoEItem)) { exists = true; break; }
                        }

                        if (!exists)
                        {
                            var tacoData = ScriptableObject.CreateInstance<ItemData>();
                            TrySetProperty(tacoData, "inItemPool", true);
                            TrySetProperty(tacoData, "eItem", TacoEItem);
                            TrySetProperty(tacoData, "description", "TACO increases health regeneration by 20%.");
                            TrySetProperty(tacoData, "shortDescription", "TACO");
                            TrySetProperty(tacoData, "icon", GetYellowIcon());
                            TrySetProperty(tacoData, "rarity", (EItemRarity)ERarity.Common);
                            TrySetProperty(tacoData, "maxAmount", 99);

                            ListAdd(listObj, tacoData);
                            int afterCount = ListCount(listObj);
                            log.LogInfo($"Prefixed: Added TACO to unsortedItems (count {beforeCount} -> {afterCount})");
                        }
                        else
                        {
                            log.LogInfo("Prefixed: TACO already present in unsortedItems");
                        }
                    }
                    else log.LogWarning("[Prefix] unsortedItems is null or inaccessible; skipping TACO injection");

                    // Intentionally do NOT touch the itemData dictionary here.
                    // DataManager.Load() will rebuild it from unsortedItems; pre-populating can cause duplicate-key exceptions.
                }
                catch (Exception ex)
                {
                    log.LogError($"DataManager.Load Prefix failed: {ex}");
                }

                // Continue with original Load
                return true;
            }
        }

        // Note: Do not patch DataManager.Awake (not present on IL2CPP target). We rely on Load Prefix instead.

        // Removed post-load injection to avoid duplicates; items are injected in Prefix now.
        // Ensure the dictionary contains TACO after Load (in case Load overwrote the list before building dictionaries)
        [HarmonyPatch(typeof(DataManager))]
        [HarmonyPatch(nameof(DataManager.Load))]
        public static class DataManagerLoadPostfixEnsureDictPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(DataManager __instance)
            {
                try
                {
                    if (__instance == null) return;
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                    // Get private field itemData
                    var dictField = __instance.GetType().GetField("itemData", flags);
                    object dictObj = dictField?.GetValue(__instance);
                    int dictCountBefore = 0;
                    if (dictObj != null)
                    {
                        var countProp = dictObj.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (countProp != null)
                        {
                            try { dictCountBefore = (int)countProp.GetValue(dictObj); } catch { dictCountBefore = -1; }
                        }
                        log.LogInfo($"[Postfix] itemData dict count before ensure: {dictCountBefore}");
                    }
                    if (dictObj == null && dictField != null)
                    {
                        var dictType = dictField.FieldType;
                        if (dictType != null)
                        {
                            dictObj = Activator.CreateInstance(dictType);
                            dictField.SetValue(__instance, dictObj);
                            log.LogInfo("[Postfix] Created new itemData dictionary instance");
                        }
                    }

                    if (dictObj == null)
                        return;

                    if (!DictionaryContainsKey(dictObj, TacoEItem))
                    {
                        var tacoData = ScriptableObject.CreateInstance<ItemData>();
                        TrySetProperty(tacoData, "inItemPool", true);
                        TrySetProperty(tacoData, "eItem", TacoEItem);
                        TrySetProperty(tacoData, "description", "TACO increases health regeneration by 20%.");
                        TrySetProperty(tacoData, "shortDescription", "TACO");
                        TrySetProperty(tacoData, "icon", GetYellowIcon());
                        TrySetProperty(tacoData, "rarity", (EItemRarity)ERarity.Common);
                        TrySetProperty(tacoData, "maxAmount", 99);

                        DictionarySet(dictObj, TacoEItem, tacoData);
                        var countProp2 = dictObj.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var dictCountAfter = dictCountBefore;
                        if (countProp2 != null)
                        {
                            try { dictCountAfter = (int)countProp2.GetValue(dictObj); } catch { }
                        }
                        log.LogInfo($"Postfix: Ensured TACO present in itemData (count {dictCountBefore} -> {dictCountAfter})");

                        // Also ensure it's in unsortedItems (for UI), if the list still exists
                        var listField = __instance.GetType().GetField("unsortedItems", flags);
                        var listObj = listField?.GetValue(__instance);
                        if (listObj != null)
                        {
                            int count = ListCount(listObj);
                            bool exists = false;
                            for (int i = 0; i < count; i++)
                            {
                                var it = ListGet(listObj, i) as ItemData;
                                if (it == null) continue;
                                var e = GetPropertyValue(it, "eItem");
                                if (e != null && e.Equals(TacoEItem)) { exists = true; break; }
                            }
                            if (!exists)
                            {
                                ListAdd(listObj, tacoData);
                                log.LogInfo("Postfix: Added TACO to DataManager.unsortedItems as well");
                            }
                            else
                            {
                                log.LogInfo("Postfix: TACO already in unsortedItems");
                            }
                        }
                        else
                        {
                            log.LogWarning("[Postfix] unsortedItems list was null; could not mirror TACO");
                        }
                    }
                    else
                    {
                        log.LogInfo("Postfix: TACO already present in itemData");
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"DataManager.Load Postfix (ensure dict) failed: {ex}");
                }
            }
        }

        // Apply TACO effect: +20% health regeneration per TACO owned.
        [HarmonyPatch(typeof(PlayerStatsNew))]
        [HarmonyPatch(nameof(PlayerStatsNew.GetStat))]
        public static class PlayerStatsGetStatPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(object __instance, EStat stat, ref float __result)
            {
                try
                {
                                        // Only adjust health regeneration stat. Be tolerant of enum names: HealthRegen or HealthRegeneration.
                                        var statName = stat.ToString();
                                        if (!(string.Equals(statName, "HealthRegeneration", StringComparison.OrdinalIgnoreCase) ||
                                                    string.Equals(statName, "HealthRegen", StringComparison.OrdinalIgnoreCase)))
                                                return;

                    // Fetch PlayerInventory from PlayerStatsNew via reflection (field or property)
                    var type = __instance.GetType();
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    object playerInventory = null;
                    foreach (var f in type.GetFields(flags))
                    {
                        if (f.FieldType.Name == nameof(PlayerInventory)) { playerInventory = f.GetValue(__instance); break; }
                    }
                    if (playerInventory == null)
                    {
                        foreach (var p in type.GetProperties(flags))
                        {
                            if (p.PropertyType.Name == nameof(PlayerInventory)) { playerInventory = p.GetValue(__instance); break; }
                        }
                    }
                    if (playerInventory == null) return;

                    // Get itemInventory from PlayerInventory
                    object itemInventory = null;
                    var pit = playerInventory.GetType();
                    var fi = pit.GetField("itemInventory", flags);
                    if (fi != null) itemInventory = fi.GetValue(playerInventory);
                    if (itemInventory == null)
                    {
                        var pi = pit.GetProperty("itemInventory", flags);
                        if (pi != null) itemInventory = pi.GetValue(playerInventory);
                    }
                    if (itemInventory == null) return;

                    // Call GetAmount(EItem) on itemInventory
                    var getAmount = itemInventory.GetType().GetMethods(flags)
                        .FirstOrDefault(m => m.Name == "GetAmount" && m.GetParameters().Length == 1 &&
                                             (m.GetParameters()[0].ParameterType == typeof(EItem) || m.GetParameters()[0].ParameterType.Name == nameof(EItem)));
                    if (getAmount == null) return;
                    var countObj = getAmount.Invoke(itemInventory, new object[] { TacoEItem });
                    int count = 0;
                    if (countObj is int ci) count = ci;
                    if (count <= 0) return;

                    // Apply a flat +20% once if the player has at least one TACO
                    __result *= 1.20f;
                }
                catch (Exception ex)
                {
                    log.LogError($"Failed to apply TACO regen bonus: {ex}");
                }
            }
        }

        // DEBUG: Give the player 1x TACO on first PlayerInventory.Update to force ItemFactory.CreateItem(TACO)
        // This helps verify the full pipeline end-to-end. Safe to remove after validation.
        [HarmonyPatch(typeof(PlayerInventory))]
        [HarmonyPatch(nameof(PlayerInventory.Update))]
        public static class PlayerInventoryGiveTacoOncePatch
        {
            private static bool s_given;

            [HarmonyPostfix]
            internal static void Postfix(PlayerInventory __instance)
            {
                if (s_given || __instance == null) return;
                try
                {
                    // Get itemInventory field/property
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    object itemInventory = null;
                    var pit = __instance.GetType();
                    var fi = pit.GetField("itemInventory", flags);
                    if (fi != null) itemInventory = fi.GetValue(__instance);
                    if (itemInventory == null)
                    {
                        var pi = pit.GetProperty("itemInventory", flags);
                        if (pi != null) itemInventory = pi.GetValue(__instance);
                    }
                    if (itemInventory == null) return;

                    // Try AddItem(EItem,int) first, then AddItem(EItem)
                    var methods = itemInventory.GetType().GetMethods(flags).Where(m => m.Name == "AddItem").ToArray();
                    var add2 = methods.FirstOrDefault(m =>
                    {
                        var ps = m.GetParameters();
                        return ps.Length == 2 &&
                               (ps[0].ParameterType == typeof(EItem) || ps[0].ParameterType.Name == nameof(EItem)) &&
                               (ps[1].ParameterType == typeof(int) || ps[1].ParameterType.Name == nameof(Int32));
                    });
                    var add1 = methods.FirstOrDefault(m =>
                    {
                        var ps = m.GetParameters();
                        return ps.Length == 1 &&
                               (ps[0].ParameterType == typeof(EItem) || ps[0].ParameterType.Name == nameof(EItem));
                    });

                    if (add2 != null)
                    {
                        add2.Invoke(itemInventory, new object[] { TacoEItem, 1 });
                        log.LogInfo("[Debug] Granted 1x TACO via ItemInventory.AddItem(EItem,int)");
                        s_given = true;
                    }
                    else if (add1 != null)
                    {
                        add1.Invoke(itemInventory, new object[] { TacoEItem });
                        log.LogInfo("[Debug] Granted 1x TACO via ItemInventory.AddItem(EItem)");
                        s_given = true;
                    }
                    else
                    {
                        log.LogWarning("[Debug] Could not find ItemInventory.AddItem overload to grant TACO");
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"[Debug] Failed to grant TACO once: {ex}");
                }
            }
        }
    }
}