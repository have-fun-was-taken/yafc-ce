﻿using System;

namespace YAFC.UI {
    public struct SearchQuery {
        public readonly string query;
        public readonly string[] tokens;
        public bool empty => tokens == null || tokens.Length == 0;

        public void SetSearch(string query) {
            this = new SearchQuery(query);
        }

        public SearchQuery(string query) {
            this.query = query;
            tokens = string.IsNullOrWhiteSpace(query) ? Array.Empty<string>() : query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        public bool Match(string text) {
            if (empty) {
                return true;
            }

            foreach (string token in tokens) {
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) {
                    return false;
                }
            }

            return true;
        }
    }
}
