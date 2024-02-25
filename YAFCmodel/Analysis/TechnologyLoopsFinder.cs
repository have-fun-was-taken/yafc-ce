﻿using System;
using System.Linq;

namespace YAFC.Model {
    public static class TechnologyLoopsFinder {
        public static void FindTechnologyLoops() {
            Graph<Technology> graph = new Graph<Technology>();
            foreach (Technology technology in Database.technologies.all) {
                foreach (Technology preq in technology.prerequisites) {
                    graph.Connect(preq, technology);
                }
            }

            Graph<(Technology single, Technology[] list)> merged = graph.MergeStrongConnectedComponents();
            bool loops = false;
            foreach (Graph<(Technology single, Technology[] list)>.Node m in merged) {
                if (m.userdata.list != null) {
                    Console.WriteLine("Technology loop: " + string.Join(", ", m.userdata.list.Select(x => x.locName)));
                    loops = true;
                }
            }
            if (!loops) {
                Console.WriteLine("No technology loops found");
            }
        }
    }
}
