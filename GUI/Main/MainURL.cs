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

            Util.Invoke(this, () =>
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

            Util.Invoke(this, () =>
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

            Util.Invoke(this, () =>
            {
                if (CurrentInstance != null)
                {
                    var reg = RegistryManager.Instance(CurrentInstance, repoData).registry;
                    var marked = false;

                    // Freeze so the changeset recomputes once at the end instead of once per mod.
                    ManageMods.WithFrozenChangeset(() =>
                    {
                        foreach (var (modId, version) in mods)
                        {
                            // Resolve the identifier and optional pinned version to a module in the registry.
                            CkanModule? module = null;
                            if (version != null)
                            {
                                module = reg.GetModuleByVersion(modId, version);
                                if (module == null)
                                {
                                    urlLog.WarnFormat("Version {0} of {1} not in registry. Falling back to latest compatible",
                                                      version, modId);
                                }
                            }

                            // If still null, get latest.
                            module ??= reg.LatestAvailable(modId,
                                                           CurrentInstance.StabilityToleranceConfig,
                                                           CurrentInstance.VersionCriteria());
                            if (module == null)
                            {
                                urlLog.WarnFormat("Mod not found in registry: {0}", modId);
                                continue;
                            }

                            // Setting SelectedMod is the same thing as when the user ticks an install checkbox.
                            // Therefore URL clicks accumulate with anything already marked and the user can remove mods normally.
                            if (ManageMods.MainModList?.full_list_of_mod_rows is { } rows
                                && rows.TryGetValue(modId, out var row)
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
                }
            });
        }
    }
}
