using System.Collections.Generic;
using System.Linq;

using CKAN.Configuration;
using CKAN.Versioning;

namespace CKAN.IO
{
    // How a mod from an install URL resolved against the registry. Each UI presents these the same way.
    // Module is the mod to act on. It's null only for Unknown.
    public enum InstallOutcome
    {
        Ready,
        AlreadyInstalled,
        // The pinned version isn't in the registry. Module is the latest instead.
        PinMissed,
        // Nothing for this mod is compatible. The user has to confirm before we install it.
        Incompatible,
        Unknown,
    }

    public static class InstallResolver
    {
        // Resolve the mods from an install URL in a single pass over the registry.
        // Matching is case insensitive so a URL written by hand needn't get the capitalisation right.
        // The spec requires identifiers to be unique regardless of case, so this can't be ambiguous.
        public static List<(string Query, CkanModule? Module, InstallOutcome Outcome)> Resolve(
            IEnumerable<(string Mod, string? Version)> mods,
            IRegistryQuerier registry,
            StabilityToleranceConfig stability,
            GameVersionCriteria crit)
        {
            var pending = new Dictionary<string, (string Query, string? Version)>();
            foreach (var (mod, version) in mods)
            {
                pending[mod.ToLowerInvariant()] = (mod, version);
            }

            // Which stream a mod arrives in is its compatibility.
            var compatible = registry.CompatibleModules(stability, crit)
                                     .Select(m => (Module: m, Compatible: true));
            var incompatible = registry.IncompatibleModules(stability, crit)
                                       .Select(m => (Module: m, Compatible: false));

            // Stop the moment every wanted mod is accounted for.
            var found = new Dictionary<string, (string Query, CkanModule? Module, InstallOutcome Outcome)>();
            foreach (var (module, isCompatible) in compatible.Concat(incompatible))
            {
                if (pending.Count == 0)
                {
                    break;
                }

                var key = module.identifier.ToLowerInvariant();
                if (pending.TryGetValue(key, out var want))
                {
                    pending.Remove(key);
                    found[key] = Classify(want.Query, module, isCompatible, want.Version, registry);
                }
            }

            // Anything left never appeared in the registry.
            foreach (var (key, want) in pending)
            {
                found[key] = (want.Query, null, InstallOutcome.Unknown);
            }

            // One result per mod, in the order the URL listed them.
            return mods.Select(m => m.Mod.ToLowerInvariant())
                       .Distinct()
                       .Select(key => found[key])
                       .ToList();
        }

        private static (string Query, CkanModule? Module, InstallOutcome Outcome) Classify(
            string query, CkanModule available, bool compatible, string? version, IRegistryQuerier registry)
        {
            var pinned = version == null ? null
                                         : registry.GetModuleByVersion(available.identifier, version);
            var module = pinned ?? available;

            if (!compatible)
            {
                return (query, module, InstallOutcome.Incompatible);
            }

            if (version != null && pinned == null)
            {
                return (query, module, InstallOutcome.PinMissed);
            }

            return module.Equals(registry.GetInstalledVersion(module.identifier))
                ? (query, module, InstallOutcome.AlreadyInstalled)
                : (query, module, InstallOutcome.Ready);
        }
    }
}
