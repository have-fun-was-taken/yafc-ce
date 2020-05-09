using System.Runtime.CompilerServices;
using System;
using System.Linq;
using YAFC.UI;
[assembly:InternalsVisibleTo("YAFCparser")]

namespace YAFC.Model
{
    public interface IFactorioObjectWrapper
    {
        string text { get; }
        FactorioObject target { get; }
    }

    internal enum FactorioObjectSortOrder
    {
        SpecialGoods,
        Items,
        Fluids,
        Recipes,
        Mechanics,
        Technologies,
        Entities
    }
    
    public abstract class FactorioObject : IFactorioObjectWrapper, IComparable<FactorioObject>
    {
        public string type { get; internal set; }
        public string name { get; internal set; }
        public string typeDotName { get; internal set; }
        public string locName { get; internal set; }
        public string locDescr { get; internal set; }
        public FactorioIconPart[] iconSpec { get; internal set; }
        public Icon icon { get; internal set; }
        public int id { get; internal set; }
        internal abstract FactorioObjectSortOrder sortingOrder { get; }

        FactorioObject IFactorioObjectWrapper.target => this;
        string IFactorioObjectWrapper.text => locName;

        public void FallbackLocalization(FactorioObject other, string description)
        {
            if (locName == null)
            {
                if (other == null)
                    locName = name;
                else
                {
                    locName = other.locName;
                    locDescr = description + " " + locName;
                }
            }
        }

        public abstract void GetDependencies(IDependencyCollector collector);

        public override string ToString() => name;

        public int CompareTo(FactorioObject other) => DataUtils.DefaultOrdering.Compare(this, other);
    }
    
    public class FactorioIconPart
    {
        public string path;
        public float size = 32;
        public float x, y, r = 1, g = 1, b = 1, a = 1;
        public float scale = 1;

        public bool IsSimple()
        {
            return x == 0 && y == 0 && r == 1 && g == 1 && b == 1 && a == 1 && scale == 1;
        }
    }

    [Flags]
    public enum RecipeFlags
    {
        UsesMiningProductivity = 1 << 0,
        //ProductivityDisabled = 1 << 1,
        UsesFluidTemperature = 1 << 2,
        ScaleProductionWithPower = 1 << 3,
    }
    
    public class Recipe : FactorioObject
    {
        public PackedList<Entity> crafters { get; internal set; }
        public Ingredient[] ingredients { get; internal set; }
        public Product[] products { get; internal set; }
        public Item[] modules { get; internal set; } = Array.Empty<Item>();
        public PackedList<Technology> technologyUnlock { get; internal set; }
        public Entity sourceEntity { get; internal set; }
        public Goods mainProduct { get; internal set; }
        public float time { get; internal set; }
        public bool enabled { get; internal set; }
        public bool hidden { get; internal set; }
        public RecipeFlags flags { get; internal set; }
        internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Recipes;

        public override void GetDependencies(IDependencyCollector collector)
        {
            if (ingredients.Length > 0)
            {
                var ingList = new int[ingredients.Length];
                for (var i = 0; i < ingredients.Length; i++)
                    ingList[i] = ingredients[i].goods.id;
                collector.Add(ingList, DependencyList.Flags.Ingredient);
            }
            collector.Add(crafters, DependencyList.Flags.CraftingEntity);
            if (sourceEntity != null)
                collector.Add(new[] {sourceEntity.id}, DependencyList.Flags.SourceEntity);
            if (!enabled)
                collector.Add(technologyUnlock, DependencyList.Flags.TechnologyUnlock);
        }

        public bool CanFit(int itemInputs, int fluidInputs, Goods[] slots)
        {
            foreach (var ingredient in ingredients)
            {
                if (ingredient.goods is Item && --itemInputs < 0) return false;
                if (ingredient.goods is Fluid && --fluidInputs < 0) return false;
                if (slots != null && !slots.Contains(ingredient.goods)) return false;
            }
            return true;
        }
    }

    public class Mechanics : Recipe
    {
        internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Mechanics;
    }
    
    public class Ingredient : IFactorioObjectWrapper
    {
        public readonly Goods goods;
        public readonly float amount;
        public float minTemperature { get; internal set; }
        public float maxTemperature { get; internal set; }
        public Ingredient(Goods goods, float amount)
        {
            this.goods = goods;
            this.amount = amount;
            if (goods is Fluid fluid)
            {
                minTemperature = fluid.minTemperature;
                maxTemperature = fluid.maxTemperature;
            }
        }

        string IFactorioObjectWrapper.text
        {
            get
            {
                var text = goods.locName;
                if (amount != 1f)
                    text = amount + "x " + text;
                if (minTemperature != 0 || maxTemperature != 0)
                    text += " ("+minTemperature+"°-"+maxTemperature+"°)";
                return text;
            }
        }

        FactorioObject IFactorioObjectWrapper.target => goods;
    }
    
    public class Product : IFactorioObjectWrapper
    {
        public readonly Goods goods;
        public readonly float amount;
        public Product(Goods goods, float amount)
        {
            this.goods = goods;
            this.amount = amount;
        }

        public float average => amount * probability;
        public float temperature { get; internal set; }
        public float probability { get; internal set; } = 1;

        FactorioObject IFactorioObjectWrapper.target => goods;

        string IFactorioObjectWrapper.text
        {
            get
            {
                var text = goods.locName;
                if (amount != 1f)
                    text = amount + "x " + text;
                if (probability != 1)
                    text = (probability * 100) + "% " + text;
                if (temperature != 0)
                    text += " (" + temperature + "°)";
                return text;
            }
        }
    }

    // Abstract base for anything that can be produced or consumed by recipes (etc)
    public abstract class Goods : FactorioObject
    {
        public float fuelValue;
        public abstract bool isPower { get; }
        public virtual Fluid fluid => null;
        public Recipe[] production { get; internal set; }
        public Recipe[] usages { get; internal set; }
        public Entity[] loot { get; internal set; }

        public override void GetDependencies(IDependencyCollector collector)
        {
            collector.Add(new PackedList<FactorioObject>(production.Concat<FactorioObject>(loot)), DependencyList.Flags.Source);
        }
    }
    
    public class Item : Goods
    {
        public Item fuelResult { get; internal set; }
        public Entity placeResult { get; internal set; }
        public ModuleSpecification module { get; internal set; }
        public override bool isPower => false;
        internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Items;
    }
    
    public class Fluid : Goods
    {
        public override Fluid fluid => this;
        public float heatCapacity { get; internal set; } = 1e-3f;
        public float minTemperature { get; internal set; }
        public float maxTemperature { get; internal set; }
        public override bool isPower => false;
        internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Fluids;
    }
    
    public class Special : Goods
    {
        internal bool power;
        public override bool isPower => power;
        internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.SpecialGoods;
    } 
    
    public class Entity : FactorioObject
    {
        public Product[] loot { get; internal set; }
        public PackedList<Recipe> recipes { get; internal set; }
        public bool mapGenerated { get; internal set; }
        public float mapGenDensity { get; internal set; }
        public float power { get; internal set; }
        public EntityEnergy energy { get; internal set; }
        public float craftingSpeed { get; internal set; } = 1f;
        public float productivity { get; internal set; }
        public PackedList<Item> itemsToPlace { get; internal set; }
        public int itemInputs { get; internal set; }
        public int fluidInputs { get; internal set; } // fluid inputs for recipe, not including power
        public Goods[] inputs { get; internal set; }
        internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Entities;

        public override void GetDependencies(IDependencyCollector collector)
        {
            if (energy != null)
                collector.Add(energy.fuels, DependencyList.Flags.Fuel);
            if (mapGenerated)
                return;
            collector.Add(itemsToPlace, DependencyList.Flags.ItemToPlace);
        }
    }

    public class Technology : Recipe // Technology is very similar to recipe
    {
        public float count { get; internal set; } // TODO support formula count
        public Technology[] prerequisites { get; internal set; }
        public Recipe[] unlockRecipes { get; internal set; }
        internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Technologies;

        public override void GetDependencies(IDependencyCollector collector)
        {
            base.GetDependencies(collector);
            if (prerequisites.Length > 0)
                collector.Add(new PackedList<Technology>(prerequisites), DependencyList.Flags.TechnologyPrerequisites);
        }
    }

    public class EntityEnergy
    {
        public bool usesHeat { get; internal set; }
        public float minTemperature { get; internal set; }
        public float maxTemperature { get; internal set; }
        public float fluidLimit { get; internal set; } = float.PositiveInfinity;
        public PackedList<Goods> fuels { get; internal set; }
        public float effectivity { get; internal set; } = 1f;
    }

    public class ModuleSpecification
    {
        public float consumption { get; internal set; }
        public float speed { get; internal set; }
        public float productivity { get; internal set; }
        public float pollution { get; internal set; }
        public Recipe[] limitation { get; internal set; }
    }
}