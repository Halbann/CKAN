using System;
using System.Collections.Generic;

using log4net;

using CKAN.IO;
using CKAN.ConsoleUI.Toolkit;

namespace CKAN.ConsoleUI {

    public partial class ModListScreen {

        private static readonly ILog urlLog = LogManager.GetLogger("URL");

        /// <summary>
        /// Run the screen with the ckan:// handlers subscribed.
        /// </summary>  
        public override void Run(Action? process = null)
        {
            ProtocolRouter.OnFocus += HandleProtocolFocus;
            ProtocolRouter.OnSearch += HandleProtocolSearch;
            ProtocolRouter.OnInstall += HandleProtocolInstall;

            try
            {
                base.Run(process);
            }
            finally
            {
                ProtocolRouter.OnFocus -= HandleProtocolFocus;
                ProtocolRouter.OnSearch -= HandleProtocolSearch;
                ProtocolRouter.OnInstall -= HandleProtocolInstall;
            }
        }

        private void HandleProtocolFocus(string identifier)
        {
            urlLog.DebugFormat("Focus requested: {0}", identifier);

            // The handlers fire on the pipe thread. ConsoleInput.Post runs the work on the UI thread.
            ConsoleInput.Post(() => {

                // Clear search filter that might hide the mod.
                SetSearch("");

                if (moduleList.SelectItem(m => string.Equals(m.identifier, identifier,
                                                             StringComparison.OrdinalIgnoreCase))) {
                    SetFocus(moduleList);
                }
            });
        }

        private void HandleProtocolSearch(string query)
        {
            urlLog.DebugFormat("Search requested: {0}", query);
            ConsoleInput.Post(() => SetSearch(query));
        }

        private void HandleProtocolInstall(List<(string Mod, string? Version)> mods)
        {
            urlLog.DebugFormat("Install requested: {0}", string.Join(", ", mods.ConvertAll(m => m.Mod)));

            ConsoleInput.Post(() => {
                if (manager.CurrentInstance == null) {
                    return;
                }

                bool installAny = false;

                foreach (var (modId, version) in mods) {
                    CkanModule? module = null;

                    // If version provided, search for the versioned mod.
                    if (version != null) {
                        module = registry.GetModuleByVersion(modId, version);
                        if (module == null) {
                            urlLog.WarnFormat("Version {0} of {1} not in registry. Using latest compatible.",
                                              version, modId);
                        }
                    }

                    // If still null, find latest.
                    module ??= registry.LatestAvailable(modId,
                                                        manager.CurrentInstance.StabilityToleranceConfig,
                                                        manager.CurrentInstance.VersionCriteria());

                    if (module == null) {
                        urlLog.WarnFormat("Mod not found in registry: {0}", modId);
                        continue;
                    }

                    // Add rather than toggle so that repeated links accumulate.
                    plan.Install.Add(module);
                    installAny = true;
                }

                if (installAny) {
                    ApplyChanges();
                }
            });
        }

        private void SetSearch(string text)
        {
            searchBox.Value = text;
            searchBox.Position = text.Length;

            // Assigning Value doesn't fire OnChange, so set the filter too.
            moduleList.FilterString = text;
        }
    }
}
