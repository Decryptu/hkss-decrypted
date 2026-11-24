using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Modding
{
    public static class ReflectionHelper
    {
        private const BindingFlags AllFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static readonly ConcurrentDictionary<(Type, string), FieldInfo> FieldCache = new();

        private static FieldInfo ResolveField(Type type, string name)
        {
            return FieldCache.GetOrAdd((type, name), static key =>
            {
                for (Type? cur = key.Item1; cur != null; cur = cur.BaseType)
                {
                    FieldInfo? fi = cur.GetField(key.Item2, AllFlags);
                    if (fi != null) return fi;
                }
                throw new MissingFieldException(key.Item1.FullName, key.Item2);
            });
        }

        public static TField GetField<TObject, TField>(TObject obj, string name)
        {
            return (TField)ResolveField(typeof(TObject), name).GetValue(obj);
        }

        public static TField GetField<TObject, TField>(string name)
        {
            return (TField)ResolveField(typeof(TObject), name).GetValue(null);
        }

        public static void SetField<TObject, TField>(TObject obj, string name, TField value)
        {
            ResolveField(typeof(TObject), name).SetValue(obj, value);
        }

        public static void SetField<TObject, TField>(string name, TField value)
        {
            ResolveField(typeof(TObject), name).SetValue(null, value);
        }
    }
}
