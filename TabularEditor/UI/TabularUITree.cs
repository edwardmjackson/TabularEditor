﻿using Aga.Controls.Tree;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Dynamic;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using TabularEditor.BestPracticeAnalyzer;
using TabularEditor.TOMWrapper;
using TabularEditor.TreeViewAdvExtension;

namespace TabularEditor
{
    public enum FilterMode
    {
        Parent = 1,
        Child = 2,
        Flat = 3
    }

    public class TabularUITree : TabularTree, ITreeModel
    {

        public TreeViewAdv TreeView { get; set; }

        /*private string linqFilter;
        public string LinqFilter
        {
            get
            {
                return linqFilter;
            }

            set
            {
                if (linqFilter == value) return;
                linqFilter = value;

                FilterItems.Clear();
                if(!string.IsNullOrEmpty(LinqFilter))
                {
                    var sw = new Stopwatch();
                    sw.Start();
                    // Do Dynamic Linq search for each type of object (based on the types available for Best Practice Analyzer):
                    foreach (RuleScope scope in Enum.GetValues(typeof(RuleScope)))
                    {
                        FilterItems.AddRange(Linq(Analyzer.GetCollection(Model, scope), scope.GetScopeType()));
                    }
                    sw.Stop();
                    FilterExecutionTime = sw.ElapsedMilliseconds;
                }

                OnStructureChanged();
            }
        }*/

        public long FilterExecutionTime { get; private set; }
        public int FilterResultCount => 0;  //FilterItems.Count;
        //bool LinqMode => !string.IsNullOrEmpty(linqFilter);

        public override void OnNodesChanged()
        {
            //if (UpdateLocks == 0) TreeView?.Invalidate();
            //NodesChanged?.Invoke(this, new TreeModelEventArgs(null, new object[] { }));
        }

        public TabularUITree(TabularModelHandler handler) : base(handler) { }
        //public List<ITabularNamedObject> FilterItems = new List<ITabularNamedObject>();

        public virtual IEnumerable GetChildren(TreePath treePath)
        {
            if (UpdateLocks > 0) throw new InvalidOperationException("Tree enumeration attempted while update in progress");

            List<TabularNamedObject> items = new List<TabularNamedObject>();

            if (treePath.IsEmpty())
            {
                if (!string.IsNullOrEmpty(Filter) && FilterMode == FilterMode.Flat)
                {
                    return GetAllItems().Where(child => SatisfiesFilterCriteria(child));
                }
                else
                {
                    // If no root was specified, use the entire model
                    if (Options.HasFlag(LogicalTreeOptions.ShowRoot))
                        items.Add(Model);
                    else
                        return GetChildren(Model);
                }
            }
            else
            {
                var container = treePath.LastNode as ITabularObjectContainer;
                return (string.IsNullOrEmpty(Filter) || FilterMode == FilterMode.Flat) ? GetChildren(container) : GetChildrenFilteredLocal(container);
            }

            return items;
        }

        private IEnumerable GetChildrenFilteredLocal(ITabularObjectContainer container)
        {
            return GetChildren(container).Where(child => VisibleInTreeLocal(child));
        }

        private IEnumerable<ITabularNamedObject> GetAllItems()
        {
            yield return Model;
            foreach (var ds in Model.DataSources) yield return ds;
            foreach (var rel in Model.Relationships) yield return rel;
            foreach (var expr in Model.Expressions) yield return expr;
            foreach (var role in Model.Roles) yield return role;
            foreach (var table in Model.Tables) yield return table;
            foreach (var measure in Model.AllMeasures) yield return measure;
            foreach (var col in Model.AllColumns) yield return col;
            foreach (var hier in Model.AllHierarchies) yield return hier;
            foreach (var part in Model.AllPartitions) yield return part;
            foreach (var lev in Model.AllLevels) yield return lev;
            foreach (var trans in Model.Cultures) yield return trans;
            foreach (var persp in Model.Perspectives) yield return persp;
        }

        private bool SatisfiesFilterCriteria(ITabularNamedObject obj)
        {
            if (_useWildcardSearch) return obj.Name.ToUpperInvariant().EqualsWildcard(_filterUpper);
            else if (_useLinqSearch) return SatisfiesLinq(obj);
            else return obj.Name.ToUpperInvariant().Contains(_filterUpper);
        }

        private bool VisibleInTreeLocal(ITabularNamedObject obj)
        {
            switch(FilterMode)
            {
                case FilterMode.Flat:
                    return SatisfiesFilterCriteria(obj);

                case FilterMode.Parent:
                    // All table objects are shown if the table itself satisfies the criteria:
                    var tableObject = obj as ITabularTableObject;
                    if (tableObject != null && SatisfiesFilterCriteria(tableObject.Table)) return true;

                    // Measures, hierarchies and columns are only shown if any ancestor folder satisfies the criteria:
                    if (obj.ObjectType == ObjectType.Measure || obj.ObjectType == ObjectType.Hierarchy || obj.ObjectType == ObjectType.Column)
                        return (obj as IFolderObject).GetFolderStack(Culture).Any(f => SatisfiesFilterCriteria(f));

                    // Folders are shown when one of the following is true:
                    //  - The folder satisfies the criteria itself,
                    //  - Any ancestor folder satisfies the criteria
                    //  - The folder contains subfolders that satisfy the criteria
                    if (obj.ObjectType == ObjectType.Folder)
                        return SatisfiesFilterCriteria(obj)
                            || (obj as IFolderObject).GetFolderStack(Culture).Any(f => SatisfiesFilterCriteria(f))
                            || GetChildren(obj as ITabularObjectContainer).OfType<Folder>().Any(f => VisibleInTreeLocal(f));

                    // Parent objects are shown if they satisfy the filter criteria:
                    if (obj.ObjectType == ObjectType.Table)
                        return SatisfiesFilterCriteria(obj) || GetChildren(obj as ITabularObjectContainer).OfType<Folder>().Any(f => VisibleInTreeLocal(f));

                    // Group objects are shown if they have any visible children:
                    if (obj.ObjectType == ObjectType.Group) return GetChildren(obj as ITabularObjectContainer).Any(child => VisibleInTreeLocal(child));

                    // All other objects must satisfy the filter criteria:
                    return SatisfiesFilterCriteria(obj);

                case FilterMode.Child:
                    // Parent objects are shown if they contain visible children:
                    if (obj.ObjectType == ObjectType.Folder || obj.ObjectType == ObjectType.Table || obj.ObjectType == ObjectType.Group)
                        return GetChildren(obj as ITabularObjectContainer).Any(child => VisibleInTreeLocal(child));
                    
                    // Relationships are shown if a column at either end satisfies the criteria:
                    if (obj.ObjectType == ObjectType.Relationship)
                    {
                        var rel = obj as SingleColumnRelationship;
                        return (rel.FromColumn != null && SatisfiesFilterCriteria(rel.FromColumn)) || (rel.ToColumn != null && SatisfiesFilterCriteria(rel.ToColumn));
                    }

                    if (obj.ObjectType == ObjectType.PartitionCollection)
                        return GetChildren(obj as ITabularObjectContainer).Any(child => VisibleInTreeLocal(child));

                    // All other objects are shown if they satisfy the filter criteria:
                    return SatisfiesFilterCriteria(obj);
                default:
                    return true;
            }
        }

        private string _filterUpper;
        private string _filter;
        private string _linqFilter;
        private bool _useWildcardSearch;
        private bool _useLinqSearch;
        public string Filter
        {
            get { return _filter; }
            set {
                if (value == _filter) return;
                _filter = value;
                if(value != null)
                {
                    _filterUpper = _filter.ToUpperInvariant();
                    _useLinqSearch = _filter.StartsWith(":");
                    _linqFilter = _useLinqSearch ? _filter.Substring(1) : null;
                    _useWildcardSearch = !_useLinqSearch && (_filter.Contains('*') || _filter.Contains('?'));
                    if (_useLinqSearch) PrepareDynamicLinqLambdas();
                }
                else
                {
                    _filterUpper = null;
                    _linqFilter = null;
                    _useLinqSearch = false;
                    _useWildcardSearch = false;
                }
            }
        }
        public FilterMode FilterMode { get; set; } = FilterMode.Parent;

        /*private IEnumerable<ITabularNamedObject> Linq(IEnumerable collection, Type type)
        {
            try
            {
                var queryable = collection.AsQueryable();
                var lambda = System.Linq.Dynamic.DynamicExpression.ParseLambda(type, typeof(bool), linqFilter);
                return
                    queryable.Provider.CreateQuery(
                        Expression.Call(typeof(Queryable), "Where", new Type[] { type }, queryable.Expression, Expression.Quote(lambda))
                    ).OfType<ITabularNamedObject>();
            }
            catch { }
            return Enumerable.Empty<ITabularNamedObject>();
        }*/

        private Func<Model, bool> LF_Model;
        private Func<Table, bool> LF_Table;
        private Func<Measure, bool> LF_Measure;
        private Func<Hierarchy, bool> LF_Hierarchy;
        private Func<Level, bool> LF_Level;
        private Func<SingleColumnRelationship, bool> LF_Relationship;
        private Func<Perspective, bool> LF_Perspective;
        private Func<Culture, bool> LF_Culture;
        private Func<Partition, bool> LF_Partition;
        private Func<ProviderDataSource, bool> LF_ProviderDataSource;
        private Func<StructuredDataSource, bool> LF_StructuredDataSource;
        private Func<DataColumn, bool> LF_DataColumn;
        private Func<CalculatedColumn, bool> LF_CalculatedColumn;
        private Func<CalculatedTableColumn, bool> LF_CalculatedTableColumn;
        private Func<CalculatedTable, bool> LF_CalculatedTable;
        private Func<KPI, bool> LF_KPI;
        private Func<Variation, bool> LF_Variation;
        private Func<NamedExpression, bool> LF_NamedExpression;
        private Func<ModelRole, bool> LF_ModelRole;
        private Func<Folder, bool> LF_Folder;

        private void PrepareDynamicLinqLambda<T>(ref Func<T, bool> lf)
        {
                lf = null;
                try
                {
                    lf = System.Linq.Dynamic.DynamicExpression.ParseLambda<T, bool>(_linqFilter).Compile();
                }
                catch { }
        }
        

        private void PrepareDynamicLinqLambdas()
        {
            PrepareDynamicLinqLambda(ref LF_Model);
            PrepareDynamicLinqLambda(ref LF_Table);
            PrepareDynamicLinqLambda(ref LF_Measure);
            PrepareDynamicLinqLambda(ref LF_Hierarchy);
            PrepareDynamicLinqLambda(ref LF_Level);
            PrepareDynamicLinqLambda(ref LF_Relationship);
            PrepareDynamicLinqLambda(ref LF_Perspective);
            PrepareDynamicLinqLambda(ref LF_Culture);
            PrepareDynamicLinqLambda(ref LF_Partition);
            PrepareDynamicLinqLambda(ref LF_ProviderDataSource);
            PrepareDynamicLinqLambda(ref LF_DataColumn);
            PrepareDynamicLinqLambda(ref LF_CalculatedColumn);
            PrepareDynamicLinqLambda(ref LF_CalculatedTable);
            PrepareDynamicLinqLambda(ref LF_CalculatedTableColumn);
            PrepareDynamicLinqLambda(ref LF_KPI);
            PrepareDynamicLinqLambda(ref LF_StructuredDataSource);
            PrepareDynamicLinqLambda(ref LF_Variation);
            PrepareDynamicLinqLambda(ref LF_NamedExpression);
            PrepareDynamicLinqLambda(ref LF_ModelRole);
            PrepareDynamicLinqLambda(ref LF_Folder);
        }

        private bool SatisfiesLinq(ITabularNamedObject obj)
        {
            switch (obj.ObjectType)
            {
                case ObjectType.Model: return LF_Model == null ? false : LF_Model(obj as Model);
                case ObjectType.Table:
                    if (obj is CalculatedTable) return LF_CalculatedTable == null ? false : LF_CalculatedTable(obj as CalculatedTable);
                    if (obj is Table) return LF_Table == null ? false : LF_Table(obj as Table);
                    return false;
                case ObjectType.Measure: return LF_Measure == null ? false : LF_Measure(obj as Measure);
                case ObjectType.Hierarchy: return LF_Hierarchy == null ? false : LF_Hierarchy(obj as Hierarchy);
                case ObjectType.Level: return LF_Level == null ? false : LF_Level(obj as Level);
                case ObjectType.Relationship: return LF_Relationship == null ? false : LF_Relationship(obj as SingleColumnRelationship);
                case ObjectType.Perspective: return LF_Perspective == null ? false : LF_Perspective(obj as Perspective);
                case ObjectType.Culture: return LF_Culture == null ? false : LF_Culture(obj as Culture);
                case ObjectType.Partition: return LF_Partition == null ? false : LF_Partition(obj as Partition);
                case ObjectType.DataSource:
                    if (obj is ProviderDataSource) return LF_ProviderDataSource == null ? false : LF_ProviderDataSource(obj as ProviderDataSource);
                    if (obj is StructuredDataSource) return LF_StructuredDataSource == null ? false : LF_StructuredDataSource(obj as StructuredDataSource);
                    return false;
                case ObjectType.Column:
                    if (obj is DataColumn) return LF_DataColumn == null ? false : LF_DataColumn(obj as DataColumn);
                    if (obj is CalculatedColumn) return LF_CalculatedColumn == null ? false : LF_CalculatedColumn(obj as CalculatedColumn);
                    if (obj is CalculatedTableColumn) return LF_CalculatedTableColumn == null ? false : LF_CalculatedTableColumn(obj as CalculatedTableColumn);
                    return false;
                case ObjectType.KPI: return LF_KPI == null ? false : LF_KPI(obj as KPI);
                case ObjectType.Variation: return LF_Variation == null ? false : LF_Variation(obj as Variation);
                case ObjectType.Expression: return LF_NamedExpression == null ? false : LF_NamedExpression(obj as NamedExpression);
                case ObjectType.Role: return LF_ModelRole == null ? false : LF_ModelRole(obj as ModelRole);
                case ObjectType.Folder: return LF_Folder == null ? false : LF_Folder(obj as Folder);
                default:
                    return false;
            }

        }

        TreeDragInformation DragInfo;
        DropMode DragMode;
        private bool SetDropMode(DropMode mode)
        {
            DragMode = mode;
            return mode != DropMode.None;
        }


        #region Handling drag and drop
        /// <summary>
        /// Contains logic for determining if a drag/drop operation is legal. Additionally, specify doDrop = true to actually perform the drop (if it is legal).
        /// </summary>
        /// <param name="sourceNodes"></param>
        /// <param name="targetNode"></param>
        /// <param name="position"></param>
        /// <param name="doDrop"></param>
        /// <returns></returns>
        public virtual bool CanDrop(TreeNodeAdv[] sourceNodes, TreeNodeAdv targetNode, NodePosition position)
        {
            if (FilterMode == FilterMode.Flat || sourceNodes == null || sourceNodes.Length == 0) return false;
            DragInfo = TreeDragInformation.FromNodes(sourceNodes, targetNode, position);

            // Must not drop nodes on themselves or any of their children:
            if (sourceNodes.Contains(targetNode)) return false;
            if (sourceNodes.Any(n => targetNode.HasAncestor(n))) return false;

            // Drag operations that require the source and destination table to be the same:
            if (DragInfo.SameTable)
            {
                // Dragging foldered objects into or out of folders:
                if (sourceNodes.All(n => n.Tag is IFolderObject))
                {
                    if (targetNode.Tag is IFolder) return SetDropMode(DropMode.Folder);
                }

                // Dragging into a hierarchy:
                if (DragInfo.TargetHierarchy != null) {

                    // Dragging levels within a hierarchy or between hierarchies:
                    if (sourceNodes.All(n => n.Tag is Level))
                    {
                        return SetDropMode(DragInfo.SameHierarchy ? DropMode.ReorderLevels : DropMode.LevelMove);
                    }

                    // Dragging columns into a hierarchy:
                    if (sourceNodes.All(n => n.Tag is Column))
                    {
                        // Prevent drop if the hierarchy already contains the dragged column(s) as a level:
                        if (DragInfo.TargetHierarchy.Levels.Any(l => sourceNodes.Select(n => n.Tag as Column).Contains(l.Column))) return false;

                        return SetDropMode(DropMode.AddColumns);
                    }
                }
            } else
            {
                // Dragging measures and calculated columns between tables is also allowed:
                if (sourceNodes.All(n => n.Tag is CalculatedColumn || n.Tag is Measure))
                {
                    if (targetNode.Tag is Table && position == NodePosition.Inside) return SetDropMode(DropMode.MoveObject);
                }

            }

            // All other cases not allowed:
            return false;
        }

        public void DoDrop(TreeNodeAdv[] sourceNodes, TreeNodeAdv targetNode, NodePosition position)
        {
            if (!CanDrop(sourceNodes, targetNode, position)) throw new ArgumentException("Invalid drag drop operation.");

            switch(DragMode)
            {
                case DropMode.ReorderLevels: Handler.Actions.ReorderLevels(sourceNodes.Select(n => n.Tag as Level), DragInfo.TargetOrdinal); break;
                case DropMode.LevelMove: Handler.Actions.AddColumnsToHierarchy(sourceNodes.Select(n => (n.Tag as Level).Column), DragInfo.TargetHierarchy, DragInfo.TargetOrdinal); break;
                case DropMode.AddColumns: Handler.Actions.AddColumnsToHierarchy(sourceNodes.Select(n => n.Tag as Column), DragInfo.TargetHierarchy, DragInfo.TargetOrdinal); break;
                case DropMode.Folder: Handler.Actions.SetContainer(sourceNodes.Select(n => n.Tag as IFolderObject), targetNode.Tag as IFolder, Culture); break;
                case DropMode.MoveObject:
                    Handler.Actions.MoveObjects(sourceNodes.Select(n => n.Tag as IFolderObject), targetNode.Tag as Table);
                    break;
            }
        }

        #endregion

        public bool IsLeaf(TreePath treePath)
        {
            return !(treePath.LastNode is ITabularObjectContainer);
        }

        public event EventHandler<TreeModelEventArgs> NodesChanged;
        public event EventHandler<TreeModelEventArgs> NodesInserted;
        public event EventHandler<TreeModelEventArgs> NodesRemoved;
        public event EventHandler<TreePathEventArgs> StructureChanged;

        public TreePath GetPath(ITabularObject item)
        {
            if (item == null) return TreePath.Empty;

            var stack = new List<object>();

            stack.Add(Model);
            if (item is Partition)
            {
                stack.Add((item as Partition).Table.Partitions);
                stack.Add(item);
                return new TreePath(stack.ToArray());
            }

            if (Options.HasFlag(LogicalTreeOptions.AllObjectTypes) && item != Model)
            {

                // If "Show all object types" is enabled, we need to add the "group" of the item to the stack,
                // to get the complete path. The group can be determined from the type of object:
                switch (item.ObjectType)
                {
                    case ObjectType.Culture: stack.Add(Model.Groups.Translations); break;
                    case ObjectType.Role: stack.Add(Model.Groups.Roles); break;
                    case ObjectType.Perspective: stack.Add(Model.Groups.Perspectives); break;
                    case ObjectType.DataSource: stack.Add(Model.Groups.DataSources); break;
                    case ObjectType.Relationship: stack.Add(Model.Groups.Relationships); break;
                    default:
                        // All other object types should appear in the "Tables" group:
                        stack.Add(Model.Groups.Tables); break;
                }
            }
            if (item is Table)
            {
                stack.Add(item);
            }
            else if (item is ITabularTableObject)
            {
                stack.Add((item as ITabularTableObject).Table);

                var level = item as Level;
                if (level != null) item = level.Hierarchy;

                if (item is IFolderObject && Options.HasFlag(LogicalTreeOptions.DisplayFolders))
                {
                    var folderStack = new Stack<Folder>();
                    var folder = (item as IFolderObject).GetFolder(Culture);
                    while(folder != null && folder.Path != "")
                    {
                        folderStack.Push(folder);
                        folder = folder.GetFolder(Culture);
                    }
                    while(folderStack.Count > 0)
                    {
                        stack.Add(folderStack.Pop());
                    }
                }

                stack.Add(item);
                if (level != null) stack.Add(level);
            }
            return new TreePath(stack.ToArray());
        }

        private HashSet<ITabularObject> structureChangedItems = new HashSet<ITabularObject>();
        private int nodeChangedItemsCount = 0;

        public override void OnStructureChanged(ITabularNamedObject obj = null)
        {
            if (UpdateLocks > 0)
            {
                structureChangedItems.AddIfNotExists(obj);
                return;
            }
            else
            {
                if (obj == null)
                    OnStructureChanged(new TreePath());
                else
                    OnStructureChanged(GetPath(obj));
            }
        }

        public override void OnNodesRemoved(ITabularObject parent, params ITabularObject[] children)
        {
            if (UpdateLocks > 0)
            {
                structureChangedItems.AddIfNotExists(parent);
                return;
            }
            else
            {
                if (!string.IsNullOrEmpty(Filter) && FilterMode == FilterMode.Flat)
                {
                    StructureChanged?.Invoke(this, new TreePathEventArgs());
                }
                else
                    NodesRemoved?.Invoke(this, new TreeModelEventArgs(GetPath(parent), children));
            }
        }

        public override void OnNodesInserted(ITabularObject parent, params ITabularObject[] children)
        {
            if (UpdateLocks > 0)
            {
                structureChangedItems.AddIfNotExists(parent);
                return;
            }
            else
            {
                if (!string.IsNullOrEmpty(Filter) && FilterMode == FilterMode.Flat)
                {
                    StructureChanged?.Invoke(this, new TreePathEventArgs());
                }
                else
                    NodesInserted?.Invoke(this, new TreeModelEventArgs(GetPath(parent), children));
            }
        }

        public override void OnNodesChanged(ITabularObject nodeItem)
        {
            if (UpdateLocks > 0)
            {
                nodeChangedItemsCount++;
                return;
            }
            else
            {
                var path = GetPath(nodeItem);
                NodesChanged?.Invoke(this, new TreeModelEventArgs(path, new object[] { }));
            }
        }


        public void OnStructureChanged(TreePath path)
        {
            if (UpdateLocks > 0)
            {
                structureChangedItems.AddIfNotExists(path.LastNode as ITabularObject);
                return;
            }
            else
            {
                if(!string.IsNullOrEmpty(Filter) && FilterMode == FilterMode.Flat)
                {
                    StructureChanged?.Invoke(this, new TreePathEventArgs());
                }
                else
                    StructureChanged?.Invoke(this, new TreePathEventArgs(path));
            }

        }

        public override void EndUpdate()
        {
            base.EndUpdate();

            if (UpdateLocks > 0) return;

            if (structureChangedItems.Count > 0)
            {
                if (structureChangedItems.Count == 1) OnStructureChanged(GetPath(structureChangedItems.First()));
                else OnStructureChanged();

                structureChangedItems.Clear();
            }

            if (nodeChangedItemsCount > 0)
            {
                OnNodesChanged();
                nodeChangedItemsCount = 0;
            }
        }
    }

    internal enum DropMode
    {
        Folder,
        ReorderLevels,
        LevelMove,
        AddColumns,
        MoveObject,
        None
    }

    internal class TreeDragInformation
    {
        public static TreeDragInformation FromNodes(TreeNodeAdv[] sourceNodes, TreeNodeAdv targetNode, NodePosition position)
        {
            var dragInfo = new TreeDragInformation();

            dragInfo.SourceTable = (sourceNodes.First().Tag as ITabularTableObject)?.Table;
            dragInfo.TargetTable = (targetNode.Tag as ITabularTableObject)?.Table ?? (position == NodePosition.Inside ? targetNode.Tag as Table : null);

            dragInfo.SourceHierarchy = (sourceNodes.First().Tag as Level)?.Hierarchy;
            dragInfo.TargetHierarchy = (targetNode.Tag as Level)?.Hierarchy ?? (position == NodePosition.Inside ? targetNode.Tag as Hierarchy : null);

            //dragInfo.TargetFolder = position == NodePosition.Inside ? targetNode.Tag as IDetailObjectContainer : (targetNode.Tag as IDetailObject)?.GetContainer();
            dragInfo.TargetLevel = targetNode.Tag as Level;

            if (dragInfo.TargetLevel != null)
            {
                dragInfo.TargetOrdinal = dragInfo.TargetLevel.Ordinal;
                if (position == NodePosition.After) dragInfo.TargetOrdinal++;
            }
            else if (dragInfo.TargetHierarchy != null)
            {
                dragInfo.TargetOrdinal = dragInfo.TargetHierarchy.Levels.Count;
            }

            return dragInfo;
        }
        
        public int TargetOrdinal = -1;
        public Table SourceTable;
        public Table TargetTable;
        public Hierarchy SourceHierarchy;
        public Hierarchy TargetHierarchy;
        public Level TargetLevel;

        public bool SameTable { get
            {
                return TargetTable != null && SourceTable == TargetTable;
            }
        }

        public bool SameHierarchy { get
            {
                return TargetHierarchy != null && SourceHierarchy == TargetHierarchy;
            }
        }
    }

    public static class CollectionHelper
    {
        public static void AddIfNotExists<T>(this ICollection<T> coll, T item)
        {
            if (!coll.Contains(item))
                coll.Add(item);
        }
    }
}
