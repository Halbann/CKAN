using System;
using System.Collections.Generic;

using log4net;

using CKAN.IO;
using CKAN.ConsoleUI.Toolkit;

namespace CKAN.ConsoleUI {

    public partial class ModListScreen {

        // Log Debug rather than Warn throughout because Warn prints to the console and breaks rendering.
        private static readonly ILog urlLog = LogManager.GetLogger("URL");

        /// <summary>
        /// Run the screen with the ckan:// handlers subscribed.
        /// </summary>
        protected override void RunScreen(Action? process)
        {
            ProtocolRouter.OnFocus += HandleProtocolFocus;
            ProtocolRouter.OnSearch += HandleProtocolSearch;
            ProtocolRouter.OnInstall += HandleProtocolInstall;

            ProtocolRouter.HandlePendingLaunchUrl();

            try {
                base.RunScreen(process);
            }
            finally {
                ProtocolRouter.OnFocus -= HandleProtocolFocus;
                ProtocolRouter.OnSearch -= HandleProtocolSearch;
                ProtocolRouter.OnInstall -= HandleProtocolInstall;
            }
        }

        private void PostToModList(Action action)
        {
            if (Current == this) {
                ConsoleInput.Post(action);
            } else {
                urlLog.Debug("Ignoring URL because another screen is running");
            }
        }

        private void HandleProtocolFocus(string identifier)
        {
            urlLog.DebugFormat("Focus requested: {0}", identifier);

            // Currently on the pipe thread. Need to invoke on the UI thread.
            PostToModList(() => {

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
            PostToModList(() => SetSearch(query));
        }

        private void HandleProtocolInstall(List<(string Mod, string? Version)> mods)
        {
            urlLog.DebugFormat("Install requested: {0}", string.Join(", ", mods.ConvertAll(m => m.Mod)));

            PostToModList(() => {
                if (manager.CurrentInstance == null) {
                    return;
                }

                bool installAny = false;

                foreach (var (modId, version) in mods) {
                    var ident = allMods == null
                                    ? modId
                                    : ProtocolRouter.CanonicalIdentifier(modId, allMods.ConvertAll(m => m.identifier))
                                      ?? modId;

                    // The id and version came from a URL, so treat any failure as not found.
                    CkanModule? module = null;
                    try {
                        // Try to get versioned module.
                        if (version != null) {
                            module = registry.GetModuleByVersion(ident, version);
                            if (module == null) {
                                urlLog.DebugFormat("Version {0} of {1} not in registry. Using latest compatible.",
                                                   version, modId);
                            }
                        }

                        // Default to latest.
                        module ??= registry.LatestAvailable(ident,
                                                            manager.CurrentInstance.StabilityToleranceConfig,
                                                            manager.CurrentInstance.VersionCriteria());
                    } catch (Exception ex) {
                        urlLog.DebugFormat("Could not resolve {0}: {1}", modId, ex.Message);
                        continue;
                    }

                    if (module == null) {
                        urlLog.DebugFormat("Mod not found in registry: {0}", modId);
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
