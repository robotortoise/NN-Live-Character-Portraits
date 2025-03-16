using System;
using System.Collections.Generic;

namespace Naninovel.E2E
{
    /// <summary>
    /// Configuration provider used by test suites;
    /// allows overriding specific configs during playback.
    /// </summary>
    public class Configurator : ProjectConfigurationProvider
    {
        private readonly Dictionary<Type, List<Action<Configuration>>> typeToConfigures
            = new();

        /// <summary>
        /// Registers a configuration override.
        /// </summary>
        /// <param name="type">Type of configuration to override.</param>
        /// <param name="configure">The override to perform when the config is requested during test.</param>
        public virtual void Register (Type type, Action<Configuration> configure)
        {
            GetConfigures(type).Add(configure);
        }

        public override Configuration GetConfiguration (Type type)
        {
            var config = base.GetConfiguration(type);
            foreach (var configure in GetConfigures(type))
                configure(config);
            return config;
        }

        private List<Action<Configuration>> GetConfigures (Type type)
        {
            return typeToConfigures.TryGetValue(type, out var configures)
                ? configures
                : typeToConfigures[type] = new();
        }
    }
}
