﻿using System;
using System.Collections.Generic;
using System.Linq;
using YAFC.Model;

namespace YAFC.Parser {
    internal partial class FactorioDataDeserializer {
        private readonly List<FactorioObject> allObjects = new List<FactorioObject>();
        private readonly List<FactorioObject> rootAccessible = new List<FactorioObject>();
        private readonly Dictionary<(Type type, string name), FactorioObject> registeredObjects = new Dictionary<(Type type, string name), FactorioObject>();
        private readonly DataBucket<string, Goods> fuels = new DataBucket<string, Goods>();
        private readonly DataBucket<Entity, string> fuelUsers = new DataBucket<Entity, string>();
        private readonly DataBucket<string, RecipeOrTechnology> recipeCategories = new DataBucket<string, RecipeOrTechnology>();
        private readonly DataBucket<EntityCrafter, string> recipeCrafters = new DataBucket<EntityCrafter, string>();
        private readonly DataBucket<Recipe, Item> recipeModules = new DataBucket<Recipe, Item>();
        private readonly Dictionary<Item, string> placeResults = new Dictionary<Item, string>();
        private readonly List<Item> universalModules = new List<Item>();
        private Item[] allModules;
        private readonly HashSet<Item> sciencePacks = new HashSet<Item>();
        private readonly Dictionary<string, List<Fluid>> fluidVariants = new Dictionary<string, List<Fluid>>();
        private readonly Dictionary<string, FactorioObject> formerAliases = new Dictionary<string, FactorioObject>();
        private readonly Dictionary<string, int> rocketInventorySizes = new Dictionary<string, int>();

        private readonly bool expensiveRecipes;

        private Recipe generatorProduction;
        private Recipe reactorProduction;
        private Special voidEnergy;
        private Special heat;
        private Special electricity;
        private Special rocketLaunch;
        private EntityEnergy voidEntityEnergy;
        private EntityEnergy laborEntityEnergy;
        private Entity character;
        private readonly Version factorioVersion;

        private static readonly Version v0_18 = new Version(0, 18);

        public FactorioDataDeserializer(bool expensiveRecipes, Version factorioVersion) {
            this.expensiveRecipes = expensiveRecipes;
            this.factorioVersion = factorioVersion;
            RegisterSpecial();
        }

        private Special CreateSpecialObject(bool isPower, string name, string locName, string locDescr, string icon, string signal) {
            Special obj = GetObject<Special>(name);
            obj.virtualSignal = signal;
            obj.factorioType = "special";
            obj.locName = locName;
            obj.locDescr = locDescr;
            obj.iconSpec = new FactorioIconPart { path = icon }.SingleElementArray();
            obj.power = isPower;
            if (isPower) {
                obj.fuelValue = 1f;
            }

            return obj;
        }

        private void RegisterSpecial() {
            electricity = CreateSpecialObject(true, SpecialNames.Electricity, "Electricity", "This is an object that represents electric energy",
                "__core__/graphics/icons/alerts/electricity-icon-unplugged.png", "signal-E");
            fuels.Add(SpecialNames.Electricity, electricity);

            heat = CreateSpecialObject(true, SpecialNames.Heat, "Heat", "This is an object that represents heat energy", "__core__/graphics/arrows/heat-exchange-indication.png", "signal-H");
            fuels.Add(SpecialNames.Heat, heat);

            voidEnergy = CreateSpecialObject(true, SpecialNames.Void, "Void", "This is an object that represents infinite energy", "__core__/graphics/icons/mip/infinity.png", "signal-V");
            fuels.Add(SpecialNames.Void, voidEnergy);
            rootAccessible.Add(voidEnergy);

            rocketLaunch = CreateSpecialObject(false, SpecialNames.RocketLaunch, "Rocket launch slot", "This is a slot in a rocket ready to be launched", "__base__/graphics/entity/rocket-silo/02-rocket.png", "signal-R");

            generatorProduction = CreateSpecialRecipe(electricity, SpecialNames.GeneratorRecipe, "generating");
            generatorProduction.products = new Product(electricity, 1f).SingleElementArray();
            generatorProduction.flags |= RecipeFlags.ScaleProductionWithPower;
            generatorProduction.ingredients = Array.Empty<Ingredient>();

            reactorProduction = CreateSpecialRecipe(heat, SpecialNames.ReactorRecipe, "generating");
            reactorProduction.products = new Product(heat, 1f).SingleElementArray();
            reactorProduction.flags |= RecipeFlags.ScaleProductionWithPower;
            reactorProduction.ingredients = Array.Empty<Ingredient>();

            voidEntityEnergy = new EntityEnergy { type = EntityEnergyType.Void, effectivity = float.PositiveInfinity };
            laborEntityEnergy = new EntityEnergy { type = EntityEnergyType.Labor, effectivity = float.PositiveInfinity };
        }

        private T GetObject<T>(string name) where T : FactorioObject, new() {
            return GetObject<T, T>(name);
        }

        private TActual GetObject<TNominal, TActual>(string name) where TNominal : FactorioObject where TActual : TNominal, new() {
            (Type, string name) key = (typeof(TNominal), name);
            if (registeredObjects.TryGetValue(key, out FactorioObject existing)) {
                return (TActual)existing;
            }

            TActual newItem = new TActual { name = name };
            allObjects.Add(newItem);
            registeredObjects[key] = newItem;
            return newItem;
        }

        private int Skip(int from, FactorioObjectSortOrder sortOrder) {
            for (; from < allObjects.Count; from++) {
                if (allObjects[from].sortingOrder != sortOrder) {
                    break;
                }
            }

            return from;
        }

        private void ExportBuiltData() {
            Database.rootAccessible = rootAccessible.ToArray();
            Database.objectsByTypeName = allObjects.ToDictionary(x => x.typeDotName = x.type + "." + x.name);
            foreach (KeyValuePair<string, FactorioObject> alias in formerAliases) {
                _ = Database.objectsByTypeName.TryAdd(alias.Key, alias.Value);
            }

            Database.allSciencePacks = sciencePacks.ToArray();
            Database.voidEnergy = voidEnergy;
            Database.electricity = electricity;
            Database.electricityGeneration = generatorProduction;
            Database.heat = heat;
            Database.character = character;
            int firstSpecial = 0;
            int firstItem = Skip(firstSpecial, FactorioObjectSortOrder.SpecialGoods);
            int firstFluid = Skip(firstItem, FactorioObjectSortOrder.Items);
            int firstRecipe = Skip(firstFluid, FactorioObjectSortOrder.Fluids);
            int firstMechanics = Skip(firstRecipe, FactorioObjectSortOrder.Recipes);
            int firstTechnology = Skip(firstMechanics, FactorioObjectSortOrder.Mechanics);
            int firstEntity = Skip(firstTechnology, FactorioObjectSortOrder.Technologies);
            int last = Skip(firstEntity, FactorioObjectSortOrder.Entities);
            if (last != allObjects.Count) {
                throw new Exception("Something is not right");
            }

            Database.objects = new FactorioIdRange<FactorioObject>(0, last, allObjects);
            Database.specials = new FactorioIdRange<Special>(firstSpecial, firstItem, allObjects);
            Database.items = new FactorioIdRange<Item>(firstItem, firstFluid, allObjects);
            Database.fluids = new FactorioIdRange<Fluid>(firstFluid, firstRecipe, allObjects);
            Database.goods = new FactorioIdRange<Goods>(firstSpecial, firstRecipe, allObjects);
            Database.recipes = new FactorioIdRange<Recipe>(firstRecipe, firstTechnology, allObjects);
            Database.mechanics = new FactorioIdRange<Mechanics>(firstMechanics, firstTechnology, allObjects);
            Database.recipesAndTechnologies = new FactorioIdRange<RecipeOrTechnology>(firstRecipe, firstEntity, allObjects);
            Database.technologies = new FactorioIdRange<Technology>(firstTechnology, firstEntity, allObjects);
            Database.entities = new FactorioIdRange<Entity>(firstEntity, last, allObjects);
            Database.fluidVariants = fluidVariants;

            Database.allModules = allModules;
            Database.allBeacons = Database.entities.all.OfType<EntityBeacon>().ToArray();
            Database.allCrafters = Database.entities.all.OfType<EntityCrafter>().ToArray();
            Database.allBelts = Database.entities.all.OfType<EntityBelt>().ToArray();
            Database.allInserters = Database.entities.all.OfType<EntityInserter>().ToArray();
            Database.allAccumulators = Database.entities.all.OfType<EntityAccumulator>().ToArray();
            Database.allContainers = Database.entities.all.OfType<EntityContainer>().ToArray();
        }

        private bool IsBarrelingRecipe(Recipe barreling, Recipe unbarreling) {
            Product product = barreling.products[0];
            if (product.probability != 1f) {
                return false;
            }

            if (product.goods is not Item barrel) {
                return false;
            }

            if (unbarreling.ingredients.Length != 1) {
                return false;
            }

            Ingredient ingredient = unbarreling.ingredients[0];
            if (ingredient.variants != null || ingredient.goods != barrel || ingredient.amount != product.amount) {
                return false;
            }

            if (unbarreling.products.Length != barreling.ingredients.Length) {
                return false;
            }

            if (barrel.miscSources.Length != 0 || barrel.fuelValue != 0f || barrel.placeResult != null || barrel.module != null) {
                return false;
            }

            foreach ((Product testProduct, Ingredient testIngredient) in unbarreling.products.Zip(barreling.ingredients)) {
                if (testProduct.probability != 1f || testProduct.goods != testIngredient.goods || testIngredient.variants != null || testProduct.amount != testIngredient.amount) {
                    return false;
                }
            }
            return !unbarreling.IsProductivityAllowed() && !barreling.IsProductivityAllowed();
        }

        private void CalculateMaps() {
            DataBucket<Goods, Recipe> itemUsages = new DataBucket<Goods, Recipe>();
            DataBucket<Goods, Recipe> itemProduction = new DataBucket<Goods, Recipe>();
            DataBucket<Goods, FactorioObject> miscSources = new DataBucket<Goods, FactorioObject>();
            DataBucket<Entity, Item> entityPlacers = new DataBucket<Entity, Item>();
            DataBucket<Recipe, Technology> recipeUnlockers = new DataBucket<Recipe, Technology>();
            // Because actual recipe availibility may be different than just "all recipes from that category" because of item slot limit and fluid usage restriction, calculate it here
            DataBucket<RecipeOrTechnology, EntityCrafter> actualRecipeCrafters = new DataBucket<RecipeOrTechnology, EntityCrafter>();
            DataBucket<Goods, Entity> usageAsFuel = new DataBucket<Goods, Entity>();
            List<Recipe> allRecipes = new List<Recipe>();
            List<Mechanics> allMechanics = new List<Mechanics>();

            // step 1 - collect maps

            foreach (FactorioObject o in allObjects) {
                switch (o) {
                    case Technology technology:
                        foreach (Recipe recipe in technology.unlockRecipes) {
                            recipeUnlockers.Add(recipe, technology);
                        }

                        break;
                    case Recipe recipe:
                        allRecipes.Add(recipe);
                        foreach (Product product in recipe.products) {
                            if (product.amount > 0) {
                                itemProduction.Add(product.goods, recipe);
                            }
                        }

                        foreach (Ingredient ingredient in recipe.ingredients) {
                            if (ingredient.variants == null) {
                                itemUsages.Add(ingredient.goods, recipe);
                            }
                            else {
                                ingredient.goods = ingredient.variants[0];
                                foreach (Goods variant in ingredient.variants) {
                                    itemUsages.Add(variant, recipe);
                                }
                            }
                        }
                        if (recipe is Mechanics mechanics) {
                            allMechanics.Add(mechanics);
                        }

                        break;
                    case Item item:
                        if (placeResults.TryGetValue(item, out string placeResultStr)) {
                            item.placeResult = GetObject<Entity>(placeResultStr);
                            entityPlacers.Add(item.placeResult, item);
                        }
                        if (item.fuelResult != null) {
                            miscSources.Add(item.fuelResult, item);
                        }

                        break;
                    case Entity entity:
                        foreach (Product product in entity.loot) {
                            miscSources.Add(product.goods, entity);
                        }

                        if (entity is EntityCrafter crafter) {
                            crafter.recipes = recipeCrafters.GetRaw(crafter).SelectMany(x => recipeCategories.GetRaw(x).Where(y => y.CanFit(crafter.itemInputs, crafter.fluidInputs, crafter.inputs))).ToArray();
                            foreach (RecipeOrTechnology recipe in crafter.recipes) {
                                actualRecipeCrafters.Add(recipe, crafter, true);
                            }
                        }
                        if (entity.energy != null && entity.energy != voidEntityEnergy) {
                            IEnumerable<Goods> fuelList = fuelUsers.GetRaw(entity).SelectMany(fuels.GetRaw);
                            if (entity.energy.type == EntityEnergyType.FluidHeat) {
                                fuelList = fuelList.Where(x => x is Fluid f && entity.energy.acceptedTemperature.Contains(f.temperature) && f.temperature > entity.energy.workingTemperature.min);
                            }

                            Goods[] fuelListArr = fuelList.ToArray();
                            entity.energy.fuels = fuelListArr;
                            foreach (Goods fuel in fuelListArr) {
                                usageAsFuel.Add(fuel, entity);
                            }
                        }
                        break;
                }
            }

            voidEntityEnergy.fuels = new Goods[] { voidEnergy };

            actualRecipeCrafters.Seal();
            usageAsFuel.Seal();
            recipeUnlockers.Seal();
            entityPlacers.Seal();

            // step 2 - fill maps

            foreach (FactorioObject o in allObjects) {
                switch (o) {
                    case RecipeOrTechnology recipeOrTechnology:
                        if (recipeOrTechnology is Recipe recipe) {
                            recipe.FallbackLocalization(recipe.mainProduct, "A recipe to create");
                            recipe.technologyUnlock = recipeUnlockers.GetArray(recipe);
                        }
                        recipeOrTechnology.crafters = actualRecipeCrafters.GetArray(recipeOrTechnology);
                        break;
                    case Goods goods:
                        goods.usages = itemUsages.GetArray(goods);
                        goods.production = itemProduction.GetArray(goods);
                        goods.miscSources = miscSources.GetArray(goods);
                        if (o is Item item) {
                            if (item.placeResult != null) {
                                item.FallbackLocalization(item.placeResult, "An item to build");
                            }
                        }
                        else if (o is Fluid fluid && fluid.variants != null) {
                            string temperatureDescr = "Temperature: " + fluid.temperature + "°";
                            fluid.locDescr = fluid.locDescr == null ? temperatureDescr : temperatureDescr + "\n" + fluid.locDescr;
                        }

                        goods.fuelFor = usageAsFuel.GetArray(goods);
                        break;
                    case Entity entity:
                        entity.itemsToPlace = entityPlacers.GetArray(entity);
                        break;
                }
            }

            foreach (Mechanics mechanic in allMechanics) {
                mechanic.locName = mechanic.source.locName + " " + mechanic.locName;
                mechanic.locDescr = mechanic.source.locDescr;
                mechanic.iconSpec = mechanic.source.iconSpec;
            }

            // step 3 - detect barreling/unbarreling and voiding recipes
            foreach (Recipe recipe in allRecipes) {
                if (recipe.specialType != FactorioObjectSpecialType.Normal) {
                    continue;
                }

                if (recipe.products.Length == 0) {
                    recipe.specialType = FactorioObjectSpecialType.Voiding;
                    continue;
                }
                if (recipe.products.Length != 1 || recipe.ingredients.Length == 0) {
                    continue;
                }

                if (recipe.products[0].goods is Item barrel) {
                    foreach (Recipe usage in barrel.usages) {
                        if (IsBarrelingRecipe(recipe, usage)) {
                            recipe.specialType = FactorioObjectSpecialType.Barreling;
                            usage.specialType = FactorioObjectSpecialType.Unbarreling;
                            barrel.specialType = FactorioObjectSpecialType.FilledBarrel;
                        }
                    }
                }
            }

            foreach (FactorioObject any in allObjects) {
                any.locName ??= any.name;
            }

            foreach ((string _, List<Fluid> list) in fluidVariants) {
                foreach (Fluid fluid in list) {
                    fluid.locName += " " + fluid.temperature + "°";
                }
            }
        }

        private Recipe CreateSpecialRecipe(FactorioObject production, string category, string hint) {
            string fullName = category + (category.EndsWith(".") ? "" : ".") + production.name;
            if (registeredObjects.TryGetValue((typeof(Mechanics), fullName), out FactorioObject recipeRaw)) {
                return recipeRaw as Recipe;
            }

            Mechanics recipe = GetObject<Mechanics>(fullName);
            recipe.time = 1f;
            recipe.factorioType = SpecialNames.FakeRecipe;
            recipe.name = fullName;
            recipe.source = production;
            recipe.locName = hint;
            recipe.enabled = true;
            recipe.hidden = true;
            recipe.technologyUnlock = Array.Empty<Technology>();
            recipeCategories.Add(category, recipe);
            return recipe;
        }

        private class DataBucket<TKey, TValue> : IEqualityComparer<List<TValue>> {
            private readonly Dictionary<TKey, IList<TValue>> storage = new Dictionary<TKey, IList<TValue>>();
            /// <summary>This function provides a default list of values for the key for when the key is not present in the storage.</summary>
            /// <remarks>The provided function must *must not* return null.</remarks>
            private Func<TKey, IEnumerable<TValue>> defaultList = NoExtraItems;

            /// <summary>When true, it is not allowed to add new items to this bucket.</summary>
            private bool isSealed;

            /// <summary>
            /// Replaces the list values in storage with array values while (optionally) adding extra values depending on the item.
            /// </summary>
            /// <param name="addExtraItems">Function to provide extra items, *must not* return null.</param>
            public void Seal(Func<TKey, IEnumerable<TValue>> addExtraItems = null) {
                if (isSealed) {
                    throw new InvalidOperationException("Data bucket is already sealed");
                }

                if (addExtraItems != null) {
                    defaultList = addExtraItems;
                }

                KeyValuePair<TKey, IList<TValue>>[] values = storage.ToArray();
                foreach ((TKey key, IList<TValue> value) in values) {
                    if (value is not List<TValue> list) {
                        // Unexpected type, (probably) never happens
                        continue;
                    }

                    // Add the extra values to the list when provided before storing the complete array.
                    IEnumerable<TValue> completeList = addExtraItems != null ? list.Concat(addExtraItems(key)) : list;
                    TValue[] completeArray = completeList.ToArray();

                    storage[key] = completeArray;
                }

                isSealed = true;
            }

            public void Add(TKey key, TValue value, bool checkUnique = false) {
                if (isSealed) {
                    throw new InvalidOperationException("Data bucket is sealed");
                }

                if (key == null) {
                    return;
                }

                if (!storage.TryGetValue(key, out IList<TValue> list)) {
                    storage[key] = new List<TValue> { value };
                }
                else if (!checkUnique || !list.Contains(value)) {
                    list.Add(value);
                }
            }

            public TValue[] GetArray(TKey key) {
                return !storage.TryGetValue(key, out global::System.Collections.Generic.IList<TValue> list) ? defaultList(key).ToArray() : list is TValue[] value ? value : list.ToArray();
            }

            public IList<TValue> GetRaw(TKey key) {
                if (!storage.TryGetValue(key, out IList<TValue> list)) {
                    list = defaultList(key).ToList();
                    if (isSealed) {
                        list = list.ToArray();
                    }

                    storage[key] = list;
                }
                return list;
            }

            ///<summary>Just return an empty enumerable.</summary>
            private static IEnumerable<TValue> NoExtraItems(TKey item) {
                return Enumerable.Empty<TValue>();
            }

            public bool Equals(List<TValue> x, List<TValue> y) {
                if (x.Count != y.Count) {
                    return false;
                }

                EqualityComparer<TValue> comparer = EqualityComparer<TValue>.Default;
                for (int i = 0; i < x.Count; i++) {
                    if (!comparer.Equals(x[i], y[i])) {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(List<TValue> obj) {
                int count = obj.Count;
                return count == 0 ? 0 : (((obj.Count * 347) + obj[0].GetHashCode()) * 347) + obj[count - 1].GetHashCode();
            }
        }

        public Type TypeNameToType(string typeName) {
            return typeName switch {
                "item" => typeof(Item),
                "fluid" => typeof(Fluid),
                "technology" => typeof(Technology),
                "recipe" => typeof(Recipe),
                "entity" => typeof(Entity),
                _ => null,
            };
        }

        private void ParseModYafcHandles(LuaTable scriptEnabled) {
            if (scriptEnabled != null) {
                foreach (object element in scriptEnabled.ArrayElements) {
                    if (element is LuaTable table) {
                        _ = table.Get("type", out string type);
                        _ = table.Get("name", out string name);
                        if (registeredObjects.TryGetValue((TypeNameToType(type), name), out FactorioObject existing)) {
                            rootAccessible.Add(existing);
                        }
                    }
                }
            }
        }
    }
}
