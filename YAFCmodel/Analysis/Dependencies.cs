﻿using System;
using System.Collections.Generic;

namespace YAFC.Model {
    public interface IDependencyCollector {
        void Add(FactorioId[] raw, DependencyList.Flags flags);
        void Add(IReadOnlyList<FactorioObject> raw, DependencyList.Flags flags);
    }

    public struct DependencyList {
        [Flags]
        public enum Flags {
            RequireEverything = 0x100,
            OneTimeInvestment = 0x200,

            Ingredient = 1 | RequireEverything,
            CraftingEntity = 2 | OneTimeInvestment,
            SourceEntity = 3 | OneTimeInvestment,
            TechnologyUnlock = 4 | OneTimeInvestment,
            Source = 5,
            Fuel = 6,
            ItemToPlace = 7,
            TechnologyPrerequisites = 8 | RequireEverything | OneTimeInvestment,
            IngredientVariant = 9,
            Hidden = 10,
        }

        public Flags flags;
        public FactorioId[] elements;
    }

    public static class Dependencies {
        public static Mapping<FactorioObject, DependencyList[]> dependencyList;
        public static Mapping<FactorioObject, List<FactorioId>> reverseDependencies;

        public static void Calculate() {
            dependencyList = Database.objects.CreateMapping<DependencyList[]>();
            reverseDependencies = Database.objects.CreateMapping<List<FactorioId>>();
            foreach (FactorioObject obj in Database.objects.all) {
                reverseDependencies[obj] = new List<FactorioId>();
            }

            DependencyCollector collector = new DependencyCollector();
            List<FactorioObject> temp = new List<FactorioObject>();
            foreach (FactorioObject obj in Database.objects.all) {
                obj.GetDependencies(collector, temp);
                DependencyList[] packed = collector.Pack();
                dependencyList[obj] = packed;

                foreach (DependencyList group in packed) {
                    foreach (FactorioId req in group.elements) {
                        if (!reverseDependencies[req].Contains(obj.id)) {
                            reverseDependencies[req].Add(obj.id);
                        }
                    }
                }
            }
        }

        private class DependencyCollector : IDependencyCollector {
            private readonly List<DependencyList> list = new List<DependencyList>();

            public void Add(FactorioId[] raw, DependencyList.Flags flags) {
                list.Add(new DependencyList { elements = raw, flags = flags });
            }

            public void Add(IReadOnlyList<FactorioObject> raw, DependencyList.Flags flags) {
                FactorioId[] elems = new FactorioId[raw.Count];
                for (int i = 0; i < raw.Count; i++) {
                    elems[i] = raw[i].id;
                }

                list.Add(new DependencyList { elements = elems, flags = flags });
            }

            public DependencyList[] Pack() {
                DependencyList[] packed = list.ToArray();
                list.Clear();
                return packed;
            }
        }

    }
}
