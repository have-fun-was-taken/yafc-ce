﻿using System;
using System.Linq;
using YAFC.Model;

namespace YAFC.Parser {
    internal partial class FactorioDataDeserializer {
        private T DeserializeWithDifficulty<T>(LuaTable table, string prototypeType, Action<T, LuaTable, bool> loader) where T : FactorioObject, new() {
            T obj = DeserializeCommon<T>(table, prototypeType);
            object current = expensiveRecipes ? table["expensive"] : table["normal"];
            object fallback = expensiveRecipes ? table["normal"] : table["expensive"];
            if (current is LuaTable) {
                loader(obj, current as LuaTable, false);
            }
            else if (fallback is LuaTable) {
                loader(obj, fallback as LuaTable, current is bool b && !b);
            }
            else {
                loader(obj, table, false);
            }

            return obj;
        }

        private void DeserializeRecipe(LuaTable table) {
            Recipe recipe = DeserializeWithDifficulty<Recipe>(table, "recipe", LoadRecipeData);
            _ = table.Get("category", out string recipeCategory, "crafting");
            recipeCategories.Add(recipeCategory, recipe);
            recipe.modules = recipeModules.GetArray(recipe);
            recipe.flags |= RecipeFlags.LimitedByTickRate;
        }

        private void DeserializeFlags(LuaTable table, RecipeOrTechnology recipe, bool forceDisable) {
            recipe.hidden = table.Get("hidden", true);
            recipe.enabled = !forceDisable && table.Get("enabled", true);
        }

        private void DeserializeTechnology(LuaTable table) {
            Technology technology = DeserializeWithDifficulty<Technology>(table, "technology", LoadTechnologyData);
            recipeCategories.Add(SpecialNames.Labs, technology);
            technology.products = Array.Empty<Product>();
        }

        private void UpdateRecipeCatalysts() {
            foreach (Recipe recipe in allObjects.OfType<Recipe>()) {
                foreach (Product product in recipe.products) {
                    if (product.productivityAmount == product.amount) {
                        float catalyst = recipe.GetConsumption(product.goods);
                        if (catalyst > 0f) {
                            product.SetCatalyst(catalyst);
                        }
                    }
                }
            }
        }

        private void UpdateRecipeIngredientFluids() {
            foreach (Recipe recipe in allObjects.OfType<Recipe>()) {
                foreach (Ingredient ingredient in recipe.ingredients) {
                    if (ingredient.goods is Fluid fluid && fluid.variants != null) {
                        int min = -1, max = fluid.variants.Count - 1;
                        for (int i = 0; i < fluid.variants.Count; i++) {
                            Fluid variant = fluid.variants[i];
                            if (variant.temperature < ingredient.temperature.min) {
                                continue;
                            }

                            if (min == -1) {
                                min = i;
                            }

                            if (variant.temperature > ingredient.temperature.max) {
                                max = i - 1;
                                break;
                            }
                        }

                        if (min >= 0 && max >= 0) {
                            ingredient.goods = fluid.variants[min];
                            if (max > min) {
                                Fluid[] fluidVariants = new Fluid[max - min + 1];
                                ingredient.variants = fluidVariants;
                                fluid.variants.CopyTo(min, fluidVariants, 0, max - min + 1);
                            }
                        }
                    }
                }
            }
        }

        private void LoadTechnologyData(Technology technology, LuaTable table, bool forceDisable) {
            _ = table.Get("unit", out LuaTable unit);
            technology.ingredients = LoadIngredientList(unit);
            DeserializeFlags(table, technology, forceDisable);
            technology.time = unit.Get("time", 1f);
            technology.count = unit.Get("count", 1000f);
            if (table.Get("prerequisites", out LuaTable preqList)) {
                technology.prerequisites = preqList.ArrayElements<string>().Select(GetObject<Technology>).ToArray();
            }

            if (table.Get("effects", out LuaTable modifiers)) {
                technology.unlockRecipes = modifiers.ArrayElements<LuaTable>()
                .Select(x => x.Get("type", out string type) && type == "unlock-recipe" && GetRef<Recipe>(x, "recipe", out Recipe recipe) ? recipe : null).Where(x => x != null)
                .ToArray();
            }
        }

        private Product LoadProduct(LuaTable table) {
            return LoadProductWithMultiplier(table, 1);
        }

        private Product LoadProductWithMultiplier(LuaTable table, int multiplier) {
            bool haveExtraData = LoadItemData(out Goods goods, out float amount, table, true);
            amount *= multiplier;
            float min = amount, max = amount;
            if (haveExtraData && amount == 0) {
                _ = table.Get("amount_min", out min);
                _ = table.Get("amount_max", out max);
                min *= multiplier;
                max *= multiplier;
            }

            Product product = new Product(goods, min, max, table.Get("probability", 1f));
            float catalyst = table.Get("catalyst_amount", 0f);
            if (catalyst > 0f) {
                product.SetCatalyst(catalyst);
            }

            return product;
        }

        private Product[] LoadProductList(LuaTable table) {
            if (table.Get("results", out LuaTable resultList)) {
                return resultList.ArrayElements<LuaTable>().Select(LoadProduct).Where(x => x.amount != 0).ToArray();
            }

            _ = table.Get("result", out string name);
            if (name == null) {
                return Array.Empty<Product>();
            }

            Product singleProduct = new Product(GetObject<Item>(name), table.Get("result_count", out float amount) ? amount : table.Get("count", 1));
            return singleProduct.SingleElementArray();
        }

        private Ingredient[] LoadIngredientList(LuaTable table) {
            _ = table.Get("ingredients", out LuaTable ingrList);
            return ingrList.ArrayElements<LuaTable>().Select(x => {
                bool haveExtraData = LoadItemData(out Goods goods, out float amount, x, false);
                Ingredient ingredient = new Ingredient(goods, amount);
                if (haveExtraData && goods is Fluid f) {
                    ingredient.temperature = x.Get("temperature", out int temp)
                        ? new TemperatureRange(temp)
                        : new TemperatureRange(x.Get("minimum_temperature", f.temperatureRange.min), x.Get("maximum_temperature", f.temperatureRange.max));
                }
                return ingredient;
            }).ToArray();
        }

        private void LoadRecipeData(Recipe recipe, LuaTable table, bool forceDisable) {
            recipe.ingredients = LoadIngredientList(table);
            recipe.products = LoadProductList(table);

            recipe.time = table.Get("energy_required", 0.5f);

            if (table.Get("main_product", out string mainProductName) && mainProductName != "") {
                recipe.mainProduct = recipe.products.FirstOrDefault(x => x.goods.name == mainProductName)?.goods;
            }
            else if (recipe.products.Length == 1) {
                recipe.mainProduct = recipe.products[0]?.goods;
            }

            DeserializeFlags(table, recipe, forceDisable);
        }
    }
}
