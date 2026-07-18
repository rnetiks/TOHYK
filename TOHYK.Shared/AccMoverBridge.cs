#if KKS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;

namespace TOHYK
{
    internal static class AccMoverBridge
    {
        private const string AccMoverGuid = "starstorm.accmover";

        private enum Strategy { Undetected, PublicMethod, InternalField, Unsupported }

        private static Strategy _strategy = Strategy.Undetected;
        private static MethodInfo _selectedAccsMethod;
        private static FieldInfo _selectedTransformField;
        private static bool _broken;

        private static bool IsLoaded => Chainloader.PluginInfos.ContainsKey(AccMoverGuid);

        public static bool IsUsable => !_broken && IsLoaded && Detect() != Strategy.Unsupported;

        public static bool TryGetSelectedSlots(List<int> results)
        {
            results.Clear();

            if (!IsLoaded || _broken)
                return false;

            var strategy = Detect();
            if (strategy == Strategy.Unsupported)
                return false;

            try
            {
                ReadSelectedSlots(strategy, results);
            }
            catch (Exception ex)
            {
                _broken = true;
                TOHYK.Log?.LogWarning(
                    "[TOHYK] AccMover integration failed (likely an incompatible AccMover version) - " +
                    "falling back to single-accessory mode for the rest of this session. " + ex.Message);
                results.Clear();
                return false;
            }

            return results.Count > 0;
        }

        private static Strategy Detect()
        {
            if (_strategy != Strategy.Undetected)
                return _strategy;

            try
            {
                var accMoverType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => SafeGetType(a, "AccMover.AccMover"))
                    .FirstOrDefault(t => t != null);

                if (accMoverType == null)
                {
                    _strategy = Strategy.Unsupported;
                    return _strategy;
                }

                var method = accMoverType.GetMethod("SelectedAccs",
                    BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method != null && typeof(IEnumerable).IsAssignableFrom(method.ReturnType))
                {
                    _selectedAccsMethod = method;
                    _strategy = Strategy.PublicMethod;
                    return _strategy;
                }

                var field = accMoverType.GetField("selectedTransform",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (field != null && typeof(IEnumerable).IsAssignableFrom(field.FieldType))
                {
                    _selectedTransformField = field;
                    _strategy = Strategy.InternalField;
                    return _strategy;
                }

                _strategy = Strategy.Unsupported;
            }
            catch (Exception)
            {
                _strategy = Strategy.Unsupported;
            }

            return _strategy;
        }

        private static Type SafeGetType(Assembly assembly, string fullName)
        {
            try { return assembly.GetType(fullName); }
            catch (ReflectionTypeLoadException) { return null; }
        }

        private static void ReadSelectedSlots(Strategy strategy, List<int> results)
        {
            IEnumerable raw = strategy == Strategy.PublicMethod
                ? (IEnumerable)_selectedAccsMethod.Invoke(null, null)
                : (IEnumerable)_selectedTransformField.GetValue(null);

            foreach (var item in raw)
            {
                if (item is int slot)
                {
                    results.Add(slot);
                    continue;
                }

                var keyProp = item.GetType().GetProperty("Key");
                if (keyProp?.GetValue(item) is int keyed)
                    results.Add(keyed);
            }
        }
    }
}
#endif