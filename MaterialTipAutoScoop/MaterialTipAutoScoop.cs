using FrooxEngine;
using HarmonyLib;
using NeosModLoader;
using System;
using BaseX;
using System.Collections.Generic;

namespace MaterialTipAutoScoop
{
    public class MaterialTipAutoScoop : NeosMod
    {
        public override string Name => "MaterialTipAutoScoop";
        public override string Author => "badhaloninja";
        public override string Version => "1.1.0";
        public override string Link => "https://github.com/badhaloninja/MaterialTipAutoScoop";

        private static ModConfiguration config;



        
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<int> scoopMode = new ModConfigurationKey<int>("scoopMode", "<size=200%>Orb handling\n0: Always Destroy | 1: Drop if material is stored on orb | 2: Always Drop</size>", () => 1, valueValidator: (i) => i.IsBetween(0, 2)); //Desc scaled for NeosModSettings 
        /* 
         * Orb handling
         * 0: Always Destroy
         * 1: Drop if material is stored on orb
         * 2: Always Drop
         */

        public override void OnEngineInit()
        {
            config = GetConfiguration();

            Harmony harmony = new Harmony("me.badhaloninja.MaterialTipAutoScoop");
            harmony.PatchAll();
        }



        [HarmonyPatch(typeof(MaterialTip), "GenerateMenuItems")]
        class MenuItems
        {
            public static void Postfix(MaterialTip __instance, CommonTool tool, ContextMenu menu)
            {
                Uri icon = new Uri("neosdb:///5f2cc0be2830560a76b6e2873e8e1dd86e4036a476656e693c90da19b4cf36cf.png");
                ContextMenuItem contextMenuItem = menu.AddItem("What", icon, default(color));

                var field = contextMenuItem.Slot.AttachComponent<ValueField<int>>();
                field.Value.Value = config.GetValue(scoopMode);
                field.Value.OnValueChange += (vf) =>
                {  // Yeah
                    config.Set(scoopMode, vf.Value);
                };

                //contextMenuItem.AttachOptionDescriptionDriver<int>();
                contextMenuItem.SetupValueCycle(field.Value, new List<OptionDescription<int>> {
                        new OptionDescription<int>(0, "Always Destroy Orb", new color?(color.Red), icon),
                        new OptionDescription<int>(1, "Destroy Reference Orbs", new color?(color.Yellow), icon),
                        new OptionDescription<int>(2, "Always Drop Orb", new color?(color.Green), icon)
                    });
            }
        }


        [HarmonyPatch(typeof(MaterialTip), "OnSecondaryPress")]
        class MaterialTip_OnSecondaryPress_Patch
        {
            public static void Prefix(MaterialTip __instance)
            {
                Slot heldSlot = __instance.GetHeldReference<Slot>();
                if (heldSlot != null || __instance.OrbSlot.ChildrenCount == 0) return;

                var mat = GetTargetMaterial(__instance);
                if (mat == null || __instance.GetMaterial() == mat) return;


                __instance.OrbSlot.DestroyChildren(filter: (orb) =>
                 {  // Destroy if setting == 0 |OR| if setting == 1 &AND& does not have material comp
                     int scoopSetting = config.GetValue(scoopMode);
                     if (scoopSetting != 1) return scoopSetting == 0;
                     return null == orb.GetComponent<IAssetProvider<Material>>();
                 });

                __instance.OrbSlot.ReparentChildren(__instance.LocalUserSpace);
            }

            private static IAssetProvider<Material> GetTargetMaterial(MaterialTip instance)
            {  // Might just override the method at this point
                IAssetProvider<Material> mat = instance.GetHeldReference<IAssetProvider<Material>>();
                if (mat != null) return mat;

                if (instance.UsesLaser)
                {
                    bool hitSomething;
                    mat = RaycastMaterial(instance, out hitSomething);

                    if (mat == null && !hitSomething)
                    {
                        mat = instance.World.RootSlot.GetComponentInChildren<Skybox>().Material.Target;
                    }
                }
                return mat;
            }


            [HarmonyReversePatch]
            [HarmonyPatch(typeof(MaterialTip), "RaycastMaterial")]
            private static IAssetProvider<Material> RaycastMaterial(MaterialTip instance, out bool hitSomething)
            {
                throw new NotImplementedException("It's a stub");
            }
        }
    }
}