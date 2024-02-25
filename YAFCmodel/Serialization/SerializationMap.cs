﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace YAFC.Model {
    [AttributeUsage(AttributeTargets.Property)]
    public class SkipSerializationAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class NoUndoAttribute : Attribute { }

    internal abstract class SerializationMap {
        private static readonly UndoSnapshotBuilder snapshotBuilder = new UndoSnapshotBuilder();
        private static readonly UndoSnapshotReader snapshotReader = new UndoSnapshotReader();

        public UndoSnapshot MakeUndoSnapshot(ModelObject target) {
            snapshotBuilder.BeginBuilding(target);
            BuildUndo(target, snapshotBuilder);
            return snapshotBuilder.Build();
        }
        public void RevertToUndoSnapshot(ModelObject target, UndoSnapshot snapshot) {
            snapshotReader.DoSnapshot(snapshot);
            ReadUndo(target, snapshotReader);
        }
        public abstract void BuildUndo(object target, UndoSnapshotBuilder builder);
        public abstract void ReadUndo(object target, UndoSnapshotReader reader);


        private static readonly Dictionary<Type, SerializationMap> undoBuilders = new Dictionary<Type, SerializationMap>();

        public static SerializationMap GetSerializationMap(Type type) {
            return undoBuilders.TryGetValue(type, out SerializationMap builder)
                ? builder
                : (undoBuilders[type] = Activator.CreateInstance(typeof(SerializationMap<>.SpecificSerializationMap).MakeGenericType(type)) as SerializationMap);
        }

        public abstract void SerializeToJson(object target, Utf8JsonWriter writer);
        public abstract void PopulateFromJson(object target, ref Utf8JsonReader reader, DeserializationContext context);
    }

    internal static class SerializationMap<T> where T : class {
        private static readonly Type parentType;
        private static readonly ConstructorInfo constructor;
        private static readonly PropertySerializer<T>[] properties;
        private static readonly int constructorProperties;
        private static readonly ulong constructorFieldMask;
        private static readonly ulong requiredConstructorFieldMask;

        public class SpecificSerializationMap : SerializationMap {
            public override void BuildUndo(object target, UndoSnapshotBuilder builder) {
                T t = target as T;
                foreach (PropertySerializer<T> property in properties) {
                    if (property.type == PropertyType.Normal) {
                        property.SerializeToUndoBuilder(t, builder);
                    }
                }
            }

            public override void ReadUndo(object target, UndoSnapshotReader reader) {
                T t = target as T;
                foreach (PropertySerializer<T> property in properties) {
                    if (property.type == PropertyType.Normal) {
                        property.DeserializeFromUndoBuilder(t, reader);
                    }
                }
            }

            public override void SerializeToJson(object target, Utf8JsonWriter writer) {
                SerializationMap<T>.SerializeToJson(target as T, writer);
            }

            public override void PopulateFromJson(object target, ref Utf8JsonReader reader, DeserializationContext context) {
                SerializationMap<T>.PopulateFromJson(target as T, ref reader, context);
            }
        }

        private static bool GetInterfaceSerializer(Type iface, out Type serializerType, out Type keyType, out Type elementType) {
            if (iface.IsGenericType) {
                Type definition = iface.GetGenericTypeDefinition();
                if (definition == typeof(ICollection<>)) {
                    elementType = iface.GetGenericArguments()[0];
                    keyType = null;
                    if (ValueSerializer.IsValueSerializerSupported(elementType)) {
                        serializerType = typeof(CollectionSerializer<,,>);
                        return true;
                    }
                }

                if (definition == typeof(IDictionary<,>)) {
                    Type[] args = iface.GetGenericArguments();
                    if (ValueSerializer.IsValueSerializerSupported(args[0])) {
                        keyType = args[0];
                        elementType = args[1];
                        if (ValueSerializer.IsValueSerializerSupported(elementType)) {
                            serializerType = typeof(DictionarySerializer<,,,>);
                            return true;
                        }
                    }
                }
            }

            keyType = elementType = serializerType = null;
            return false;
        }

        static SerializationMap() {
            List<PropertySerializer<T>> list = new List<PropertySerializer<T>>();

            bool isModel = typeof(ModelObject).IsAssignableFrom(typeof(T));

            constructor = typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
            ParameterInfo[] constructorParameters = constructor.GetParameters();
            List<PropertyInfo> processedProperties = new List<PropertyInfo>();
            if (constructorParameters.Length > 0) {
                int firstReadOnlyArg = 0;
                if (isModel) {
                    parentType = constructorParameters[0].ParameterType;
                    if (!typeof(ModelObject).IsAssignableFrom(parentType)) {
                        throw new NotSupportedException("First parameter of constructor of type " + typeof(T) + " should be 'parent'");
                    }

                    firstReadOnlyArg = 1;
                }
                for (int i = firstReadOnlyArg; i < constructorParameters.Length; i++) {
                    ParameterInfo argument = constructorParameters[i];
                    if (!ValueSerializer.IsValueSerializerSupported(argument.ParameterType)) {
                        throw new NotSupportedException("Constructor of type " + typeof(T) + " parameter " + argument.Name + " should be value");
                    }

                    PropertyInfo property = typeof(T).GetProperty(argument.Name);
                    if (property == null) {
                        throw new NotSupportedException("Constructor of type " + typeof(T) + " parameter " + argument.Name + " should have matching property");
                    }

                    processedProperties.Add(property);
                    PropertySerializer<T> serializer = Activator.CreateInstance(typeof(ValuePropertySerializer<,>).MakeGenericType(typeof(T), argument.ParameterType), property) as PropertySerializer<T>;
                    list.Add(serializer);
                    constructorFieldMask |= 1ul << (i - firstReadOnlyArg);
                    if (!argument.IsOptional) {
                        requiredConstructorFieldMask |= 1ul << (i - firstReadOnlyArg);
                    }
                }
            }

            constructorProperties = list.Count;

            foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (property.GetCustomAttribute<SkipSerializationAttribute>() != null) {
                    continue;
                }

                if (processedProperties.Contains(property)) {
                    continue;
                }

                Type propertyType = property.PropertyType;
                Type serializerType = null, elementType = null, keyType = null;
                if (property.CanWrite && property.GetSetMethod() != null) {
                    serializerType = ValueSerializer.IsValueSerializerSupported(propertyType)
                        ? typeof(ValuePropertySerializer<,>)
                        : typeof(ModelObject).IsAssignableFrom(propertyType)
                        ? typeof(ReadWriteReferenceSerializer<,>)
                        : throw new NotSupportedException("Type " + typeof(T) + " has property " + property.Name + " that cannot be serialized");
                }
                else {
                    if (typeof(ModelObject).IsAssignableFrom(propertyType)) {
                        serializerType = typeof(ReadOnlyReferenceSerializer<,>);
                    }
                    else {
                        if (!propertyType.IsInterface || !GetInterfaceSerializer(propertyType, out serializerType, out keyType, out elementType)) {
                            foreach (Type iface in propertyType.GetInterfaces()) {
                                if (GetInterfaceSerializer(iface, out serializerType, out keyType, out elementType)) {
                                    break;
                                }
                            }
                        }
                    }
                }

                if (serializerType != null) {
                    Type[] typeArgs = elementType == null ? new[] { typeof(T), propertyType } : keyType == null ? new[] { typeof(T), propertyType, elementType } : new[] { typeof(T), propertyType, keyType, elementType };
                    list.Add(Activator.CreateInstance(serializerType.MakeGenericType(typeArgs), property) as PropertySerializer<T>);
                }
            }
            properties = list.ToArray();
        }

        public static void SerializeToJson(T value, Utf8JsonWriter writer) {
            if (value == null) {
                writer.WriteNullValue();
                return;
            }
            writer.WriteStartObject();
            foreach (PropertySerializer<T> property in properties) {
                if (property.type == PropertyType.Obsolete) {
                    continue;
                }

                writer.WritePropertyName(property.propertyName);
                property.SerializeToJson(value, writer);
            }
            writer.WriteEndObject();
        }

        private static PropertySerializer<T> FindProperty(ref Utf8JsonReader reader, ref int lastMatch) {
            if (reader.TokenType != JsonTokenType.PropertyName) {
                return null;
            }

            for (int i = lastMatch + 1; i < properties.Length; i++) {
                if (reader.ValueTextEquals(properties[i].propertyName.EncodedUtf8Bytes)) {
                    lastMatch = i;
                    return properties[i];
                }
            }

            for (int i = 0; i < lastMatch; i++) {
                if (reader.ValueTextEquals(properties[i].propertyName.EncodedUtf8Bytes)) {
                    lastMatch = i;
                    return properties[i];
                }
            }

            return null;
        }

        public static T DeserializeFromJson(ModelObject owner, ref Utf8JsonReader reader, DeserializationContext context) {
            if (reader.TokenType == JsonTokenType.Null) {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject) {
                throw new JsonException("Expected start object");
            }

            int depth = reader.CurrentDepth;
            try {
                T obj;
                if (parentType != null || constructorProperties > 0) {
                    if (parentType != null && !parentType.IsInstanceOfType(owner)) {
                        throw new NotSupportedException("Parent is of wrong type");
                    }

                    int firstReadOnlyArg = parentType == null ? 0 : 1;
                    object[] constructorArgs = new object[constructorProperties + firstReadOnlyArg];
                    constructorArgs[0] = owner;
                    if (constructorProperties > 0) {
                        Utf8JsonReader savedReaderState = reader;
                        int lastMatch = -1;
                        ulong constructorMissingFields = constructorFieldMask;
                        while (constructorMissingFields != 0 && reader.TokenType != JsonTokenType.EndObject) {
                            _ = reader.Read();
                            PropertySerializer<T> property = FindProperty(ref reader, ref lastMatch);
                            if (property != null && lastMatch < constructorProperties) {
                                _ = reader.Read();
                                constructorMissingFields &= ~(1ul << lastMatch);
                                constructorArgs[lastMatch + firstReadOnlyArg] = property.DeserializeFromJson(ref reader, context);
                            }
                            else {
                                reader.Skip();
                                _ = reader.Read();
                            }
                        }

                        if ((constructorMissingFields & requiredConstructorFieldMask) != 0) {
                            throw new JsonException("Json has missing constructor parameters");
                        }

                        reader = savedReaderState;
                    }

                    obj = constructor.Invoke(constructorArgs) as T;
                }
                else {
                    obj = Activator.CreateInstance<T>();
                }

                PopulateFromJson(obj, ref reader, context);
                return obj;
            }
            catch (Exception ex) {
                context.Exception(ex, "Unable to deserialize " + typeof(T).Name, ErrorSeverity.MajorDataLoss);
                if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == depth) {
                    _ = reader.Read();
                }

                while (reader.CurrentDepth > depth) {
                    _ = reader.Read();
                }

                return null;
            }
        }

        public static void PopulateFromJson(T obj, ref Utf8JsonReader reader, DeserializationContext allObjects) {
            if (allObjects != null && obj is ModelObject modelObject) {
                allObjects.Add(modelObject);
            }

            if (reader.TokenType != JsonTokenType.StartObject) {
                throw new JsonException("Expected start object");
            }

            int lastMatch = -1;
            _ = reader.Read();
            while (reader.TokenType != JsonTokenType.EndObject) {
                PropertySerializer<T> property = FindProperty(ref reader, ref lastMatch);
                if (property == null || lastMatch < constructorProperties) {
                    if (property == null) {
                        Console.Error.WriteLine("Json has extra property: " + reader.GetString());
                    }

                    reader.Skip();
                }
                else {
                    _ = reader.Read();
                    property.DeserializeFromJson(obj, ref reader, allObjects);
                }
                _ = reader.Read();
            }
        }
    }

    public class DeserializationContext {
        private readonly List<ModelObject> allObjects = new List<ModelObject>();
        private readonly ErrorCollector collector;

        public DeserializationContext(ErrorCollector errorCollector) {
            collector = errorCollector;
        }

        public void Add(ModelObject obj) {
            allObjects.Add(obj);
        }

        public void Notify() {
            foreach (ModelObject o in allObjects) {
                o.AfterDeserialize();
            }

            foreach (ModelObject o in allObjects) {
                o.ThisChanged(false);
            }
        }

        public void Error(string message, ErrorSeverity severity) {
            collector?.Error(message, severity);
        }

        public void Exception(Exception exception, string message, ErrorSeverity severity) {
            collector?.Exception(exception, message, severity);
        }
    }
}
