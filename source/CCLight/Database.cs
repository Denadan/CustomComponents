﻿using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Data;
using Harmony;

namespace CustomComponents
{
    internal class Database
    {
        #region internal

        internal static bool SetCustomWithIdentifier(string identifier, ICustom cc, bool replace)
        {
            return Shared.SetCustomInternal(identifier, cc, replace);
        }

        internal static IEnumerable<T> GetCustomsFromIdentifier<T>(string identifier)
        {
            if (identifier == null)
            {
                return Enumerable.Empty<T>();
            }
            return Shared.GetCustomsInternal<T>(identifier);
        }

        internal static IEnumerable<T> GetCustoms<T>(object target)
        {
            var identifier = Identifier(target);
            return GetCustomsFromIdentifier<T>(identifier);
        }

        internal static T GetCustom<T>(object target)
        {
            return GetCustoms<T>(target).FirstOrDefault();
        }

        internal static bool Is<T>(object target, out T value)
        {
            value = GetCustom<T>(target);
            return value != null;
        }

        internal static bool Is<T>(object target)
        {
            return GetCustom<T>(target) != null;
        }

        internal static string Identifier(object target)
        {
            if (target == null)
            {
                Control.LogError("Error - requested identifier for null!");
                return string.Empty;
            }

            // this is for faster access
            if (target is MechComponentDef mechComponentDef)
            {
                return mechComponentDef.Description.Id;
            }

            var descriptionProperty = target.GetType().GetProperty(nameof(MechComponentDef.Description), typeof(DescriptionDef));
            var description = descriptionProperty?.GetValue(target, null) as DescriptionDef;
            return description?.Id;
        }

        #endregion

        #region private

        private static readonly Database Shared = new Database();

        private readonly Dictionary<string, List<ICustom>> customs = new Dictionary<string, List<ICustom>>();

        private IEnumerable<T> GetCustomsInternal<T>(string key)
        {
            if (!customs.TryGetValue(key, out var ccs))
            {
                return Enumerable.Empty<T>();
            }

            return ccs.OfType<T>();
        }

        private bool SetCustomInternal(string key, ICustom cc, bool replace)
        {
            Control.LogDebug(DType.CCLoading, $"SetCustomInternal key={key} cc={cc}");

            if (!customs.TryGetValue(key, out var ccs))
            {
                ccs = new List<ICustom>();
                customs[key] = ccs;
            }

            var attribute = Registry.GetAttributeByType(cc.GetType());

            for (int i = 0; i < ccs.Count; i++)
            {
                var custom = ccs[i];
                var attribute2 = Registry.GetAttributeByType(custom.GetType());

                bool same_type = string.IsNullOrEmpty(attribute.Group)
                    ? attribute.Name == attribute2.Name
                    : attribute.Group == attribute2.Group;

                if (!same_type)
                    continue;

                if (attribute.AllowArray)
                {
                    if (!(cc is IReplaceIdentifier cci1))
                        break;

                    if (custom is IReplaceIdentifier cci2 &&
                        cci1.ReplaceID == cci2.ReplaceID)
                    {
                        Control.LogDebug(DType.CCLoading, $"--find replace: add:{attribute.Name} fnd:{attribute2.Name} grp:{attribute.Group} rid:{cci1.ReplaceID} Replace: {replace}");
                        Control.LogDebug(DType.CCLoading, $"--replace: from:{custom} to:{cc}");
                        if (replace)
                        {
                            ccs[i] = cc;
                            return true;
                        }

                        return false;
                    }
                }
                else
                {
                    Control.LogDebug(DType.CCLoading, $"--find replace: add:{attribute.Name} fnd:{attribute2.Name} grp:{attribute.Group} Replace: {replace}");
                    Control.LogDebug(DType.CCLoading, $"--replace: from:{custom} to:{cc}");
                    if (replace)
                    {
                        ccs[i] = cc;
                        return true;
                    }
                    return false;
                }
            }
            Control.LogDebug(DType.CCLoading, $"--added");
            ccs.Add(cc);
            return true;
        }

        private void Clear()
        {
            customs.Clear();
        }

        #endregion

        #region embedded

        [HarmonyPatch(typeof(DataManager), nameof(DataManager.Clear))]
        public static class DataManager_Clear_Patch
        {
            public static void Prefix(bool defs)
            {
                if (defs)
                {
                    Shared.Clear();
                }
            }
        }

        #endregion
    }
}