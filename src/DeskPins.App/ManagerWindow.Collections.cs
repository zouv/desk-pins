using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed partial class ManagerWindow
    {
        private const int DropBefore = 0, DropInside = 1, DropAfter = 2;
        private const string CollectionFormat = "DeskPins.Collection";
        private TreeViewItem treeDragItem, highlightedNode;
        private Point treeDragStart;
        private bool treeDragReady;

        private void TreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (buildingTree) return;
            var node = tree.SelectedItem as TreeViewItem;
            outlineProperties.IsEnabled = outlineDelete.IsEnabled = node != null;
            if (node == null) return;
            SaveEditor();
            if (node.Tag is Folder folder) folderFilter = folder.Id;
            else if (node.Tag is NoteStack stack) stackFilter = stack.Id;
            else if (node.Tag is Tag tag) tagFilter = tag.Id;
            BuildTree(); Refresh();
        }

        private void BuildTree()
        {
            if (tree == null) return;
            int category = SelectedCategory;
            if (!IsOutlineCategory(category)) { if (tree.Items.Count > 0) tree.Items.Clear(); navigationSnapshot = category + "|none"; return; }
            if (category == 8 && !host.Library.Catalog.Folders.Any(f => f.Id == folderFilter)) folderFilter = "";
            if (category == 2 && !host.Library.Catalog.Stacks.Any(s => s.Id == stackFilter)) stackFilter = "";
            if (category == 9 && !host.Library.Catalog.Tags.Any(t => t.Id == tagFilter)) tagFilter = "";
            var snapshot = category + "|" + folderFilter + "|" + stackFilter + "|" + tagFilter + "|" +
                Json.Text(host.Library.Catalog.Folders.Select(f => new[] { f.Id, f.Name, f.ParentId, f.Encrypted.ToString(), StoredCount(f.Id).ToString() }).ToArray()) +
                Json.Text(host.Library.Catalog.Stacks.Select(s => new[] { s.Id, s.Name, s.Color, StackCount(s.Id).ToString() }).ToArray()) +
                Json.Text(host.Library.Catalog.Tags.Select(t => new[] { t.Id, t.Name, t.Color, TagCount(t.Id).ToString() }).ToArray());
            if (snapshot == navigationSnapshot && tree.Items.Count > 0) return;
            navigationSnapshot = snapshot; buildingTree = true;
            try
            {
                tree.Items.Clear();
                if (category == 8) foreach (var folder in host.Library.Catalog.Folders.Where(f => f.ParentId == "")) tree.Items.Add(FolderNode(folder, new HashSet<string>()));
                else if (category == 2) foreach (var stack in host.Library.Catalog.Stacks) tree.Items.Add(CollectionNode(stack, railIcons[2], stack.Name, StackCount(stack.Id), Ui.Brush(stack.Color)));
                else foreach (var tag in host.Library.Catalog.Tags) tree.Items.Add(CollectionNode(tag, railIcons[9], tag.Name, TagCount(tag.Id), Ui.Brush(tag.Color)));
                foreach (var node in Nodes(tree.Items)) if ((node.Tag as Folder)?.Id == folderFilter || (node.Tag as NoteStack)?.Id == stackFilter || (node.Tag as Tag)?.Id == tagFilter) { node.IsSelected = true; break; }
            }
            finally { buildingTree = false; }
        }

        private int StoredCount(string folderId) { return host.Library.Notes.Values.Count(n => n.State == NoteState.Stored && n.FolderId == folderId); }
        private int StackCount(string stackId) { return host.Library.Notes.Values.Count(n => n.StackId == stackId); }
        private int TagCount(string tagId) { return host.Library.Notes.Values.Count(n => n.Tags.Contains(tagId)); }

        private TreeViewItem CollectionNode(object value, string glyph, string name, int count, Brush color = null)
        {
            var node = new TreeViewItem { Header = NodeHeader(glyph, name, count, color), Tag = value, IsExpanded = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, AllowDrop = true, ContextMenu = CollectionMenu(value) };
            WireNode(node);
            return node;
        }

        private TreeViewItem FolderNode(Folder folder, HashSet<string> visited)
        {
            var node = new TreeViewItem { Header = NodeHeader(railIcons[8], (folder.Encrypted ? "🔒 " : "") + folder.Name, StoredCount(folder.Id)), Tag = folder, AllowDrop = true, IsExpanded = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, ContextMenu = CollectionMenu(folder) };
            WireNode(node);
            if (!visited.Add(folder.Id)) return node;
            foreach (var child in host.Library.Catalog.Folders.Where(f => f.ParentId == folder.Id)) node.Items.Add(FolderNode(child, visited));
            return node;
        }

        private void WireNode(TreeViewItem node)
        {
            node.DragOver += NodeDragOver;
            node.DragLeave += (s, e) => ClearHighlight();
            node.Drop += NodeDrop;
        }

        private static Grid NodeHeader(string glyph, string name, int count, Brush color = null)
        {
            var panel = new Grid { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = color ?? Ui.Brush("#1268B3"), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(icon);
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 1); panel.Children.Add(label);
            var countBlock = new TextBlock { Text = count.ToString(), Foreground = Ui.Brush("#111827"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(countBlock, 2); panel.Children.Add(countBlock);
            return panel;
        }

        private static IEnumerable<TreeViewItem> Nodes(ItemCollection items) { foreach (TreeViewItem item in items) { yield return item; foreach (var child in Nodes(item.Items)) yield return child; } }

        private ContextMenu BlankTreeMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(Ui.Item("新建便签", NewNoteForSelection));
            if (SelectedCategory == 8) menu.Items.Add(Ui.Item("新建文件夹", () => CreateCollectionItem("folders", null)));
            else if (SelectedCategory == 2) menu.Items.Add(Ui.Item("新建堆叠", () => CreateCollectionItem("stacks", null)));
            else if (SelectedCategory == 9) menu.Items.Add(Ui.Item("新建标签", () => CreateCollectionItem("tags", null)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.Item("显示全部", () => { folderFilter = stackFilter = tagFilter = ""; BuildTree(); Refresh(); }));
            return menu;
        }

        private ContextMenu CollectionMenu(object item)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Ui.Item("新建便签", () => NewNoteIn(item)));
            if (item is Folder folder)
            {
                menu.Items.Add(Ui.Item("新建子文件夹", () => CreateCollectionItem("folders", folder)));
                menu.Items.Add(Ui.Item("切换加密", () => ToggleFolderEncryption(folder)));
            }
            else if (item is NoteStack) menu.Items.Add(Ui.Item("新建堆叠", () => CreateCollectionItem("stacks", null)));
            else if (item is Tag) menu.Items.Add(Ui.Item("新建标签", () => CreateCollectionItem("tags", null)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.Item("重命名…", () => EditCollection(item)));
            menu.Items.Add(Ui.Item("属性…", () => EditCollection(item)));
            menu.Items.Add(Ui.Item("删除", () => DeleteCollection(item)));
            return menu;
        }

        private void OpenCreateMenu(Button anchor)
        {
            var menu = BlankTreeMenu();
            if (SelectedCategory == 8 && tree.SelectedItem is TreeViewItem node && node.Tag is Folder folder) menu.Items.Insert(1, Ui.Item("新建子文件夹", () => CreateCollectionItem("folders", folder)));
            menu.PlacementTarget = anchor; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
        }

        private void CreateCollectionItem(string kind, Folder parent)
        {
            object item;
            if (kind == "folders") { var folder = new Folder { ParentId = parent == null ? "" : parent.Id }; if (!CollectionEditor.Edit(this, host, folder, kind)) return; host.Library.Catalog.Folders.Add(folder); item = folder; }
            else if (kind == "stacks") { var stack = new NoteStack(); if (!CollectionEditor.Edit(this, host, stack, kind)) return; host.Library.Catalog.Stacks.Add(stack); item = stack; }
            else if (kind == "tags") { var tag = new Tag(); if (!CollectionEditor.Edit(this, host, tag, kind)) return; host.Library.Catalog.Tags.Add(tag); item = tag; }
            else return;
            host.Library.SaveCatalog();
            SetFilterTo(kind, item is Folder f ? f.Id : item is NoteStack s ? s.Id : ((Tag)item).Id);
            BuildTree(); Refresh();
        }

        private void EditSelectedCollection()
        {
            try { var node = tree.SelectedItem as TreeViewItem; if (node != null) EditCollection(node.Tag); }
            catch (Exception ex) { Ui.Error(ex); }
        }

        private void EditCollection(object item)
        {
            if (item == null) return;
            string kind = item is Folder ? "folders" : item is NoteStack ? "stacks" : item is Tag ? "tags" : null;
            if (kind == null) return;
            if (!CollectionEditor.Edit(this, host, item, kind)) return;
            host.Library.SaveCatalog();
            BuildTree(); Refresh();
        }

        private void DeleteSelectedCollection()
        {
            var node = tree.SelectedItem as TreeViewItem;
            if (node != null) DeleteCollection(node.Tag);
        }

        private void DeleteCollection(object item)
        {
            try
            {
                if (item is Folder folder)
                {
                    if (!Ui.Confirm(string.Format(Ui.Text("删除文件夹“{0}”？其中的便签会移回已收藏。"), folder.Name))) return;
                    if (folder.Encrypted) host.EnsureUnlocked(folder);
                    host.Library.Atomic(() =>
                    {
                        foreach (var n in host.Library.Notes.Values.Where(n => n.FolderId == folder.Id).ToArray()) host.Library.Move(n, NoteState.Stored);
                        foreach (var child in host.Library.Catalog.Folders.Where(f => f.ParentId == folder.Id).ToArray()) child.ParentId = folder.ParentId;
                        host.Library.Catalog.Folders.Remove(folder);
                        host.Library.SaveCatalog();
                    });
                    folderFilter = "";
                }
                else if (item is NoteStack stack)
                {
                    if (!Ui.Confirm(string.Format(Ui.Text("删除堆叠“{0}”？便签会保留并移出堆叠。"), stack.Name))) return;
                    host.Library.Atomic(() =>
                    {
                        foreach (var n in host.Library.Notes.Values.Where(n => n.StackId == stack.Id).ToArray()) { n.StackId = ""; host.Library.Save(n); }
                        host.Library.Catalog.Stacks.Remove(stack);
                        host.Library.SaveCatalog();
                    });
                    stackFilter = "";
                }
                else if (item is Tag tag)
                {
                    if (!Ui.Confirm(string.Format(Ui.Text("删除标签“{0}”？便签内容会保留。"), tag.Name))) return;
                    host.Library.Atomic(() =>
                    {
                        foreach (var n in host.Library.Notes.Values.Where(n => n.Tags.Contains(tag.Id)).ToArray()) { n.Tags.Remove(tag.Id); host.Library.Save(n); }
                        host.Library.Catalog.Tags.Remove(tag);
                        host.Library.SaveCatalog();
                    });
                    tagFilter = "";
                }
                else return;
                BuildTree(); Refresh();
            }
            catch (Exception ex) { Ui.Error(ex); }
        }

        private void ToggleFolderEncryption(Folder folder)
        {
            try
            {
                host.EnsureUnlocked(new Folder { Encrypted = true });
                host.Library.SetFolderEncrypted(folder, !folder.Encrypted);
                host.Library.SaveCatalog();
                BuildTree(); Refresh();
            }
            catch (Exception ex) { Ui.Error(ex); }
        }

        private void NewNoteForSelection() { try { NewNoteIn(tree.SelectedItem is TreeViewItem node ? node.Tag : null); } catch (Exception ex) { Ui.Error(ex); } }

        private void NewNoteIn(object item)
        {
            SaveEditor();
            int category = SelectedCategory;
            if (category != 2 && category != 8 && category != 9) { host.New(); return; }
            if (category == 8)
            {
                var folder = item as Folder;
                if (folder != null) host.EnsureUnlocked(folder);
                var note = host.Library.Create();
                host.Library.Move(note, NoteState.Stored, folder == null ? "" : folder.Id);
                Refresh(); SelectNoteInList(note.Id);
            }
            else if (category == 2)
            {
                var stack = item as NoteStack;
                var note = host.New();
                if (stack != null) { stack.Expanded = true; note.StackId = stack.Id; host.Library.Save(note); host.Library.SaveCatalog(); }
                Refresh(); SelectNoteInList(note.Id);
            }
            else if (category == 9)
            {
                var tag = item as Tag;
                var note = host.New();
                if (tag != null) { note.Tags.Add(tag.Id); host.Library.Save(note); }
                Refresh(); SelectNoteInList(note.Id);
            }
        }

        private void DropTo(DragEventArgs e, NoteState state, string folder)
        {
            if (!e.Data.GetDataPresent("DeskPins.Notes")) return;
            e.Handled = true;
            try
            {
                SaveEditor();
                var targets = ((string[])e.Data.GetData("DeskPins.Notes")).Where(host.Library.Notes.ContainsKey).Select(id => host.Library.Notes[id]).ToArray();
                foreach (var n in targets) host.FlushNote(n);
                host.Library.Atomic(() => { foreach (var n in targets) host.Library.Move(n, state, folder); });
                Refresh();
            }
            catch (Exception ex) { Ui.Error(ex); }
        }

        private void TreeMouseDown(object sender, MouseButtonEventArgs e)
        {
            treeDragReady = false; treeDragItem = null;
            var source = e.OriginalSource as DependencyObject;
            if (FindAncestor<ToggleButton>(source) != null) return;
            treeDragItem = FindTreeItem(source);
            if (treeDragItem != null) { treeDragStart = e.GetPosition(tree); treeDragReady = true; }
        }

        private void TreeMouseMove(object sender, MouseEventArgs e)
        {
            if (!treeDragReady || treeDragItem == null || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(tree);
            if (Math.Abs(point.X - treeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - treeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            treeDragReady = false;
            var tag = treeDragItem.Tag;
            string kind = tag is Folder ? "folders" : tag is NoteStack ? "stacks" : tag is Tag ? "tags" : null;
            if (kind == null) return;
            var id = tag is Folder f ? f.Id : tag is NoteStack s ? s.Id : ((Tag)tag).Id;
            try { DragDrop.DoDragDrop(tree, new DataObject(CollectionFormat, new[] { kind, id }), DragDropEffects.Move); }
            finally { treeDragItem = null; }
        }

        private void TreeDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(CollectionFormat)) return;
            e.Effects = DragDropEffects.Move; e.Handled = true;
        }

        private void TreeDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(CollectionFormat)) return;
            e.Handled = true;
            var data = (string[])e.Data.GetData(CollectionFormat);
            MoveCollectionItem(data[0], data[1], null, DropAfter);
            AfterCollectionMove(data[0], data[1]);
        }

        private void NodeDragOver(object sender, DragEventArgs e)
        {
            var node = sender as TreeViewItem;
            bool allowed = false;
            if (e.Data.GetDataPresent(CollectionFormat)) allowed = CanDropCollection((string[])e.Data.GetData(CollectionFormat), node == null ? null : node.Tag);
            else if (e.Data.GetDataPresent("DeskPins.Notes")) allowed = node != null && node.Tag is Folder;
            e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true;
            if (allowed) HighlightNode(node); else ClearHighlight();
        }

        private void NodeDrop(object sender, DragEventArgs e)
        {
            ClearHighlight();
            var node = sender as TreeViewItem; if (node == null) return;
            if (e.Data.GetDataPresent(CollectionFormat))
            {
                e.Handled = true;
                var data = (string[])e.Data.GetData(CollectionFormat);
                if (!CanDropCollection(data, node.Tag)) return;
                MoveCollectionItem(data[0], data[1], node.Tag, ZoneForNode(node, e));
                AfterCollectionMove(data[0], data[1]);
                return;
            }
            if (!e.Data.GetDataPresent("DeskPins.Notes")) return;
            var folder = node.Tag as Folder; if (folder == null) return;
            e.Handled = true;
            try { host.EnsureUnlocked(folder); DropTo(e, NoteState.Stored, folder.Id); }
            catch (Exception ex) { Ui.Error(ex); }
        }

        private static int ZoneForNode(TreeViewItem node, DragEventArgs e)
        {
            double height = Math.Max(1, node.ActualHeight); double fraction = Math.Max(0, Math.Min(1, e.GetPosition(node).Y / height));
            if (node.Tag is Folder) return fraction < .25 ? DropBefore : fraction > .75 ? DropAfter : DropInside;
            return fraction < .5 ? DropBefore : DropAfter;
        }

        private bool CanDropCollection(string[] data, object target)
        {
            if (data == null || data.Length != 2) return false;
            if (data[0] == "folders")
            {
                var folder = target as Folder; var source = host.Library.Catalog.Folders.FirstOrDefault(f => f.Id == data[1]);
                return folder != null && source != null && folder.Id != source.Id && !IsFolderUnder(folder.Id, source.Id);
            }
            if (data[0] == "stacks") { var stack = target as NoteStack; return stack != null && stack.Id != data[1]; }
            if (data[0] == "tags") { var tag = target as Tag; return tag != null && tag.Id != data[1]; }
            return false;
        }

        private bool IsFolderUnder(string folderId, string ancestorId)
        {
            var folders = host.Library.Catalog.Folders; var current = folders.FirstOrDefault(f => f.Id == folderId); var visited = new HashSet<string>();
            while (current != null && visited.Add(current.Id)) { if (current.Id == ancestorId) return true; current = folders.FirstOrDefault(f => f.Id == current.ParentId); }
            return false;
        }

        private void MoveCollectionItem(string kind, string id, object target, int zone)
        {
            var catalog = host.Library.Catalog;
            if (kind == "folders")
            {
                var list = catalog.Folders; var source = list.FirstOrDefault(f => f.Id == id); if (source == null) return;
                var folder = target as Folder;
                if (folder == null) { source.ParentId = ""; list.Remove(source); list.Add(source); }
                else if (folder.Id == source.Id || IsFolderUnder(folder.Id, source.Id)) return;
                else if (zone == DropInside) { source.ParentId = folder.Id; list.Remove(source); list.Add(source); }
                else
                {
                    source.ParentId = folder.ParentId;
                    list.Remove(source);
                    int index = list.IndexOf(folder); if (index < 0) index = list.Count; if (zone == DropAfter) index++;
                    list.Insert(Math.Max(0, Math.Min(index, list.Count)), source);
                }
            }
            else if (kind == "stacks")
            {
                var list = catalog.Stacks; var source = list.FirstOrDefault(s => s.Id == id); if (source == null) return;
                var stack = target as NoteStack;
                if (stack != null && stack.Id == id) return;
                list.Remove(source);
                if (stack == null) list.Add(source);
                else { int index = list.IndexOf(stack); if (index < 0) index = list.Count; if (zone != DropBefore) index++; list.Insert(Math.Max(0, Math.Min(index, list.Count)), source); }
            }
            else if (kind == "tags")
            {
                var list = catalog.Tags; var source = list.FirstOrDefault(t => t.Id == id); if (source == null) return;
                var tag = target as Tag;
                if (tag != null && tag.Id == id) return;
                list.Remove(source);
                if (tag == null) list.Add(source);
                else { int index = list.IndexOf(tag); if (index < 0) index = list.Count; if (zone != DropBefore) index++; list.Insert(Math.Max(0, Math.Min(index, list.Count)), source); }
            }
            else return;
            host.Library.SaveCatalog();
        }

        private void AfterCollectionMove(string kind, string id)
        {
            treeDragReady = false; treeDragItem = null;
            SetFilterTo(kind, id);
            BuildTree(); Refresh();
        }

        private void SetFilterTo(string kind, string id)
        {
            if (kind == "folders") folderFilter = id; else if (kind == "stacks") stackFilter = id; else if (kind == "tags") tagFilter = id;
        }

        private void HighlightNode(TreeViewItem node)
        {
            if (ReferenceEquals(highlightedNode, node)) return;
            ClearHighlight();
            highlightedNode = node;
            node.Background = Ui.Brush("#EAF3FB");
        }

        private void ClearHighlight()
        {
            if (highlightedNode != null) { highlightedNode.ClearValue(Control.BackgroundProperty); highlightedNode = null; }
        }

        private static T FindAncestor<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj != null && !(obj is T)) obj = obj is Visual || obj is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(obj) : LogicalTreeHelper.GetParent(obj);
            return obj as T;
        }

        private static TreeViewItem FindTreeItem(DependencyObject obj)
        {
            while (obj != null && !(obj is TreeViewItem)) obj = obj is Visual || obj is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(obj) : LogicalTreeHelper.GetParent(obj);
            return obj as TreeViewItem;
        }

        private static ListBoxItem FindItem(DependencyObject obj) { while (obj != null && !(obj is ListBoxItem)) obj = obj is Visual || obj is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(obj) : LogicalTreeHelper.GetParent(obj); return obj as ListBoxItem; }
    }
}
