using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Naninovel
{
    /// <inheritdoc cref="ITextLocalizer"/>
    [InitializeAtRuntime]
    public class TextLocalizer : ITextLocalizer
    {
        private readonly HashSet<LocalizableTextHolder> holders = new();
        private readonly StringBuilder builder = new();
        private readonly ITextManager docs;
        private readonly IScriptManager scripts;
        private readonly ILocalizationManager l10n;
        private readonly ICommunityLocalization communityL10n;

        public TextLocalizer (ITextManager docs, IScriptManager scripts,
            ILocalizationManager l10n, ICommunityLocalization communityL10n)
        {
            this.l10n = l10n;
            this.docs = docs;
            this.scripts = scripts;
            this.communityL10n = communityL10n;
        }

        public virtual UniTask InitializeService ()
        {
            l10n.AddChangeLocaleTask(HandleLocaleChanged);
            return UniTask.CompletedTask;
        }

        public virtual void ResetService () { }

        public virtual void DestroyService ()
        {
            l10n.RemoveChangeLocaleTask(HandleLocaleChanged);
            scripts.ScriptLoader.ReleaseAll(this);
            docs.DocumentLoader.ReleaseAll(this);
        }

        public virtual async UniTask Load (LocalizableText text, object holder)
        {
            using var _ = SetPool<string>.Rent(out var paths);
            using var __ = ListPool<UniTask>.Rent(out var tasks);
            var textHolder = new LocalizableTextHolder(text, holder);
            holders.Add(textHolder);
            foreach (var path in CollectScriptPaths(text, paths))
            {
                tasks.Add(scripts.ScriptLoader.LoadOrErr(path, textHolder));
                if (UsingL10nDocuments()) tasks.Add(docs.DocumentLoader.LoadOrErr(ToL10nPath(path), textHolder));
            }
            await UniTask.WhenAll(tasks);
        }

        public virtual void Hold (LocalizableText text, object holder)
        {
            using var _ = SetPool<string>.Rent(out var paths);
            var textHolder = new LocalizableTextHolder(text, holder);
            holders.Add(textHolder);
            foreach (var path in CollectScriptPaths(text, paths))
            {
                scripts.ScriptLoader.Hold(path, textHolder);
                if (UsingL10nDocuments()) docs.DocumentLoader.Hold(ToL10nPath(path), textHolder);
            }
        }

        public virtual void Release (LocalizableText text, object holder)
        {
            using var _ = SetPool<string>.Rent(out var paths);
            var textHolder = new LocalizableTextHolder(text, holder);
            holders.Add(textHolder);
            foreach (var path in CollectScriptPaths(text, paths))
            {
                scripts.ScriptLoader.Release(path, textHolder);
                if (UsingL10nDocuments()) docs.DocumentLoader.Release(ToL10nPath(path), textHolder);
            }
        }

        public virtual string Resolve (LocalizableText text)
        {
            if (text.Parts.Count == 1 && !text.Parts[0].PlainText)
                return Resolve(text.Parts[0].Id, text.Parts[0].Spot.ScriptPath);
            builder.Clear();
            foreach (var part in text.Parts)
                builder.Append(part.PlainText ? part.Text : Resolve(part.Id, part.Spot.ScriptPath));
            return builder.ToString();
        }

        protected virtual string Resolve (string id, string scriptPath)
        {
            if (UsingL10nDocuments()) return ResolveFromDocument(id, scriptPath);
            return ResolveFromScript(id, scriptPath);
        }

        protected virtual string ResolveFromScript (string id, string scriptPath)
        {
            if (!scripts.ScriptLoader.IsLoaded(scriptPath))
                throw new Error($"Failed to resolve localized text for '{scriptPath}/{id}': script resource is not loaded. " +
                                $"Make sure to hold the localizable text before resolving.");
            var value = scripts.ScriptLoader.GetLoaded(scriptPath)?.Object?.TextMap?.GetTextOrNull(id);
            if (!string.IsNullOrEmpty(value)) return value;
            Engine.Warn($"Failed to resolve localized text for '{scriptPath}/{id}': script or text mapping is not available.");
            return $"{Compiler.Syntax.TextIdOpen}{scriptPath}/{id}{Compiler.Syntax.TextIdClose}";
        }

        protected virtual string ResolveFromDocument (string id, string scriptPath)
        {
            var docPath = ToL10nPath(scriptPath);
            if (!docs.DocumentLoader.IsLoaded(docPath))
                throw new Error($"Failed to resolve localized text for '{scriptPath}/{id}': managed text document '{docPath}' is not loaded. " +
                                $"Make sure to hold the localizable text before resolving.");
            if (id.StartsWithFast(LocalizableText.RefPrefix)) id = id[1..];
            var value = docs.GetRecordValue(id, docPath);
            if (!string.IsNullOrEmpty(value)) return value;
            Engine.Warn($"Failed to resolve '{scriptPath}/{id}' localized text; will use source locale.");
            return ResolveFromScript(id, scriptPath);
        }

        protected virtual HashSet<string> CollectScriptPaths (LocalizableText text, HashSet<string> into)
        {
            foreach (var part in text.Parts)
                if (!part.PlainText)
                    into.Add(part.Spot.ScriptPath);
            return into;
        }

        protected virtual bool UsingL10nDocuments ()
        {
            return communityL10n.Active || !l10n.IsSourceLocaleSelected();
        }

        protected virtual async UniTask HandleLocaleChanged (LocaleChangedArgs args)
        {
            // Community l10n is activated on launch and can't change at runtime.
            if (communityL10n.Active) return;

            var isSource = l10n.IsSourceLocale(args.CurrentLocale);
            var wasSource = l10n.IsSourceLocale(args.PreviousLocale);
            // Changed between none-source locales: l10n docs loader handled the change.
            if (isSource == wasSource) return;

            // Switched from source to a none-source locale: load required l10n docs.
            if (wasSource) await UniTask.WhenAll(holders.Select(h => Load(h.Text, h.Holder)));
            // Switched from a non-source to source locale: release all l10n docs.
            else docs.DocumentLoader.ReleaseAll(this);
        }

        private static string ToL10nPath (string scriptPath)
        {
            return ManagedTextUtils.ResolveScriptL10nPath(scriptPath);
        }
    }
}
