using System;
using System.Collections.Generic;

namespace Naninovel.E2E
{
    /// <summary>
    /// End to end test suite.
    /// </summary>
    public class E2E
    {
        private readonly List<ServiceInitializationData> services = new();
        private readonly Configurator configurator = new();
        private readonly List<Func<UniTask>> with = new();
        private readonly Options options;

        public E2E (Options options = null)
        {
            this.options = options ?? new Options();
        }

        /// <summary>
        /// Makes engine use specified configuration during test playback.
        /// </summary>
        public E2E WithConfig<TConfig> (Action<TConfig> configure)
            where TConfig : Configuration
        {
            configurator.Register(typeof(TConfig), c => configure((TConfig)c));
            return this;
        }

        /// <summary>
        /// Overrides <see cref="TSource"/> engine service with <see cref="TTarget"/> during test playback.
        /// </summary>
        /// <typeparam name="TSource">Service to override.</typeparam>
        /// <typeparam name="TTarget">Service to use instead of the overridden.</typeparam>
        /// <param name="priority">Initialization priority of the service (lower = earlier).</param>
        public E2E WithService<TSource, TTarget> (int priority = 0)
            where TSource : IEngineService
            where TTarget : IEngineService
        {
            services.Add(new(typeof(TTarget),
                new(priority, typeof(TSource))));
            return this;
        }

        /// <summary>
        /// Specified async task will be executed after engine is initialized but before starting the test run.
        /// </summary>
        public E2E With (Func<UniTask> task)
        {
            with.Add(task);
            return this;
        }

        /// <summary>
        /// Specified action will be invoked after engine is initialized but before starting the test run.
        /// </summary>
        public E2E With (Action action) => With(() => {
            action();
            return UniTask.CompletedTask;
        });

        /// <summary>
        /// Initializes the engine and starts test playback.
        /// </summary>
        /// <param name="sequence">Sequence to play after starting the test (optional).</param>
        public ISequence Play (ISequence sequence = null)
        {
            if (Configuration.GetOrDefault<EngineConfiguration>().InitializeOnApplicationLoad)
                Shortcuts.Fail("Disable 'Initialize On Application Load' in engine config before running tests.");
            if (options.Cover) Coverage.Enable();
            else Coverage.Disable();
            var seq = new Sequence();
            seq.Enqueue(async () => {
                if (Engine.Initialized || Engine.Initializing) Engine.Destroy();
                await AsyncUtils.DelayFrame(1);
                await RuntimeInitializer.Initialize(configurator, services);
                foreach (var task in with) await task();
            });
            if (sequence != null) seq.Play(sequence);
            return seq;
        }
    }
}
