using System;
using System.Collections.Generic;
using System.Linq;
using Naninovel.Commands;
using Naninovel.Parsing;

namespace Naninovel
{
    public class GenericTextLineParser : ScriptLineParser<GenericTextScriptLine, GenericLine>
    {
        protected virtual CommandParser CommandParser { get; }
        protected virtual GenericLine Model { get; private set; }
        protected virtual IList<Command> InlinedCommands { get; } = new List<Command>();
        protected virtual string AuthorId => Model.Prefix?.Author ?? "";
        protected virtual string AuthorAppearance => Model.Prefix?.Appearance ?? "";
        protected virtual PlaybackSpot Spot => new(ScriptPath, LineIndex, InlinedCommands.Count);

        private readonly MixedValueParser mixedParser;
        private readonly IErrorHandler errorHandler;
        private readonly Type printCommandType;

        public GenericTextLineParser (ITextIdentifier identifier, IErrorHandler errorHandler = null)
        {
            this.errorHandler = errorHandler;
            mixedParser = new(identifier);
            CommandParser = new(identifier, errorHandler);
            printCommandType = Command.CommandTypes.Values.First(typeof(PrintText).IsAssignableFrom);
        }

        protected override GenericTextScriptLine Parse (GenericLine lineModel)
        {
            ResetState(lineModel);
            AddAppearanceChange();
            AddContent();
            AddLastWaitInput();
            return new(InlinedCommands, LineIndex, lineModel.Indent, LineHash);
        }

        protected virtual void ResetState (GenericLine model)
        {
            Model = model;
            InlinedCommands.Clear();
        }

        protected virtual void AddAppearanceChange ()
        {
            if (string.IsNullOrEmpty(AuthorId)) return;
            if (string.IsNullOrEmpty(AuthorAppearance)) return;
            AddCommand(new ModifyCharacter {
                IsGenericPrefix = true,
                IdAndAppearance = new NamedString(AuthorId, AuthorAppearance),
                Wait = false,
                PlaybackSpot = Spot,
                Indent = Model.Indent
            });
        }

        protected virtual void AddContent ()
        {
            foreach (var content in Model.Content)
                if (content is InlinedCommand inlined)
                    if (string.IsNullOrEmpty(inlined.Command.Identifier)) continue;
                    else AddCommand(inlined.Command);
                else AddGenericText(content as MixedValue);
        }

        protected virtual void AddCommand (Parsing.Command commandModel)
        {
            var spot = new PlaybackSpot(ScriptPath, LineIndex, InlinedCommands.Count);
            var args = new CommandParseArgs(commandModel, spot, Model.Indent, Transient);
            var command = CommandParser.Parse(args);
            AddCommand(command);
        }

        protected virtual void AddCommand (Command command)
        {
            if (command is ParametrizeGeneric param)
                ParameterizeLastPrint(param);

            // Route [i] after printed text to wait input param of the print command.
            if (command is WaitForInput && InlinedCommands.LastOrDefault() is PrintText print)
                print.WaitForInput = true;
            else InlinedCommands.Add(command);
        }

        protected virtual void ParameterizeLastPrint (ParametrizeGeneric param)
        {
            var print = InlinedCommands.LastOrDefault(c => c is PrintText) as PrintText;
            if (print is null)
                throw new Error(Engine.FormatMessage(
                    "Failed to parametrize generic text: make sure [< ...] is inlined after text.", Spot));

            if (Command.Assigned(param.PrinterId)) print.PrinterId = param.PrinterId;
            if (Command.Assigned(param.AuthorId)) print.AuthorId = param.AuthorId;
            if (Command.Assigned(param.AuthorLabel)) print.AuthorLabel = param.AuthorLabel.Value.Ref();
            if (Command.Assigned(param.RevealSpeed)) print.RevealSpeed = param.RevealSpeed;
            if (Command.Assigned(param.SkipWaitingInput)) print.WaitForInput = !param.SkipWaitingInput;
            if (Command.Assigned(param.Join)) print.ResetPrinter = !param.Join;
        }

        protected virtual void AddGenericText (MixedValue genericText)
        {
            var printedBefore = InlinedCommands.Any(c => c is PrintText);
            var print = (PrintText)Activator.CreateInstance(printCommandType);
            var raw = mixedParser.Parse(genericText, !Transient);
            print.Text = CommandParameter.FromRaw<LocalizableTextParameter>(raw, Spot, out var errors);
            if (errors != null) errorHandler?.HandleError(new(errors, 0, 0));
            if (!string.IsNullOrEmpty(AuthorId)) print.AuthorId = AuthorId;
            if (printedBefore)
            {
                print.Append = true;
                print.ResetPrinter = false;
            }
            print.Wait = true;
            print.WaitForInput = false;
            print.PlaybackSpot = Spot;
            print.Indent = Model.Indent;
            AddCommand(print);
        }

        protected virtual void AddLastWaitInput ()
        {
            if (!InlinedCommands.Any(c => c is PrintText)) return;
            if (InlinedCommands.Any(c => c is ParametrizeGeneric param &&
                                         Command.Assigned(param.SkipWaitingInput) && param.SkipWaitingInput)) return;
            if (InlinedCommands.LastOrDefault() is WaitForInput) return;
            if (InlinedCommands.LastOrDefault() is PrintText print)
                print.WaitForInput = true;
            else AddCommand(new WaitForInput { PlaybackSpot = Spot, Indent = Model.Indent });
        }
    }
}
