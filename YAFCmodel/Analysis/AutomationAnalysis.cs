using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace YAFC.Model
{
    public static class AutomationAnalysis
    {
        private static bool[] automatableList;

        public static bool IsAutomatable(this FactorioObject obj) => automatableList[obj.id];
        
        private enum ProcessingState : byte
        {
            None, InQueue, Automatable, NotAutomatable
        }

        public static void Process()
        {
            var time = Stopwatch.StartNew();
            var state = new ProcessingState[Database.allObjects.Length];
            var processingQueue = new Queue<int>(Database.allRecipes.Length);
            foreach (var obj in Database.allObjects)
                if (!obj.IsAccessible())
                    state[obj.id] = ProcessingState.NotAutomatable;
            foreach (var recipe in Database.allRecipes)
            {
                if (state[recipe.id] == ProcessingState.NotAutomatable)
                    continue;
                var hasAutomatableCrafter = false;
                foreach (var crafter in recipe.crafters)
                {
                    if (crafter != Database.character && crafter.IsAccessible())
                        hasAutomatableCrafter = true;
                }
                if (!hasAutomatableCrafter)
                    state[recipe.id] = ProcessingState.NotAutomatable;
                else
                {
                    state[recipe.id] = ProcessingState.InQueue;
                    processingQueue.Enqueue(recipe.id);
                }
            }

            while (processingQueue.Count > 0)
            {
                var index = processingQueue.Dequeue();
                var dependencies = Dependencies.dependencyList[index];
                var automatable = true;
                foreach (var depGroup in dependencies)
                {
                    if ((depGroup.flags & DependencyList.Flags.OneTimeInvestment) != 0)
                        continue;
                    if ((depGroup.flags & DependencyList.Flags.RequireEverything) != 0)
                    {
                        foreach (var element in depGroup.elements)
                            if (state[element] != ProcessingState.Automatable)
                            {
                                automatable = false;
                                break;
                            }
                    }
                    else
                    {
                        automatable = false;
                        foreach (var element in depGroup.elements)
                            if (state[element] == ProcessingState.Automatable)
                            {
                                automatable = true;
                                break;
                            }
                    }
                    if (!automatable)
                        break;
                }
                state[index] = automatable ? ProcessingState.Automatable : ProcessingState.None;
                if (automatable)
                {
                    foreach (var revDep in Dependencies.reverseDependencies[index])
                    {
                        if (state[revDep] == ProcessingState.None)
                        {
                            processingQueue.Enqueue(revDep);
                            state[revDep] = ProcessingState.InQueue;
                        }
                    }
                }
            }

            var result = new bool[Database.allObjects.Length];
            for (var i = 0; i < state.Length; i++)
                if (state[i] == ProcessingState.Automatable)
                    result[i] = true;
            automatableList = result;
            Console.WriteLine("Automation analysis finished in "+time.ElapsedMilliseconds+" ms");
        }
    }
}