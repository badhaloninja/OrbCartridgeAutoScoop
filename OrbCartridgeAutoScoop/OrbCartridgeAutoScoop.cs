using System;
using Elements.Core;
using System.Reflection;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;

namespace OrbCartridgeAutoScoop
{
    public class OrbCartridgeAutoScoop : ResoniteMod
    {
        public override string Name => "OrbCartridgeAutoScoop";
        public override string Author => "badhaloninja";
        public override string Version => "2.1.0";
        public override string Link => "https://github.com/badhaloninja/OrbCartridgeAutoScoop";

        private static ModConfiguration config;

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> BehaviorEnabled = new("enabled", "Enabled", () => true);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<int> ScoopModeIndex = new("scoopMode", "<size=200%>Orb handling\n0: Always Destroy | 1: Drop if asset is stored on orb | 2: Always Drop</size>", () => 1, valueValidator: (i) => i.IsBetween(0, 2)); //Desc scaled for NeosModSettings 
        /* 
         * Orb handling
         * 0: Always Destroy
         * 1: Drop if asset is stored on orb
         * 2: Always Drop
         */

        // Scoop on created
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> ScoopMaterialOnCreated = new("scoopMaterialOnCreated", "Pick up materials when created", () => true);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> HideEjectOrb = new("hideEjectOrb", "Hide the Eject Orb context menu button", () => false);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> HideScoopMode = new("hideScoopMode", "Hide the Scoop Mode context menu button", () => false);

        public override void OnEngineInit()
        {
            config = GetConfiguration();

            Harmony harmony = new("ninja.badhalo.OrbCartridgeAutoScoop");
            harmony.PatchAll();

            Engine.Current.RunPostInit(() =>
            { // Patch dev create new form after init because of static constructor
                var target = typeof(DevCreateNewForm).GetMethod("SpawnMaterial", BindingFlags.Public | BindingFlags.Static);
                var postfix = typeof(ExtraMaterialToolBehavior).GetMethod(nameof(ExtraMaterialToolBehavior.ScoopOnCreateNew));
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            });
        }

        const string ScoopOrbIcon = "75c63b3fa95d3e50e62959f317366c183f26aa719e96758c66b8399e06d540d6";

        [HarmonyPatch(typeof(OrbCartridgeTool))]
        class ToolPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch("GenerateMenuItems")]
            public static bool HideEjectOrbButton() => !config.GetValue(BehaviorEnabled) || !config.GetValue(HideEjectOrb);

            [HarmonyPostfix]
            [HarmonyPatch("GenerateMenuItems")]
            public static void ScoopModeContextMenu(ContextMenu menu)
            {
                if (!config.GetValue(BehaviorEnabled) || config.GetValue(HideScoopMode)) return;

                Uri icon = menu.Cloud.Assets.GenerateURL(ScoopOrbIcon);
                ContextMenuItem contextMenuItem = menu.AddItem("Scoop Mode", icon, colorX.White);

                var field = contextMenuItem.Slot.AttachComponent<ValueField<int>>();

                field.Value.Value = config.GetValue(ScoopModeIndex);
                field.Value.OnValueChange += (vf) => config.Set(ScoopModeIndex, vf.Value);

                contextMenuItem.SetupValueCycle(field.Value, [
                        new OptionDescription<int>(0, "Always Destroy Orb", RadiantUI_Constants.Hero.RED, icon),
                        new OptionDescription<int>(1, "Destroy Reference Orbs", RadiantUI_Constants.Hero.YELLOW, icon),
                        new OptionDescription<int>(2, "Always Drop Orb", RadiantUI_Constants.Hero.GREEN, icon)
                    ]);
            }


            [HarmonyPrefix]
            [HarmonyPatch(typeof(MeshTool), "OnSecondaryPress")]
            public static void HandleMesh(MeshTool __instance)
            {
                if (!config.GetValue(BehaviorEnabled)) return;

                var mesh = RaycastTarget(__instance)?.Mesh.Target;
                SharedOrbTools.ClearOrbs(__instance, mesh);
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(MaterialTool), "OnSecondaryPress")]
            public static void HandleMaterial(MaterialTool __instance)
            {
                if (!config.GetValue(BehaviorEnabled)) return;
                if (__instance.GetHeldReference<Slot>() != null) return;

                var mat = GetTargetMaterial(__instance);
                SharedOrbTools.ClearOrbs(__instance, mat);
            }


            private static IAssetProvider<Material> GetTargetMaterial(MaterialTool instance)
            {  // Might just override the method at this point
                IAssetProvider<Material> mat = instance.GetHeldReference<IAssetProvider<Material>>();
                if (mat != null) return mat;

                if (instance.UsesLaser)
                {
                    mat = RaycastMaterial(instance, out bool hitSomething);

                    if (mat == null && !hitSomething)
                    {
                        mat = instance.World.RootSlot.GetComponentInChildren<Skybox>().Material.Target;
                    }
                }
                return mat;
            }

            [HarmonyReversePatch]
            [HarmonyPatch(typeof(MaterialTool), "RaycastMaterial")]
            private static IAssetProvider<Material> RaycastMaterial(MaterialTool instance, out bool hitSomething) => throw new NotImplementedException("It's a stub");
            
            [HarmonyReversePatch]
            [HarmonyPatch(typeof(MeshTool), "RaycastTarget")]
            private static MeshRenderer RaycastTarget(MeshTool instance) => throw new NotImplementedException("It's a stub");
        }

        // Scoop on created
        //[HarmonyPatch(typeof(DevCreateNewForm), "SpawnMaterial")] // Patched manually in OnEngineInit because of static constructor
        class ExtraMaterialToolBehavior
        {
            public static void ScoopOnCreateNew(Slot slot)
            {
                if (!config.GetValue(BehaviorEnabled) || !config.GetValue(ScoopMaterialOnCreated)) return;

                /* Delay to allow for OnCreated actions to run
                 * Since SpawnMaterial is a static method, I can't check if OnCreated is assigned to the CreateNewForm calling this method
                 * So I have to delay every time
                 * 
                 * OnCreated actions like Convert to Material goes by the material in the tool tip when they run
                 * and since this replaces the material in the tool tip *before* the OnCreated action runs it just copies the newly created material to it's self
                 */
                slot.RunInUpdates(0, () => // 0 is enough to run after OnCreated
                {
                    // Get matarial tip from user if it exists and prioritize the primary hand
                    var commonTools = slot.LocalUserRoot.GetRegisteredComponents<InteractionHandler>(c => c.ActiveTool is MaterialTool);

                    // Sort common tool by user's primary hand 
                    commonTools.Sort((a, b) => a.Side.Value == a.InputInterface.PrimaryHand ? -1 : 1);

                    var commonTool = commonTools.GetFirst();
                    if (commonTool?.ActiveTool is not MaterialTool materialTool) return; // Skip if no common tool with material tip found

                    var asset = SharedOrbTools.GetOrbAsset<Material>(slot);
                    SharedOrbTools.ClearOrbs(materialTool, asset);

                    slot.SetParent(materialTool.OrbSlot);
                    slot.SetIdentityTransform();
                });
            }
        }





        public static class SharedOrbTools
        {
            public static void ClearOrbs<A>(OrbCartridgeTool tool, IAssetProvider<A> asset) where A : class, IAsset
            {
                if (tool.OrbSlot.ChildrenCount == 0) return;

                if (!config.GetValue(BehaviorEnabled)) return;
                if (asset == null || GetOrbAsset<A>(tool.OrbSlot) == asset) return;

                /* Can't add a filter for ReparentChildren so gotta make a for loop
                tool.OrbSlot.DestroyChildren(filter: ShouldClearOrbs<A>);
                tool.OrbSlot.ReparentChildren(tool.LocalUserSpace);
                */

                // slot.ForeachChild does not work reverse
                for (int index = tool.OrbSlot.ChildrenCount - 1; index >= 0; index--)
                {
                    Slot child = tool.OrbSlot[index];
                    if (!IsAssetOrb<A>(child)) continue; // For any visuals that may be under the orb slot

                    if (ShouldClearOrb<A>(child)) child.DestroyPreservingAssets();
                    child.SetParent(tool.LocalUserSpace);
                }
            }
            public static bool ShouldClearOrb<A>(Slot orb) where A : class, IAsset
            { // Destroy if setting == 0 |OR| if setting == 1 &AND& does not have asset 
                int scoopSetting = config.GetValue(ScoopModeIndex);
                if (scoopSetting != 1) return scoopSetting == 0; // Return true if Always Delete is enabled | Return false if Always Drop is enabled
                return orb.GetComponent<AssetProxy<A>>() == null; // Return true if asset exists on orb
            }

            public static bool IsAssetOrb<A>(Slot slot) where A : class, IAsset => slot?.GetComponent<AssetProxy<A>>() != null;
            public static IAssetProvider<A> GetOrbAsset<A>(Slot orb) where A : class, IAsset => orb?.GetComponentInChildren<AssetProxy<A>>()?.AssetReference.Target;

        }
    }
}