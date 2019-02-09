﻿using System;
using System.Collections.Generic;
using System.Linq;
using BattleTech;

namespace CustomComponents
{
    public delegate void AutoFixerDelegate(List<MechDef> mechDefs, SimGameState simgame);


    public class AutoFixer
    {
        public static AutoFixer Shared = new AutoFixer();

        private List<AutoFixerDelegate> fixers = new List<AutoFixerDelegate>();
        private List<AutoFixerDelegate> savegamefixers = new List<AutoFixerDelegate>();

        internal void FixMechDef(List<MechDef> mechDefs)
        {
            if (!Control.Settings.RunAutofixer)
            {
                return;
            }

            foreach (var autoFixerDelegate in fixers)
            {
                try
                {
                    autoFixerDelegate(mechDefs, null);
                }
                catch (Exception e)
                {
                    Control.Logger.LogError($"Exception in Autofixer {autoFixerDelegate.Method.Name}", e);
                }
            }
        }

        internal void FixSavedMech(List<MechDef> mechDefs, SimGameState state)
        {
            if (!Control.Settings.RunAutofixer || !Control.Settings.FixSaveGameMech)
            {
                return;
            }

            foreach (var autoFixerDelegate in savegamefixers)
            {
                try
                {
                    autoFixerDelegate(mechDefs, state);
                }
                catch (Exception e)
                {
                    Control.Logger.LogError($"Exception in Autofixer {autoFixerDelegate.Method.Name}", e);
                }
            }
        }

        public void RegisterMechFixer(AutoFixerDelegate fixer)
        {
            fixers.Add(fixer);
            savegamefixers.Add(fixer);
        }

        public void RegisterSaveMechFixer(AutoFixerDelegate fixer)
        {
            savegamefixers.Add(fixer);
        }

        internal void RemoveEmptyRefs(List<MechDef> mechDefs, SimGameState state)
        {
            foreach (var mechDef in mechDefs)
            {
                if (mechDef.Inventory.All(i => i?.Def != null))
                {
                    continue;
                }
            
                Control.Logger.LogError($"Found NULL in {mechDef.Name}({mechDef.Description.Id})");

                foreach (var r in mechDef.Inventory)
                {
                    if (r.Def == null)
                        Control.Logger.LogError($"--- NULL --- {r.ComponentDefID}");
                }

                mechDef.SetInventory(mechDef.Inventory.Where(i => i.Def != null).ToArray());
            }
        }


        internal void ReAddFixed(List<MechDef> mechDefs, SimGameState state)
        {
            foreach (var mechDef in mechDefs)
            {
                mechDef.SetInventory(mechDef.Inventory.Where(i => !i.IsModuleFixed(mechDef)).ToArray());
                mechDef.Refresh();
            }
        }
    }
}