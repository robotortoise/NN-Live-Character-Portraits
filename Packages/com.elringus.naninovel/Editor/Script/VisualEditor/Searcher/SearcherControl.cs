using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Naninovel.Searcher
{
    internal class SearcherControl : VisualElement
    {
        // Window constants.
        private const string k_WindowTitleLabel = "windowTitleLabel";
        private const string k_WindowDetailsPanel = "windowDetailsVisualContainer";
        private const string k_WindowResultsScrollViewName = "windowResultsScrollView";
        private const string k_WindowSearchTextFieldName = "searchBox";
        private const string k_WindowAutoCompleteLabelName = "autoCompleteLabel";
        private const string k_WindowSearchIconName = "searchIcon";
        private const string k_WindowResizerName = "windowResizer";
        private const int k_TabCharacter = 9;

        private Label m_AutoCompleteLabel;
        private IEnumerable<SearcherItem> m_Results;
        private List<SearcherItem> m_VisibleResults;
        private HashSet<SearcherItem> m_ExpandedResults;
        private Searcher m_Searcher;
        private string m_SuggestedTerm;
        private string m_Text = string.Empty;
        private Action<SearcherItem> m_SelectionCallback;
        private ListView m_ListView;
        private TextField m_SearchTextField;
        private VisualElement m_SearchTextInput;
        private VisualElement m_DetailsPanel;

        internal Label TitleLabel { get; }
        internal VisualElement Resizer { get; }

        private readonly ScriptsConfiguration config;

        public SearcherControl ()
        {
            config = Configuration.GetOrDefault<ScriptsConfiguration>();

            // Load window template.
            var windowUxmlTemplate = Resources.Load<VisualTreeAsset>("Searcher/SearcherWindow");

            // Clone Window Template.
            var windowRootVisualElement = windowUxmlTemplate.CloneTree();
            windowRootVisualElement.AddToClassList("content");

            windowRootVisualElement.StretchToParentSize();

            // Add Window VisualElement to window's RootVisualContainer
            Add(windowRootVisualElement);

            m_VisibleResults = new();
            m_ExpandedResults = new();

            m_ListView = this.Q<ListView>(k_WindowResultsScrollViewName);

            if (m_ListView != null)
            {
                m_ListView.bindItem = Bind;
                m_ListView.RegisterCallback<KeyDownEvent>(OnResultsScrollViewKeyDown);
                m_ListView.itemsChosen += obj => m_SelectionCallback(obj?.FirstOrDefault() as SearcherItem);
                m_ListView.selectionChanged += selectedItems => m_Searcher.Adapter.OnSelectionChanged(selectedItems.OfType<SearcherItem>());
                m_ListView.focusable = true;
                m_ListView.tabIndex = 1;
            }

            m_DetailsPanel = this.Q(k_WindowDetailsPanel);

            TitleLabel = this.Q<Label>(k_WindowTitleLabel);

            m_SearchTextField = this.Q<TextField>(k_WindowSearchTextFieldName);
            if (m_SearchTextField != null)
            {
                m_SearchTextField.focusable = true;
                m_SearchTextField.RegisterCallback<InputEvent>(OnSearchTextFieldTextChanged);

                m_SearchTextInput = m_SearchTextField.Q(TextInputBaseField<string>.textInputUssName);
                m_SearchTextInput.RegisterCallback<KeyDownEvent>(OnSearchTextFieldKeyDown);
            }

            m_AutoCompleteLabel = this.Q<Label>(k_WindowAutoCompleteLabelName);

            Resizer = this.Q(k_WindowResizerName);

            RegisterCallback<AttachToPanelEvent>(OnEnterPanel);
            RegisterCallback<DetachFromPanelEvent>(OnLeavePanel);

            EditorApplication.update += HackDueToListViewScrollViewStealingFocus;

            style.flexGrow = 1;
        }

        private void HackDueToListViewScrollViewStealingFocus ()
        {
            m_SearchTextInput?.Focus();
            // ReSharper disable once DelegateSubtraction
            EditorApplication.update -= HackDueToListViewScrollViewStealingFocus;
        }

        private void OnEnterPanel (AttachToPanelEvent e)
        {
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        private void OnLeavePanel (DetachFromPanelEvent e)
        {
            UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }

        private void OnKeyDown (KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                CancelSearch();
            }
        }

        private void CancelSearch ()
        {
            OnSearchTextFieldTextChanged(InputEvent.GetPooled(m_Text, string.Empty));
            m_SelectionCallback(null);
        }

        public void Setup (Searcher searcher, Action<SearcherItem> selectionCallback)
        {
            m_Searcher = searcher;
            m_SelectionCallback = selectionCallback;

            m_DetailsPanel.AddToClassList("hidden");

            if (m_Searcher?.Adapter != null)
                TitleLabel.text = m_Searcher.Adapter.Title;

            if (string.IsNullOrEmpty(TitleLabel.text))
            {
                TitleLabel.parent.style.visibility = Visibility.Hidden;
                TitleLabel.parent.style.position = Position.Absolute;
            }

            Refresh();
        }

        private void Refresh ()
        {
            var query = m_Text;
            m_Results = m_Searcher.Search(query);
            GenerateVisibleResults();

            // The first item in the results is always the highest scored item.
            // We want to scroll to and select this item.
            var visibleIndex = -1;
            m_SuggestedTerm = string.Empty;

            var results = m_Results.ToList();
            if (results.Any())
            {
                var scrollToItem = results.First();
                visibleIndex = m_VisibleResults.IndexOf(scrollToItem);

                var cursorIndex = m_SearchTextField.cursorIndex;

                if (query.Length > 0)
                {
                    var strings = scrollToItem.Name.Split(' ');
                    var wordStartIndex = cursorIndex == 0 ? 0 : query.LastIndexOf(' ', cursorIndex - 1) + 1;
                    var word = query.Substring(wordStartIndex, cursorIndex - wordStartIndex);

                    if (word.Length > 0)
                        foreach (var t in strings)
                        {
                            if (t.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                            {
                                m_SuggestedTerm = t;
                                break;
                            }
                        }
                }
            }

            m_ListView.itemsSource = m_VisibleResults;
            m_ListView.makeItem = m_Searcher.Adapter.MakeItem;
            Rebuild(m_ListView);

            SetSelectedElementInResultsList(visibleIndex);
        }

        private void GenerateVisibleResults ()
        {
            if (string.IsNullOrEmpty(m_Text))
            {
                m_ExpandedResults.Clear();
                RemoveChildrenFromResults();
                return;
            }

            RegenerateVisibleResults();
            ExpandAllParents();
        }

        private void ExpandAllParents ()
        {
            m_ExpandedResults.Clear();
            foreach (var item in m_VisibleResults)
                if (item.HasChildren)
                    m_ExpandedResults.Add(item);
        }

        private void RemoveChildrenFromResults ()
        {
            m_VisibleResults.Clear();
            var parents = new HashSet<SearcherItem>();

            foreach (var item in m_Results.Where(i => !parents.Contains(i)))
            {
                var currentParent = item;

                while (true)
                {
                    if (currentParent.Parent == null)
                    {
                        if (!parents.Add(currentParent))
                            break;

                        m_VisibleResults.Add(currentParent);
                        break;
                    }

                    currentParent = currentParent.Parent;
                }
            }

            if (m_Searcher.SortComparison != null)
                m_VisibleResults.Sort(m_Searcher.SortComparison);
        }

        private void RegenerateVisibleResults ()
        {
            var idSet = new HashSet<SearcherItem>();
            m_VisibleResults.Clear();

            foreach (var item in m_Results.Where(item => !idSet.Contains(item)))
            {
                idSet.Add(item);
                m_VisibleResults.Add(item);

                var currentParent = item.Parent;
                while (currentParent != null)
                {
                    if (idSet.Add(currentParent))
                    {
                        m_VisibleResults.Add(currentParent);
                    }

                    currentParent = currentParent.Parent;
                }

                AddResultChildren(item, idSet);
            }

            var comparison = m_Searcher.SortComparison ?? ((i1, i2) => {
                var result = i1.Database.Id - i2.Database.Id;
                return result != 0 ? result : i1.Id - i2.Id;
            });
            m_VisibleResults.Sort(comparison);
        }

        private void AddResultChildren (SearcherItem item, ISet<SearcherItem> idSet)
        {
            if (!item.HasChildren)
                return;

            foreach (var child in item.Children)
            {
                if (idSet.Add(child))
                {
                    m_VisibleResults.Add(child);
                }

                AddResultChildren(child, idSet);
            }
        }

        private bool HasChildResult (SearcherItem item)
        {
            if (m_Results.Contains(item))
                return true;

            foreach (var child in item.Children)
            {
                if (HasChildResult(child))
                    return true;
            }

            return false;
        }

        private ItemExpanderState GetExpanderState (int index)
        {
            var item = m_VisibleResults[index];

            foreach (var child in item.Children)
            {
                if (!m_VisibleResults.Contains(child) && !HasChildResult(child))
                    continue;

                return m_ExpandedResults.Contains(item) ? ItemExpanderState.Expanded : ItemExpanderState.Collapsed;
            }

            return ItemExpanderState.Hidden;
        }

        private void Bind (VisualElement target, int index)
        {
            var item = m_VisibleResults[index];
            var expanderState = GetExpanderState(index);
            var expander = m_Searcher.Adapter.Bind(target, item, expanderState, m_Text);
            expander.RegisterCallback<MouseDownEvent>(ExpandOrCollapse);
        }

        private static void GetItemsToHide (SearcherItem parent, ref HashSet<SearcherItem> itemsToHide)
        {
            if (!parent.HasChildren)
            {
                itemsToHide.Add(parent);
                return;
            }

            foreach (var child in parent.Children)
            {
                itemsToHide.Add(child);
                GetItemsToHide(child, ref itemsToHide);
            }
        }

        private void HideUnexpandedItems ()
        {
            // Hide unexpanded children.
            var itemsToHide = new HashSet<SearcherItem>();
            foreach (var item in m_VisibleResults)
            {
                if (m_ExpandedResults.Contains(item))
                    continue;

                if (!item.HasChildren)
                    continue;

                if (itemsToHide.Contains(item))
                    continue;

                // We need to hide its children.
                GetItemsToHide(item, ref itemsToHide);
            }

            foreach (var item in itemsToHide)
                m_VisibleResults.Remove(item);
        }

        // ReSharper disable once UnusedMember.Local
        private void RefreshListViewOn ()
        {
            // Need this workaround until then.
            // See: https://fogbugz.unity3d.com/f/cases/1027728/
            // And: https://gitlab.internal.unity3d.com/upm-packages/editor/com.unity.searcher/issues/9

            var scrollView = m_ListView.Q<ScrollView>();

            var scroller = scrollView?.Q<Scroller>("VerticalScroller");
            if (scroller == null)
                return;

            var oldValue = scroller.value;
            scroller.value = oldValue + 1.0f;
            scroller.value = oldValue - 1.0f;
            scroller.value = oldValue;
        }

        private void Expand (SearcherItem item)
        {
            m_ExpandedResults.Add(item);

            RegenerateVisibleResults();
            HideUnexpandedItems();
            Rebuild(m_ListView);
        }

        private void Collapse (SearcherItem item)
        {
            // if it's already collapsed or not collapsed
            if (!m_ExpandedResults.Remove(item))
            {
                // this case applies for a left arrow key press
                if (item.Parent != null)
                    SetSelectedElementInResultsList(m_VisibleResults.IndexOf(item.Parent));

                // even if it's a root item and has no parents, do nothing more
                return;
            }

            RegenerateVisibleResults();
            HideUnexpandedItems();
            Rebuild(m_ListView);
        }

        private void ExpandOrCollapse (MouseDownEvent evt)
        {
            if (evt.target is not VisualElement expanderLabel)
                return;

            VisualElement itemElement = expanderLabel.GetFirstAncestorOfType<TemplateContainer>();

            if (itemElement?.userData is not SearcherItem { HasChildren: true } item
                || !expanderLabel.ClassListContains("Expanded") && !expanderLabel.ClassListContains("Collapsed"))
                return;

            if (!m_ExpandedResults.Contains(item))
                Expand(item);
            else
                Collapse(item);

            evt.StopImmediatePropagation();
        }

        private void OnSearchTextFieldTextChanged (InputEvent inputEvent)
        {
            var text = inputEvent.newData;

            if (string.Equals(text, m_Text))
                return;

            // This is necessary due to OnTextChanged(...) being called after user inputs that have no impact on the text.
            // Ex: Moving the caret.
            m_Text = text;

            // If backspace is pressed and no text remain, clear the suggestion label.
            if (string.IsNullOrEmpty(text))
            {
                this.Q(k_WindowSearchIconName).RemoveFromClassList("Active");

                // Display the unfiltered results list.
                Refresh();

                m_AutoCompleteLabel.text = string.Empty;
                m_SuggestedTerm = string.Empty;

                SetSelectedElementInResultsList(0);

                return;
            }

            if (!this.Q(k_WindowSearchIconName).ClassListContains("Active"))
                this.Q(k_WindowSearchIconName).AddToClassList("Active");

            Refresh();

            // Calculate the start and end indexes of the word being modified (if any).
            var cursorIndex = m_SearchTextField.cursorIndex;

            // search toward the beginning of the string starting at the character before the cursor
            // +1 because we want the char after a space, or 0 if the search fails
            var wordStartIndex = cursorIndex == 0 ? 0 : (text.LastIndexOf(' ', cursorIndex - 1) + 1);

            // search toward the end of the string from the cursor index
            var wordEndIndex = text.IndexOf(' ', cursorIndex);
            if (wordEndIndex == -1) // no space found, assume end of string
                wordEndIndex = text.Length;

            // Clear the suggestion term if the caret is not within a word (both start and end indexes are equal, ex: (space)caret(space))
            // or the user didn't append characters to a word at the end of the query.
            if (wordStartIndex == wordEndIndex || wordEndIndex < text.Length)
            {
                m_AutoCompleteLabel.text = string.Empty;
                m_SuggestedTerm = string.Empty;
                return;
            }

            var word = text.Substring(wordStartIndex, wordEndIndex - wordStartIndex);

            if (!string.IsNullOrEmpty(m_SuggestedTerm))
            {
                var wordSuggestion =
                    word + m_SuggestedTerm.Substring(word.Length, m_SuggestedTerm.Length - word.Length);
                text = text.Remove(wordStartIndex, word.Length);
                text = text.Insert(wordStartIndex, wordSuggestion);
                m_AutoCompleteLabel.text = text;
            }
            else
            {
                m_AutoCompleteLabel.text = string.Empty;
            }
        }

        private void OnResultsScrollViewKeyDown (KeyDownEvent keyDownEvent)
        {
            switch (keyDownEvent.keyCode)
            {
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                    return;
                default:
                    SetSelectedElementInResultsList(keyDownEvent);
                    break;
            }
        }

        private void OnSearchTextFieldKeyDown (KeyDownEvent keyDownEvent)
        {
            // First, check if we cancelled the search.
            if (keyDownEvent.keyCode == KeyCode.Escape)
            {
                CancelSearch();
                return;
            }

            // For some reason the KeyDown event is raised twice when entering a character.
            // As such, we ignore one of the duplicate event.
            // This workaround was recommended by the Editor team. The cause of the issue relates to how IMGUI works
            // and a fix was not in the works at the moment of this writing.
            if (keyDownEvent.character == k_TabCharacter)
            {
                // Prevent switching focus to another visual element.
                keyDownEvent.StopPropagation();

                return;
            }

            // If Tab is pressed, complete the query with the suggested term.
            if (keyDownEvent.keyCode == KeyCode.Tab)
            {
                // Used to prevent the TAB input from executing it's default behavior. We're hijacking it for auto-completion.
                keyDownEvent.StopPropagation();

                if (!string.IsNullOrEmpty(m_SuggestedTerm))
                {
                    SelectAndReplaceCurrentWord();
                    m_AutoCompleteLabel.text = string.Empty;

                    m_Text = m_SearchTextField.text;

                    Refresh();

                    m_SuggestedTerm = string.Empty;
                }
            }
            else
            {
                SetSelectedElementInResultsList(keyDownEvent);
            }
        }

        private void SelectAndReplaceCurrentWord ()
        {
            var s = m_SearchTextField.value;
            var lastWordIndex = s.LastIndexOf(' ');
            lastWordIndex++;

            var newText = s[..lastWordIndex] + m_SuggestedTerm;
            var magicMoveCursorToEndString = new string('\uDC00', newText.Length);
            m_SearchTextField.value = magicMoveCursorToEndString;
            m_SearchTextField.value = newText;
        }

        private void SetSelectedElementInResultsList (KeyDownEvent evt)
        {
            if (evt.keyCode == config.InsertLineKey && (evt.modifiers & config.InsertLineModifier) != 0)
            {
                m_SelectionCallback(new("Generic Text"));
                return;
            }

            int index;
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    m_SelectionCallback(null);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (m_ListView.selectedIndex != -1)
                    {
                        m_SelectionCallback((SearcherItem)m_ListView.selectedItem);
                    }
                    else
                    {
                        m_SelectionCallback(null);
                    }
                    break;
                case KeyCode.LeftArrow:
                    index = m_ListView.selectedIndex;
                    if (index >= 0 && index < m_ListView.itemsSource.Count)
                        Collapse(m_ListView.selectedItem as SearcherItem);
                    break;
                case KeyCode.RightArrow:
                    index = m_ListView.selectedIndex;
                    if (index >= 0 && index < m_ListView.itemsSource.Count)
                        Expand(m_ListView.selectedItem as SearcherItem);
                    break;
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                    index = m_ListView.selectedIndex;
                    if (index >= 0 && index < m_ListView.itemsSource.Count)
                        m_ListView.Focus();
                    break;
            }
        }

        private void SetSelectedElementInResultsList (int selectedIndex)
        {
            var newIndex = selectedIndex >= 0 && selectedIndex < m_VisibleResults.Count ? selectedIndex : -1;
            if (newIndex < 0)
                return;

            m_ListView.selectedIndex = newIndex;
            m_ListView.ScrollToItem(m_ListView.selectedIndex);
        }

        private static void Rebuild (ListView listView)
        {
            listView.Rebuild();
        }
    }
}
