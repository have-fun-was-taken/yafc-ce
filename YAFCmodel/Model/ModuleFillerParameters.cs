﻿using System;

namespace YAFC.Model {
    public interface IModuleFiller {
        void GetModulesInfo(RecipeParameters recipeParams, Recipe recipe, EntityCrafter entity, Goods fuel, ref ModuleEffects effects, ref RecipeParameters.UsedModule used);
    }

    [Serializable]
    public class ModuleFillerParameters : ModelObject<ModelObject>, IModuleFiller {
        public ModuleFillerParameters(ModelObject owner) : base(owner) { }

        public bool fillMiners { get; set; }
        public float autoFillPayback { get; set; }
        public Item fillerModule { get; set; }
        public EntityBeacon beacon { get; set; }
        public Item beaconModule { get; set; }
        public int beaconsPerBuilding { get; set; } = 8;

        [Obsolete("Moved to project settings", true)]
        public int miningProductivity {
            set {
                if (GetRoot() is Project rootProject && rootProject.settings.miningProductivity < value * 0.01f) {
                    rootProject.settings.miningProductivity = value * 0.01f;
                }
            }
        }

        public void AutoFillBeacons(RecipeParameters recipeParams, Recipe recipe, EntityCrafter entity, Goods fuel, ref ModuleEffects effects, ref RecipeParameters.UsedModule used) {
            if (!recipe.flags.HasFlags(RecipeFlags.UsesMiningProductivity) && beacon != null && beaconModule != null) {
                effects.AddModules(beaconModule.module, beaconsPerBuilding * beacon.beaconEfficiency * beacon.moduleSlots, entity.allowedEffects);
                used.beacon = beacon;
                used.beaconCount = beaconsPerBuilding;
            }
        }

        public void AutoFillModules(RecipeParameters recipeParams, Recipe recipe, EntityCrafter entity, Goods fuel, ref ModuleEffects effects, ref RecipeParameters.UsedModule used) {
            if (autoFillPayback > 0 && (fillMiners || !recipe.flags.HasFlags(RecipeFlags.UsesMiningProductivity))) {
                float productivityEconomy = recipe.Cost() / recipeParams.recipeTime;
                float effectivityEconomy = recipeParams.fuelUsagePerSecondPerBuilding * fuel?.Cost() ?? 0;
                if (effectivityEconomy < 0f) {
                    effectivityEconomy = 0f;
                }

                float bestEconomy = 0f;
                Item usedModule = null;
                foreach (Item module in recipe.modules) {
                    if (module.IsAccessibleWithCurrentMilestones() && entity.CanAcceptModule(module.module)) {
                        float economy = (MathF.Max(0f, module.module.productivity) * productivityEconomy) - (module.module.consumption * effectivityEconomy);
                        if (economy > bestEconomy && module.Cost() / economy <= autoFillPayback) {
                            bestEconomy = economy;
                            usedModule = module;
                        }
                    }
                }

                if (usedModule != null) {
                    int count = effects.GetModuleSoftLimit(usedModule.module, entity.moduleSlots);
                    if (count > 0) {
                        effects.AddModules(usedModule.module, count);
                        used.modules = new[] { (usedModule, count, false) };
                        return;
                    }
                }
            }

            if (fillerModule?.module != null && entity.CanAcceptModule(fillerModule.module) && recipe.CanAcceptModule(fillerModule)) {
                AddModuleSimple(fillerModule, ref effects, entity, ref used);
            }
        }

        public void GetModulesInfo(RecipeParameters recipeParams, Recipe recipe, EntityCrafter entity, Goods fuel, ref ModuleEffects effects, ref RecipeParameters.UsedModule used) {
            AutoFillModules(recipeParams, recipe, entity, fuel, ref effects, ref used);
            AutoFillBeacons(recipeParams, recipe, entity, fuel, ref effects, ref used);
        }

        private void AddModuleSimple(Item module, ref ModuleEffects effects, EntityCrafter entity, ref RecipeParameters.UsedModule used) {
            if (module.module != null) {
                int fillerLimit = effects.GetModuleSoftLimit(module.module, entity.moduleSlots);
                effects.AddModules(module.module, fillerLimit);
                used.modules = new[] { (module, fillerLimit, false) };
            }
        }
    }
}
