using System;
using System.Collections.Generic;
using System.Linq;

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
            ProtocolRouter.OnError += HandleProtocolError;

            ProtocolRouter.HandlePendingLaunchUrl();

            try {
                base.RunScreen(process);
            }
            finally {
                ProtocolRouter.OnFocus -= HandleProtocolFocus;
                ProtocolRouter.OnSearch -= HandleProtocolSearch;
                ProtocolRouter.OnInstall -= HandleProtocolInstall;
                ProtocolRouter.OnError -= HandleProtocolError;
            }
        }

        private void HandleProtocolError(UrlError error)
        {
            urlLog.DebugFormat("Cannot handle URL: {0}", error);
            PostToModList(() => RaiseError("{0}", ProtocolRouter.ErrorMessage(error)));
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
                searchBox.Clear();

                if (moduleList.SelectItem(m => string.Equals(m.identifier, identifier,
                                                             StringComparison.OrdinalIgnoreCase))) {
                    SetFocus(moduleList);
                }
            });
        }

        private void HandleProtocolSearch(string query)
        {
            urlLog.DebugFormat("Search requested: {0}", query);
            PostToModList(() => searchBox.SetValue(query));
        }

        private void HandleProtocolInstall(List<(string Mod, string? Version)> mods)
        {
            urlLog.DebugFormat("Install requested: {0}", string.Join(", ", mods.Select(m => m.Mod)));

            PostToModList(() => {
                if (manager.CurrentInstance == null) {
                    urlLog.Debug("Instance not loaded. Cannot mark anything for install");
                    return;
                }

                var inst = manager.CurrentInstance;
                var crit = inst.VersionCriteria();

                var resolved = InstallResolver.Resolve(mods, registry, inst.StabilityToleranceConfig, crit);
                var confirmed = InstallUrlPrompt.Confirm(resolved, this, inst.Game.ShortName,
                                                         crit.ToSummaryString(inst.Game));

                foreach (var module in confirmed.Install) {
                    plan.Install.Add(module); // Repeated links accumulate.
                }

                foreach (var module in confirmed.Reinstall) {
                    // Console UI doesn't have reinstall in the same way GUI does. Seems to work but might not be ideal?
                    plan.Remove.Add(module.identifier);
                    plan.Install.Add(module);
                }

                if (confirmed.Any) {
                    ApplyChanges();
                }
            });
        }
    }
}
