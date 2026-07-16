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

        // Debounce so spamming links triggers one action instead of one per click.
        // install clicks accumulate their mods while the timer runs and apply as one batch.
        private const int UrlDebounceMs = 250;
        private Timer? focusDebounce;
        private Timer? searchDebounce;
        private Timer? installDebounce;
        private string? pendingFocusIdent;
        private string? pendingSearchQuery;
        private List<(string Mod, string? Version)>? pendingInstalls;

        private void WireProtocolRouter()
        {
            focusDebounce = new Timer { Interval = UrlDebounceMs };
            focusDebounce.Tick += FocusDebounce_Tick;
            searchDebounce = new Timer { Interval = UrlDebounceMs };
            searchDebounce.Tick += SearchDebounce_Tick;
            installDebounce = new Timer { Interval = UrlDebounceMs };
            installDebounce.Tick += InstallDebounce_Tick;

            ProtocolRouter.OnFocus += HandleProtocolFocus;
            ProtocolRouter.OnSearch += HandleProtocolSearch;
            ProtocolRouter.OnInstall += HandleProtocolInstall;
        }

        private void UnwireProtocolRouter()
        {
            ProtocolRouter.OnFocus -= HandleProtocolFocus;
            ProtocolRouter.OnSearch -= HandleProtocolSearch;
            ProtocolRouter.OnInstall -= HandleProtocolInstall;

            focusDebounce?.Dispose();
            focusDebounce = null;
            searchDebounce?.Dispose();
            searchDebounce = null;
            installDebounce?.Dispose();
            installDebounce = null;
        }

        // Bring CKAN to the foreground after handling a ckan:// URL.
        // Windows demotes this to a taskbar flash rather than allow a focus steal but whatever.
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
                pendingFocusIdent = identifier;
                focusDebounce?.Stop();
                focusDebounce?.Start();
            });
        }

        private void FocusDebounce_Tick(object? sender, EventArgs e)
        {
            focusDebounce?.Stop();
            if (pendingFocusIdent is string ident)
            {
                pendingFocusIdent = null;

                // Clear any search that might be hiding the mod.
                ManageMods.SetSearches(new List<ModSearch>());

                ManageMods.FocusMod(ident, true, true);
                RaiseToForeground();
            }
        }

        private void HandleProtocolSearch(string query)
        {
            urlLog.DebugFormat("Search requested: {0}", query);

            Util.Invoke(this, () =>
            {
                pendingSearchQuery = query;
                searchDebounce?.Stop();
                searchDebounce?.Start();
            });
        }

        private void SearchDebounce_Tick(object? sender, EventArgs e)
        {
            searchDebounce?.Stop();
            if (pendingSearchQuery is string query && CurrentInstance != null)
            {
                pendingSearchQuery = null;
                var search = ModSearch.Parse(ModuleLabelList.ModuleLabels, CurrentInstance, query);
                if (search != null)
                {
                    ManageMods.SetSearches(new List<ModSearch> { search });
                }

                RaiseToForeground();
            }
        }

        private void HandleProtocolInstall(List<(string Mod, string? Version)> mods)
        {
            urlLog.DebugFormat("Install requested: {0}", string.Join(", ", mods.Select(m => m.Mod)));

            Util.Invoke(this, () =>
            {
                pendingInstalls ??= new List<(string Mod, string? Version)>();
                pendingInstalls.AddRange(mods);
                installDebounce?.Stop();
                installDebounce?.Start();
            });
        }

        private void InstallDebounce_Tick(object? sender, EventArgs e)
        {
            installDebounce?.Stop();
            if (pendingInstalls is { Count: > 0 } mods && CurrentInstance != null)
            {
                pendingInstalls = null;

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
                        if ((ManageMods.MainModList?.full_list_of_mod_rows.TryGetValue(modId, out var row) ?? false)
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
        }
    }
}
