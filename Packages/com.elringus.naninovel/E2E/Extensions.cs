using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Naninovel.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using static Naninovel.E2E.Shortcuts;

namespace Naninovel.E2E
{
    /// <summary>
    /// Extensions for <see cref="E2E"/> and <see cref="ISequence"/>.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Makes engine use transient (in-memory) state serialization handlers during test run.
        /// </summary>
        /// <remarks>
        /// By default state is clean (simulating first game run), but custom initial
        /// global and settings state can be specified via the optional parameters.
        /// </remarks>
        public static E2E WithTransientState (this E2E e2e, GlobalStateMap global = null, SettingsStateMap settings = null)
        {
            e2e.WithConfig<StateConfiguration>(c => {
                c.GameStateHandler = typeof(TransientGameStateSerializer).AssemblyQualifiedName;
                c.GlobalStateHandler = typeof(TransientGlobalStateSerializer).AssemblyQualifiedName;
                c.SettingsStateHandler = typeof(TransientSettingsStateSerializer).AssemblyQualifiedName;
            });
            TransientGlobalStateSerializer.DefaultFactory = () => global ?? new GlobalStateMap();
            TransientSettingsStateSerializer.DefaultFactory = () => settings ?? new SettingsStateMap();
            return e2e;
        }

        /// <summary>
        /// Makes playback as fast as possible during test run.
        /// </summary>
        public static E2E WithFastForward (this E2E e2e) => e2e
            .With(() => Service<IScriptPlayer>().OnWaitingForInput += _ => Input("Continue").Activate(1))
            .WithConfig<ScriptPlayerConfiguration>(c => c.SkipTimeScale = 999)
            .WithConfig<TextPrintersConfiguration>(c => {
                c.MaxRevealDelay = 0;
                foreach (var meta in c.Metadata.GetAllMetas())
                    meta.PrintFrameDelay = 0;
            });

        /// <summary>
        /// Starts test playback and waits for specified condition.
        /// </summary>
        public static ISequence Once (this E2E e2e, Func<bool> condition, float timeout = 10,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
            e2e.Play(Once(new Sequence(), condition, timeout, file, line));

        /// <summary>
        /// Given title UI has new game button with the specified name, will start new game after starting the test.
        /// </summary>
        public static ISequence StartNew (this E2E e2e, string newGameButton = "NewGameButton", float timeout = 10,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
            e2e.Play(StartNew(new Sequence(), newGameButton, timeout, file, line));

        /// <summary>
        /// Given title UI has new game button with the specified name, will start new game.
        /// </summary>
        public static ISequence StartNew (this ISequence seq, string newGameButton = "NewGameButton", float timeout = 10,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
            seq.Once(InTitle, timeout, file, line).Click(newGameButton, file, line);

        /// <summary>
        /// Enqueues specified sequence as task; can be used for composing.
        /// </summary>
        /// <remarks>
        /// If specified sequence is null, won't enqueue it; this can be used
        /// for conditional composition of chained suit sequences.
        /// </remarks>
        public static ISequence Play (this ISequence seq, ISequence sequence) =>
            sequence != null ? seq.Enqueue(sequence.ToUniTask) : seq;

        /// <summary>
        /// Enqueues multiple specified sequences as tasks; can be used for composing.
        /// </summary>
        public static ISequence Play (this ISequence seq, params ISequence[] sequences)
        {
            foreach (var sequence in sequences)
                seq.Play(sequence);
            return seq;
        }

        /// <summary>
        /// Loads specified game state.
        /// </summary>
        /// <remarks>
        /// Will as well hide <see cref="Naninovel.UI.ITitleUI"/> in case it's visible,
        /// making the task usable right after starting a test.
        /// </remarks>
        public static ISequence Load (this ISequence seq, GameStateMap state) => seq.Enqueue(async () => {
            UI<ITitleUI>()?.Hide();
            var slotId = Guid.NewGuid().ToString();
            await Service<IStateManager>().GameSlotManager.Save(slotId, state);
            await Service<IStateManager>().LoadGame(slotId);
        });

        /// <summary>
        /// Continues playing until specified condition is met or timeout (in seconds) is reached, in which case fails.
        /// </summary>
        public static ISequence Once (this ISequence seq, Func<bool> condition, float timeout = 10,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => seq.Enqueue(() => {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            try { return UniTask.WaitUntil(condition, default, cts.Token).ContinueWith(cts.Dispose); }
            catch (OperationCanceledException)
            {
                Fail("Timout.", file, line);
                return UniTask.CompletedTask;
            }
        });

        /// <summary>
        /// Waits for the specified condition and executes 'do' sequence; fails in case timeout (in seconds) is reached.
        /// When 'continue' condition is specified, will check it after executing 'do', and, when met, reset the timeout and repeat.
        /// </summary>
        public static ISequence On (this ISequence seq, Func<bool> condition, ISequence @do, Func<bool> @continue = null, float timeout = 10,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => seq.Enqueue(async () => {
            do
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                try { await UniTask.WaitUntil(condition, default, cts.Token); }
                catch (OperationCanceledException) { Fail("Timeout.", file, line); }
                if (@do != null) await @do.ToUniTask();
                cts.Dispose();
            } while (@continue?.Invoke() ?? false);
        });

        /// <summary>
        /// Waits specified number of seconds before continuing.
        /// </summary>
        public static ISequence Wait (this ISequence seq, float seconds) =>
            seq.Enqueue(() => UniTask.Delay(TimeSpan.FromSeconds(seconds)));

        /// <summary>
        /// Asserts specified assert function returns true.
        /// </summary>
        public static ISequence Ensure (this ISequence seq, Func<bool> assert,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
            seq.Enqueue(() => {
                if (!assert()) Fail("E2E assertion failed.", file, line);
            });

        /// <summary>
        /// Asserts specified condition is met; otherwise will fail with the annotated message.
        /// </summary>
        public static ISequence Ensure (this ISequence seq, Condition assert,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
            seq.Enqueue(() => {
                if (!assert.Result()) Fail($"E2E assertion failed: {assert.Message()}", file, line);
            });

        /// <summary>
        /// Invokes <see cref="UnityEngine.EventSystems.IPointerClickHandler.OnPointerClick"/>
        /// on game object with the specified name (will fail in case object not found).
        /// </summary>
        public static ISequence Click (this ISequence seq, string objectName,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => seq.Enqueue(() => {
            var go = GameObject.Find(objectName);
            if (!go) Fail($"Failed to click '{objectName}': object with the specified name not found.", file, line);
            var clicker = go.GetComponentInChildren<IPointerClickHandler>();
            if (clicker == null) Fail($"Failed to click '{objectName}': object missing '{nameof(IPointerClickHandler)}' component.", file, line);
            clicker.OnPointerClick(new(EventSystem.current));
        });

        /// <summary>
        /// Selects choice with the specified summary text ID or first available when not specified.
        /// Will fail in case handler with the ID is not found or not visible.
        /// </summary>
        /// <remarks>
        /// Example of assigning custom text ID to choice summary:
        /// <code>@choice "Choice summary|#choice-id|"</code>
        /// </remarks>
        public static ISequence Choose (this ISequence seq, string id = null,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => seq.Enqueue(() => {
            var filter = new Func<ChoiceState, bool>(c => c.Summary.Parts.Any(p => id is null || p.Id == id));
            var handler = Choices.FirstOrDefault(a => a.Visible && a.Choices.Any(filter));
            if (handler == null) Fail($"Failed to choose '{id}': no choice handlers with the specified summary ID are visible.", file, line);
            handler.HandleChoice(handler.Choices.First(filter).Id);
        });
    }
}
