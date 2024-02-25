﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace YAFC.Parser {
    internal static class DataParserUtils {
        private static class ConvertersFromLua<T> {
            public static Func<object, T, T> convert;
        }

        static DataParserUtils() {
            ConvertersFromLua<int>.convert = (o, def) => o is long l ? (int)l : o is double d ? (int)d : o is string s && int.TryParse(s, out int res) ? res : def;
            ConvertersFromLua<float>.convert = (o, def) => o is long l ? l : o is double d ? (float)d : o is string s && float.TryParse(s, out float res) ? res : def;
            ConvertersFromLua<bool>.convert = delegate (object src, bool def) {
                return src is bool b ? b : src == null ? def : src.Equals("true") || (!src.Equals("false") && def);
            };
        }

        private static bool Parse<T>(object value, out T result, T def = default) {
            if (value == null) {
                result = def;
                return false;
            }

            if (value is T t) {
                result = t;
                return true;
            }
            Func<object, T, T> converter = ConvertersFromLua<T>.convert;
            if (converter == null) {
                result = def;
                return false;
            }

            result = converter(value, def);
            return true;
        }

        public static bool Get<T>(this LuaTable table, string key, out T result, T def = default) {
            return Parse(table[key], out result, def);
        }

        public static bool Get<T>(this LuaTable table, int key, out T result, T def = default) {
            return Parse(table[key], out result, def);
        }

        public static T Get<T>(this LuaTable table, string key, T def) {
            _ = Parse(table[key], out T result, def);
            return result;
        }

        public static T Get<T>(this LuaTable table, int key, T def) {
            _ = Parse(table[key], out T result, def);
            return result;
        }

        public static T[] SingleElementArray<T>(this T item) {
            return new T[] { item };
        }

        public static IEnumerable<T> ArrayElements<T>(this LuaTable table) {
            return table.ArrayElements.OfType<T>();
        }
    }

    public static class SpecialNames {
        public const string BurnableFluid = "burnable-fluid.";
        public const string Heat = "heat";
        public const string Void = "void";
        public const string Electricity = "electricity";
        public const string HotFluid = "hot-fluid";
        public const string SpecificFluid = "fluid.";
        public const string MiningRecipe = "mining.";
        public const string BoilerRecipe = "boiler.";
        public const string FakeRecipe = "fake-recipe";
        public const string FixedRecipe = "fixed-recipe.";
        public const string GeneratorRecipe = "generator";
        public const string PumpingRecipe = "pump.";
        public const string Labs = "labs.";
        public const string RocketLaunch = "launch";
        public const string RocketCraft = "rocket.";
        public const string ReactorRecipe = "reactor";
    }
}
