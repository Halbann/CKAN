using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

using log4net;

using CKAN.IO;

namespace CKAN.GUI
{
    #if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
    #endif
    public partial class Main
    {
        private static readonly ILog urlLog = LogManager.GetLogger("URL");

        private void WireProtocolRouter()
        {
            ProtocolRouter.OnFocus += HandleProtocolFocus;
            ProtocolRouter.OnSearch += HandleProtocolSearch;
            ProtocolRouter.OnInstall += HandleProtocolInstall;
        }

        private void UnwireProtocolRouter()
        {
            ProtocolRouter.OnFocus -= HandleProtocolFocus;
            ProtocolRouter.OnSearch -= HandleProtocolSearch;
            ProtocolRouter.OnInstall -= HandleProtocolInstall;
        }

        private static bool ModalDialogOpen()
            => Application.OpenForms.OfType<Form>().Any(f => f.Modal);

        private void InvokeIfReady(Action action)
        {
            Util.Invoke(this, () =>
            {
                if (Waiting || ManageMods.MainModList == null || tabController.TabLocked || ModalDialogOpen())
                {
                    urlLog.Warn("Ignoring URL because CKAN is busy");
                    RaiseToForeground();
                }
                else
                {
                    action();
                }
            });
        }

        // Bring CKAN to the foreground after handling a ckan:// URL.
        // The url handler stub grants us its foreground rights first. Without them Windows demotes this to a taskbar flash.
        private void RaiseToForeground()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
        }

        private void HandleProtocolFocus(string identifier)
        {
            urlLog.DebugFormat("Focus requested: {0}", identifier);

            InvokeIfReady(() =>
            {
                // Clear any search that might be hiding the mod.
                ManageMods.SetSearches(new List<ModSearch>());

                ManageMods.FocusMod(identifier, true, true);
                RaiseToForeground();
            });
        }

        private void HandleProtocolSearch(string query)
        {
            urlLog.DebugFormat("Search requested: {0}", query);

            InvokeIfReady(() =>
            {
                if (CurrentInstance != null)
                {
                    var search = ModSearch.Parse(ModuleLabelList.ModuleLabels, CurrentInstance, query);
                    if (search != null)
                    {
                        ManageMods.SetSearches(new List<ModSearch> { search });
                    }

                    RaiseToForeground();
                }
            });
        }

        private void HandleProtocolInstall(List<(string Mod, string? Version)> mods)
        {
            urlLog.DebugFormat("Install requested: {0}", string.Join(", ", mods.Select(m => m.Mod)));

            InvokeIfReady(() =>
            {
                if (CurrentInstance == null)
                {
                    return;
                }

                var rows = ManageMods.MainModList?.full_list_of_mod_rows;
                if (rows == null)
                {
                    urlLog.Warn("Mod list not loaded. Cannot mark anything for install");
                    return;
                }

                var reg = RegistryManager.Instance(CurrentInstance, repoData).registry;
                var marked = false;

                // Freeze so the changeset recomputes once at the end instead of once per mod.
                ManageMods.WithFrozenChangeset(() =>
                {
                    foreach (var (modId, version) in mods)
                    {
                        // Resolving against row keys rather than the registry matches the row lookup further down.
                        var ident = ProtocolRouter.CanonicalIdentifier(modId, rows.Keys) ?? modId;

                        // The id and version came from a URL, so treat any failure as not found.
                        CkanModule? module = null;
                        try
                        {
                            // Try to get versioned module.
                            if (version != null)
                            {
                                module = reg.GetModuleByVersion(ident, version);
                                if (module == null)
                                {
                                    urlLog.WarnFormat("Version {0} of {1} not in registry. Falling back to latest compatible",
                                                      version, modId);
                                }
                            }

                            // Default to latest.
                            module ??= reg.LatestAvailable(ident,
                                                           CurrentInstance.StabilityToleranceConfig,
                                                           CurrentInstance.VersionCriteria());
                        }
                        catch (Exception ex)
                        {
                            urlLog.WarnFormat("Could not resolve {0}: {1}", modId, ex.Message);
                            continue;
                        }

                        if (module == null)
                        {
                            urlLog.WarnFormat("Mod not found in registry: {0}", modId);
                            continue;
                        }

                        // Setting SelectedMod is the same thing as when the user ticks an install checkbox.
                        // Therefore URL clicks accumulate with anything already marked and the user can remove mods normally.
                        if (rows.TryGetValue(ident, out var row)
                            && row.Tag is GUIMod gmod)
                        {
                            gmod.SelectedMod = module;
                            marked = true;
                        }
                        else
                        {
                            urlLog.WarnFormat("No mod list row for {0}. Cannot mark for install", modId);
                        }
                    }
                });

                // Take user to changeset screen.
                if (marked)
                {
                    tabController.ShowTab(ChangesetTabPage.Name, 1);
                }

                RaiseToForeground();
            });
        }
    }
}