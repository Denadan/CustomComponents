﻿using System;
using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.UI;
using fastJSON;
using HBS.Extensions;


namespace CustomComponents
{
    /// <summary>
    /// component use category logic
    /// </summary>
    [CustomComponent("Category", true)]
    public class Category : SimpleCustomComponent, IAfterLoad, IOnInstalled, IReplaceValidateDrop,
        IPreValidateDrop, IPostValidateDrop, IReplaceIdentifier, IAdjustDescription, IOnItemGrabbed,
        IClearInventory, IAdjustValidateDrop
    {
        /// <summary>
        /// name of category
        /// </summary>
        public string CategoryID { get; set; }
        /// <summary>
        /// optional tag for AllowMixTags, if not set defid will used
        /// </summary>
        public string Tag { get; set; }

        public string ReplaceID => CategoryID;


        public string GetTag()
        {
            if (string.IsNullOrEmpty(Tag))
                return Def.Description.Id;
            else
                return Tag;
        }

        //       public bool Placeholder { get; set; } // if true, item is invalid

        [JsonIgnore]
        public CategoryDescriptor CategoryDescriptor { get; set; }

        public void OnLoaded(Dictionary<string, object> values)
        {
            CategoryDescriptor = CategoryController.Shared.GetOrCreateCategory(CategoryID);

            if (CategoryDescriptor.Defaults == null)
            {
                return;
            }

            Registry.ProcessCustomFactories(Def, CategoryDescriptor.Defaults, false);
        }

        public void OnInstalled(WorkOrderEntry_InstallComponent order, SimGameState state, MechDef mech)
        {
            Control.LogDebug(DType.ComponentInstall, $"- Category");
            if (order.PreviousLocation != ChassisLocations.None)
            {
                Control.LogDebug(DType.ComponentInstall, "-- removing");
                MechComponentRef def_replace = DefaultFixer.Shared.GetReplaceFor(mech, CategoryID, order.PreviousLocation, state);
                if (def_replace != null)
                {
                    Control.LogDebug(DType.ComponentInstall,$"--- added {def_replace.ComponentDefID}");
                    var inv = mech.Inventory.ToList();
                    inv.Add(def_replace);
                    mech.SetInventory(inv.ToArray());
                }
            }

            if (order.DesiredLocation == ChassisLocations.None)
                return;

            if (!CategoryDescriptor.AutoReplace || CategoryDescriptor.MaxEquiped < 0 && CategoryDescriptor.MaxEquipedPerLocation < 0)
            {
                Control.LogDebug(DType.ComponentInstall, $"-- {CategoryDescriptor.DisplayName} r:{CategoryDescriptor.AutoReplace}  max:{CategoryDescriptor.MaxEquiped} mpr:{CategoryDescriptor.MaxEquipedPerLocation} = not requre replace");

                return;
            }


            int n1 = mech.Inventory.Count(i => i.IsCategory(CategoryID));
            int n2 = mech.Inventory.Count(i => i.MountedLocation == order.DesiredLocation && i.IsCategory(CategoryID));

#if CCDEBUG
            if (Control.Settings.DebugInfo.HasFlag(DType.ComponentInstall))
            {
                Control.LogDebug(DType.ComponentInstall, "--- list:");
                foreach (var def in mech.Inventory
                    .Where(i => i.MountedLocation == order.DesiredLocation && i.IsCategory(CategoryID))
                    .Select(i => i.Def))
                {
                    Control.LogDebug(DType.ComponentInstall,
                        $"---- list: {def.Description.Id}: Default:{def.IsDefault()}");
                }
            }
#endif

            Control.LogDebug(DType.ComponentInstall, $"-- total {n1}/{CategoryDescriptor.MaxEquiped}  location: {n2}/{CategoryDescriptor.MaxEquipedPerLocation}");

            var replace = mech.Inventory.FirstOrDefault(i => !i.IsModuleFixed(mech) && (i.MountedLocation == order.DesiredLocation || CategoryDescriptor.ReplaceAnyLocation) && i.IsCategory(CategoryID) && i.IsDefault());

            Control.LogDebug(DType.ComponentInstall, $"-- possible replace: {(replace == null ? "not found" : replace.ComponentDefID)}");

            if (replace == null)
                return;

            bool need_replace = (CategoryDescriptor.MaxEquiped > 0 && n1 > CategoryDescriptor.MaxEquiped) ||
                (CategoryDescriptor.MaxEquipedPerLocation > 0 && n2 > CategoryDescriptor.MaxEquipedPerLocation);

            Control.LogDebug(DType.ComponentInstall, $"-- need_repalce: {need_replace}");

            if (need_replace)
                DefaultHelper.RemoveInventory(replace.ComponentDefID, mech, replace.MountedLocation, replace.ComponentDefType);

        }

        public string ReplaceValidateDrop(MechLabItemSlotElement drop_item, LocationHelper location, List<IChange> changes)
        {
            Control.LogDebug(DType.ComponentInstall, $"-- Category {CategoryID}");

            if (!CategoryDescriptor.AutoReplace ||
                CategoryDescriptor.MaxEquiped <= 0 && CategoryDescriptor.MaxEquipedPerLocation <= 0)
            {
                Control.LogDebug(DType.ComponentInstall, $"--- no replace needed");
                return String.Empty;
            }

            if (changes.OfType<RemoveChange>().Select(i => i.item.ComponentRef).Any(i => i.IsCategory(CategoryID)))
            {
                Control.LogDebug(DType.ComponentInstall, $"--- replace already found");
                return string.Empty;
            }


            var mech = location.mechLab.activeMechDef;

            if (CategoryDescriptor.MaxEquiped > 0)
            {
                var n = location.mechLab.activeMechDef.Inventory.Count(i => i.Def.IsCategory(CategoryID));
                Control.LogDebug(DType.ComponentInstall, $"--- MaxEquiped: {n}/{CategoryDescriptor.MaxEquiped}");

                if (n >= CategoryDescriptor.MaxEquiped)
                {
                    var replace = location.LocalInventory
                        .FirstOrDefault(i => i.ComponentRef.Def.IsCategory(CategoryID) && !i.ComponentRef.IsModuleFixed(mech));

                    if (CategoryDescriptor.ReplaceAnyLocation && replace == null)
                    {
                        var mechlab = new MechLabHelper(location.mechLab);
                        foreach (var widget in mechlab.GetWidgets())
                        {
                            if (widget.loadout.Location == location.widget.loadout.Location)
                                continue;

                            var loc_helper = new LocationHelper(widget);
                            replace = loc_helper.LocalInventory
                                .FirstOrDefault(i => i.ComponentRef.Def.IsCategory(CategoryID) && !i.ComponentRef.IsModuleFixed(mech));
                            if (replace != null)
                                break;
                        }
                    }
                    Control.LogDebug(DType.ComponentInstall, $"--- replace: {(replace == null ? "none" : replace.ComponentRef.ComponentDefID)}");

                    if (replace == null)
                    {
                        Control.LogDebug(DType.ComponentInstall, $"--- return error");

                        if (CategoryDescriptor.MaxEquiped > 1)
                            return string.Format(CategoryDescriptor.AddMaximumReached, CategoryDescriptor.displayName,
                                n);
                        else
                            return string.Format(CategoryDescriptor.AddAlreadyEquiped, CategoryDescriptor.displayName);
                    }
                    Control.LogDebug(DType.ComponentInstall, $"--- return replace");

                    changes.Add(new RemoveChange(replace.MountedLocation, replace));

                    return string.Empty;
                }
            }

            if (CategoryDescriptor.MaxEquipedPerLocation > 0)
            {
                var n = location.LocalInventory.Count(i => i.ComponentRef.Def.IsCategory(CategoryID));
                Control.LogDebug(DType.ComponentInstall, $"--- MaxEquipedPerLocation: {n}/{CategoryDescriptor.MaxEquipedPerLocation}");

                if (n >= CategoryDescriptor.MaxEquipedPerLocation)
                {
                    var replace = location.LocalInventory
                        .FirstOrDefault(i => i.ComponentRef.Def.IsCategory(CategoryID) && !i.ComponentRef.IsModuleFixed(mech));
                    Control.LogDebug(DType.ComponentInstall, $"--- replace: {(replace == null ? "none" : replace.ComponentRef.ComponentDefID)}");

                    if (replace == null)
                    {
                        Control.LogDebug(DType.ComponentInstall, $"--- return error");

                        if (CategoryDescriptor.MaxEquipedPerLocation > 1)
                            return string.Format(CategoryDescriptor.AddMaximumLocationReached,
                                CategoryDescriptor.displayName, n, location.LocationName);
                        else
                            return string.Format(CategoryDescriptor.AddAlreadyEquipedLocation,
                                CategoryDescriptor.displayName, location.LocationName);
                    }
                    Control.LogDebug(DType.ComponentInstall, $"--- return replace");
                    changes.Add(new RemoveChange(replace.MountedLocation, replace));
                }
            }
            return string.Empty;
        }

        public string PreValidateDrop(MechLabItemSlotElement item, LocationHelper location, MechLabHelper mechlab)
        {
            Control.LogDebug(DType.ComponentInstall, $"-- Category {CategoryID}");

            if (!CategoryDescriptor.AllowMixTags && mechlab.MechLab.activeMechDef.Inventory.Any(i => i.Def.IsCategory(CategoryID, out var c) && GetTag() != c.GetTag()))
                return string.Format(CategoryDescriptor.AddMixed, CategoryDescriptor.DisplayName);

            return string.Empty;
        }

        public string PostValidateDrop(MechLabItemSlotElement drop_item, MechDef mech, List<InvItem> new_inventory, List<IChange> changes)
        {
            Control.LogDebug(DType.ComponentInstall, $"-- Category {CategoryID}");

            if (CategoryDescriptor.MaxEquiped > 0)
            {
                var total = new_inventory.Count(i => i.item.Def.IsCategory(CategoryID));
                Control.LogDebug(DType.ComponentInstall, $"---- {total}/{CategoryDescriptor.MaxEquiped}");
                if (total > CategoryDescriptor.MaxEquiped)
                    if (CategoryDescriptor.MaxEquiped > 1)
                        return string.Format(CategoryDescriptor.AddMaximumReached, CategoryDescriptor.displayName, CategoryDescriptor.MaxEquiped);
                    else
                        return string.Format(CategoryDescriptor.AddAlreadyEquiped, CategoryDescriptor.displayName);
            }

            if (CategoryDescriptor.MaxEquipedPerLocation > 0)
            {
                var total = new_inventory
                    .Where(i => i.item.Def.IsCategory(CategoryID))
                    .Select(i =>
                    {
                        i.item.Def.IsCategory(CategoryID, out var component);
                        return new { l = i.location, c = component };
                    })
                    .GroupBy(i => i.l)
                    .FirstOrDefault(i => i.Count() > CategoryDescriptor.MaxEquipedPerLocation);

                if (total != null)
                    if (CategoryDescriptor.MaxEquipedPerLocation > 1)
                        return string.Format(CategoryDescriptor.AddMaximumLocationReached, CategoryDescriptor.DisplayName,
                           CategoryDescriptor.MaxEquipedPerLocation, total.Key);
                    else
                        return string.Format(CategoryDescriptor.AddAlreadyEquipedLocation,
                            CategoryDescriptor.DisplayName, total.Key);
            }

            return string.Empty;
        }

        public string AdjustDescription(string Description)
        {
            if (CategoryDescriptor.AddCategoryToDescription)
            {
                var color_start = $"<b><color={Control.Settings.CategoryDescriptionColor}>[";
                var color_end = "]</color></b>";
                if (Description.Contains(color_start))
                {
                    int pos = Description.IndexOf(color_start, 0) + color_start.Length;
                    Description = Description.Substring(0, pos) + CategoryDescriptor.DisplayName + ", " +
                                  Description.Substring(pos);
                }
                else
                    Description = Description + "\n" + color_start + CategoryDescriptor.DisplayName + color_end;
            }

            return Description;
        }


        public override string ToString()
        {
            return "Category: " + CategoryID;
        }

        public void ClearInventory(MechDef mech, List<MechComponentRef> result, SimGameState state, MechComponentRef source)
        {
            var item = DefaultFixer.Shared.GetReplaceFor(mech, CategoryID, source.MountedLocation, state);
            if(item != null)
                result.Add(item);
        }

        public void OnItemGrabbed(IMechLabDraggableItem item, MechLabPanel mechLab, MechLabLocationWidget widget)
        {
            Control.LogDebug(DType.ComponentInstall, $"- Category {CategoryID}");
            Control.LogDebug(DType.ComponentInstall, $"-- search replace for {item.ComponentRef.ComponentDefID}");

            var replace = DefaultFixer.Shared.GetReplaceFor(mechLab.activeMechDef, CategoryID, widget.loadout.Location, mechLab.sim);

            if (replace == null)
            {
                Control.LogDebug(DType.ComponentInstall, $"-- no replacement, skipping");
                return;
            }

            DefaultHelper.AddMechLab(replace, new MechLabHelper(mechLab));
            Control.LogDebug(DType.ComponentInstall, $"-- added {replace.ComponentDefID} to {replace.MountedLocation}");
            mechLab.ValidateLoadout(false);
        }

        public IEnumerable<IChange> ValidateDropOnAdd(MechLabItemSlotElement item, LocationHelper location, MechLabHelper mechlab, List<IChange> changes)
        {
            yield break;
        }

        public IEnumerable<IChange> ValidateDropOnRemove(MechLabItemSlotElement item, LocationHelper location, MechLabHelper mechlab, List<IChange> changes)
        {
            var replace = DefaultFixer.Shared.GetReplaceFor(mechlab.MechLab.activeMechDef, CategoryID, item.MountedLocation, mechlab.MechLab.sim);
            if (replace == null)
            {
                Control.LogDebug(DType.ComponentInstall, $"--- Category {CategoryID} - no replace, return");
                yield break;
            }

            foreach (var addChange in changes.OfType<AddChange>())
            {
                if (addChange.item.ComponentRef.IsCategory(CategoryID))
                {
                    Control.LogDebug(DType.ComponentInstall, $"--- Category {CategoryID} - replace already added");
                    yield break;
                }
            }

            Control.LogDebug(DType.ComponentInstall, $"--- Category {CategoryID} - add replace {replace.ComponentDefID}");

            yield return new AddDefaultChange(replace.MountedLocation, DefaultHelper.CreateSlot(replace, mechlab.MechLab));
        }
    }
}