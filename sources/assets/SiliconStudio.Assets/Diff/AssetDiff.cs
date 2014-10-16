﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.Collections.Generic;
using System.Linq;
using SharpDiff;
using SiliconStudio.Assets.Visitors;
using SiliconStudio.Core.Reflection;

namespace SiliconStudio.Assets.Diff
{
    /// <summary>
    /// Class AssetDiff. This class cannot be inherited.
    /// </summary>
    public sealed class AssetDiff
    {
        private readonly static List<DataVisitNode> EmptyNodes = new List<DataVisitNode>();

        private readonly Asset baseAsset;
        private readonly Asset asset1;
        private readonly Asset asset2;
        private readonly NodeEqualityComparer equalityComparer;
        private Diff3Node computed;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssetDiff"/> class.
        /// </summary>
        /// <param name="baseAsset">The base asset.</param>
        /// <param name="asset1">The asset1.</param>
        /// <param name="asset2">The asset2.</param>
        public AssetDiff(Asset baseAsset, Asset asset1, Asset asset2)
        {
            // TODO handle some null values (no asset2....etc.)
            this.baseAsset = baseAsset;
            this.asset1 = asset1;
            this.asset2 = asset2;
            this.equalityComparer = new NodeEqualityComparer(this);
        }

        public Asset BaseAsset
        {
            get
            {
                return baseAsset;
            }
        }

        public Asset Asset1
        {
            get
            {
                return asset1;
            }
        }

        public Asset Asset2
        {
            get
            {
                return asset2;
            }
        }

        public void Reset()
        {
            computed = null;
        }

        public static Diff3Node Compute(Asset baseAsset, Asset asset1, Asset asset2)
        {
            var diff3 = new AssetDiff(baseAsset, asset1, asset2);
            return diff3.Compute();
        }

        /// <summary>
        /// Computes the diff3 between <see cref="BaseAsset" />, <see cref="Asset1" /> and <see cref="Asset2" />.
        /// </summary>
        /// <param name="forceRecompute">if set to <c>true</c> force to recompute the diff.</param>
        /// <returns>The result of the diff. This result is cached so next call will return it directly.</returns>
        public Diff3Node Compute(bool forceRecompute = false)
        {
            if (computed != null && !forceRecompute)
            {
                return computed;
            }
            var baseNodes = DataVisitNodeBuilder.Run(TypeDescriptorFactory.Default, baseAsset);
            var asset1Nodes = DataVisitNodeBuilder.Run(TypeDescriptorFactory.Default, asset1);
            var asset2Nodes = DataVisitNodeBuilder.Run(TypeDescriptorFactory.Default, asset2);
            computed =  DiffNode(baseNodes, asset1Nodes, asset2Nodes);
            return computed;
        }

        private Diff3Node DiffNode(DataVisitNode baseNode, DataVisitNode asset1Node, DataVisitNode asset2Node)
        {
            var diff3 = new Diff3Node(baseNode, asset1Node, asset2Node);

            var baseNodeDesc = GetNodeDescription(baseNode);
            var asset1NodeDesc = GetNodeDescription(asset1Node);
            var asset2NodeDesc = GetNodeDescription(asset2Node);

            bool hasMembers = false;

            Type type = null;
            Type nodeType = null;
            if (baseNodeDesc.Type != null)
            {
                type = baseNodeDesc.Type;
                hasMembers = baseNode.HasMembers;
                nodeType = baseNode.GetType();
            }

            if (asset1NodeDesc.Type != null)
            {
                if (type == null)
                {
                    type = asset1NodeDesc.Type;
                    hasMembers = asset1Node.HasMembers;
                    nodeType = asset1Node.GetType();
                }
                else
                {
                    if (nodeType != asset1Node.GetType())
                    {
                        diff3.ChangeType = Diff3ChangeType.InvalidNodeType;
                        return diff3;
                    } 

                    if (type != asset1NodeDesc.Type)
                    {
                        diff3.ChangeType = Diff3ChangeType.ConflictType;
                        return diff3;
                    }
                }
            }

            if (asset2NodeDesc.Type != null)
            {
                if (type == null)
                {
                    type = asset2NodeDesc.Type;
                    hasMembers = asset2Node.HasMembers;
                }
                else
                {
                    if (nodeType != asset2Node.GetType())
                    {
                        diff3.ChangeType = Diff3ChangeType.InvalidNodeType;
                        return diff3;
                    }

                    if (type != asset2NodeDesc.Type)
                    {
                        diff3.ChangeType = Diff3ChangeType.ConflictType;
                        return diff3;
                    }
                }
            }

            if (type == null)
            {
                return diff3;
            }

            diff3.InstanceType = type;

            // A comparable type doesn't have any members, is not a collection or dictionary or array.
            bool isComparableType = !hasMembers && !CollectionDescriptor.IsCollection(type) && !DictionaryDescriptor.IsDictionary(type) && !type.IsArray;
            if (isComparableType)
            {
                DiffValue(diff3, ref baseNodeDesc, ref asset1NodeDesc, ref asset2NodeDesc);
                return diff3;
            }

            // Diff members
            DiffMembers(diff3, baseNode, asset1Node, asset2Node);

            if (DictionaryDescriptor.IsDictionary(type))
            {
                DiffDictionary(diff3, baseNode, asset1Node, asset2Node);
            }
            else if (CollectionDescriptor.IsCollection(type))
            {
                DiffCollection(diff3, baseNode, asset1Node, asset2Node);
            }
            else if (type.IsArray)
            {
                DiffArray(diff3, baseNode, asset1Node, asset2Node);
            }

            return diff3;
        }

        private static void DiffValue(Diff3Node diff3, ref NodeDescription baseNodeDesc, ref NodeDescription asset1NodeDesc, ref NodeDescription asset2NodeDesc)
        {
            var baseAsset1Equals = Equals(baseNodeDesc.Instance, asset1NodeDesc.Instance);
            var baseAsset2Equals = Equals(baseNodeDesc.Instance, asset2NodeDesc.Instance);
            var asset1And2Equals = Equals(asset1NodeDesc.Instance, asset2NodeDesc.Instance);

            diff3.ChangeType = baseAsset1Equals && baseAsset2Equals
                ? Diff3ChangeType.None
                : baseAsset2Equals ? Diff3ChangeType.MergeFromAsset1 : baseAsset1Equals ? Diff3ChangeType.MergeFromAsset2 : asset1And2Equals ? Diff3ChangeType.MergeFromAsset1And2 : Diff3ChangeType.Conflict;
        }

        private void DiffMembers(Diff3Node diff3, DataVisitNode baseNode, DataVisitNode asset1Node, DataVisitNode asset2Node)
        {
            var baseMembers = baseNode != null ? baseNode.Members : null;
            var asset1Members = asset1Node != null ? asset1Node.Members : null;
            var asset2Members = asset2Node != null ? asset2Node.Members : null;
            int memberCount = 0;

            if (baseMembers != null) memberCount = baseMembers.Count;
            else if (asset1Members != null) memberCount = asset1Members.Count;
            else if (asset2Members != null) memberCount = asset2Members.Count;

            for (int i = 0; i < memberCount; i++)
            {
                AddMember(diff3, DiffNode(baseMembers == null ? null : baseMembers[i],
                    asset1Members == null ? null : asset1Members[i],
                    asset2Members == null ? null : asset2Members[i]));
            }
        }

        private void DiffCollection(Diff3Node diff3, DataVisitNode baseNode, DataVisitNode asset1Node, DataVisitNode asset2Node)
        {
            var baseItems = baseNode != null ? baseNode.Items ?? EmptyNodes : EmptyNodes;
            var asset1Items = asset1Node != null ? asset1Node.Items ?? EmptyNodes : EmptyNodes;
            var asset2Items = asset2Node != null ? asset2Node.Items ?? EmptyNodes : EmptyNodes;
            
            equalityComparer.Reset();
            var changes = Diff3.Compare(baseItems, asset1Items, asset2Items, equalityComparer);
            foreach (var change in changes)
            {
                switch (change.ChangeType)
                {
                    case SharpDiff.Diff3ChangeType.Equal:
                        for (int i = 0; i < change.Base.Length; i++)
                        {
                            var diff3Node = new Diff3Node(baseItems[change.Base.From + i], asset1Items[change.From1.From + i], asset2Items[change.From2.From + i]) { ChangeType = Diff3ChangeType.None };
                            AddItem(diff3, diff3Node);
                        }
                        break;

                    case SharpDiff.Diff3ChangeType.MergeFrom1:
                        for (int i = 0; i < change.From1.Length; i++)
                        {
                            var diff3Node = new Diff3Node(null, asset1Items[change.From1.From + i], null) { ChangeType = Diff3ChangeType.MergeFromAsset1 };
                            AddItem(diff3, diff3Node);
                        }
                        break;

                    case SharpDiff.Diff3ChangeType.MergeFrom2:
                        for (int i = 0; i < change.From2.Length; i++)
                        {
                            var diff3Node = new Diff3Node(null, null, asset2Items[change.From2.From + i]) { ChangeType = Diff3ChangeType.MergeFromAsset2 };
                            AddItem(diff3, diff3Node);
                        }
                        break;

                    case SharpDiff.Diff3ChangeType.MergeFrom1And2:
                        for (int i = 0; i < change.From2.Length; i++)
                        {
                            var diff3Node = new Diff3Node(null, asset1Items[change.From1.From + i], asset2Items[change.From2.From + i]) { ChangeType = Diff3ChangeType.MergeFromAsset1And2 };
                            AddItem(diff3, diff3Node);
                        }
                        break;

                    case SharpDiff.Diff3ChangeType.Conflict:
                        int baseIndex = change.Base.IsValid ? change.Base.From : -1;
                        int from1Index = change.From1.IsValid ? change.From1.From : -1;
                        int from2Index = change.From2.IsValid ? change.From2.From : -1;

                        // If there are changes only from 1 or 2 or base.Length == list1.Length == list2.Length, then try to make a diff per item
                        // else output the conflict as a full conflict
                        bool tryResolveConflict = false;
                        if (baseIndex >= 0)
                        {
                            if (from1Index >= 0 && from2Index >= 0)
                            {
                                if ((change.Base.Length == change.From1.Length && change.Base.Length == change.From2.Length)
                                    || (change.From1.Length == change.From2.Length))
                                {
                                    tryResolveConflict = true;
                                }
                            }
                            else if (from1Index >= 0)
                            {
                                tryResolveConflict = change.Base.Length == change.From1.Length;
                            }
                            else if (from2Index >= 0)
                            {
                                tryResolveConflict = change.Base.Length == change.From2.Length;
                            }
                            else
                            {
                                tryResolveConflict = true;
                            }
                        }

                        // Iterate on items
                        while ((baseIndex >= 0 && baseItems.Count > 0) || (from1Index >= 0 && asset1Items.Count > 0) || (from2Index >= 0 && asset2Items.Count > 0))
                        {
                            var diff3Node = tryResolveConflict ? 
                                DiffNode(GetSafeFromList(baseItems, ref baseIndex), GetSafeFromList(asset1Items, ref from1Index), GetSafeFromList(asset2Items, ref from2Index)) : 
                                new Diff3Node(GetSafeFromList(baseItems, ref baseIndex), GetSafeFromList(asset1Items, ref from1Index), GetSafeFromList(asset2Items, ref from2Index)) { ChangeType = Diff3ChangeType.Conflict };
                            AddItem(diff3, diff3Node);
                        }
                        break;
                }
            }

            // Order by descending index
            if (diff3.Items != null)
            {
                diff3.Items.Sort((left, right) =>
                {
                    int leftAsset1Index = left.Asset1Node != null ? ((DataVisitListItem)left.Asset1Node).Index : -1;
                    int rightAsset1Index = right.Asset1Node != null ? ((DataVisitListItem)right.Asset1Node).Index : -1;

                    return rightAsset1Index.CompareTo(leftAsset1Index);
                });
            }
        }

        private static DataVisitNode GetSafeFromList(List<DataVisitNode> nodes, ref int index)
        {
            if (nodes == null || index < 0) return null;
            if (index >= nodes.Count)
            {
                index = -1;
                return null;
            }
            var value = nodes[index];
            index++;
            if (index >= nodes.Count) index = -1;
            return value;
        }

        private void DiffDictionary(Diff3Node diff3, DataVisitNode baseNode, DataVisitNode asset1Node, DataVisitNode asset2Node)
        {
            var baseItems = baseNode != null ? baseNode.Items : null;
            var asset1Items = asset1Node != null ? asset1Node.Items : null;
            var asset2Items = asset2Node != null ? asset2Node.Items : null;

            // Build dictionary: key => base, v1, v2
            var keyNodes = new Dictionary<object, Diff3DictionaryItem>();
            Diff3DictionaryItem diff3Item;
            if (baseItems != null)
            {
                foreach (var dataVisitNode in baseItems.OfType<DataVisitDictionaryItem>())
                {
                    keyNodes.Add(dataVisitNode.Key, new Diff3DictionaryItem() { Base = dataVisitNode });
                }
            }
            if (asset1Items != null)
            {
                foreach (var dataVisitNode in asset1Items.OfType<DataVisitDictionaryItem>())
                {
                    keyNodes.TryGetValue(dataVisitNode.Key, out diff3Item);
                    diff3Item.Asset1 = dataVisitNode;
                    keyNodes[dataVisitNode.Key] = diff3Item;
                }
            }
            if (asset2Items != null)
            {
                foreach (var dataVisitNode in asset2Items.OfType<DataVisitDictionaryItem>())
                {
                    keyNodes.TryGetValue(dataVisitNode.Key, out diff3Item);
                    diff3Item.Asset2 = dataVisitNode;
                    keyNodes[dataVisitNode.Key] = diff3Item;
                }
            }

            // Perform merge on dictionary
            foreach (var keyNode in keyNodes)
            {
                var valueNode = keyNode.Value;

                Diff3Node diffValue;

                //  base     v1      v2     action
                //  ----     --      --     ------
                //   a        b       c     Diff(a,b,c)
                //  null      b       c     Diff(null, b, c)
                if (valueNode.Asset1 != null && valueNode.Asset2 != null)
                {
                    diffValue = DiffNode(valueNode.Base, valueNode.Asset1, valueNode.Asset2);
                }
                else if (valueNode.Asset1 == null)
                {
                    //   a       null     c     MergeFrom1 (unchanged)
                    //  null     null     c     MergeFrom2
                    //   a       null    null   MergeFrom1 (unchanged)
                    diffValue = new Diff3Node(valueNode.Base, null, valueNode.Asset2) { ChangeType = valueNode.Base == null ? Diff3ChangeType.MergeFromAsset2 : Diff3ChangeType.MergeFromAsset1 };
                }
                else
                {
                    //   a        b      null   Conflict
                    //  null      b      null   MergeFrom1 (unchanged)
                    diffValue = new Diff3Node(valueNode.Base, valueNode.Asset1, null) { ChangeType = valueNode.Base == null ? Diff3ChangeType.MergeFromAsset1 : Diff3ChangeType.Conflict };
                }

                AddItem(diff3, diffValue);
            }
        }

        private void DiffArray(Diff3Node diff3, DataVisitNode baseNode, DataVisitNode asset1Node, DataVisitNode asset2Node)
        {
            var baseItems = baseNode != null ? baseNode.Items : null;
            var asset1Items = asset1Node != null ? asset1Node.Items : null;
            var asset2Items = asset2Node != null ? asset2Node.Items : null;
            int itemCount = -1;

            if (baseItems != null)
            {
                itemCount = baseItems.Count;
            }

            if (asset1Items != null)
            {
                var newLength = asset1Items.Count;
                if (itemCount >= 0 && itemCount != newLength)
                {
                    diff3.ChangeType = Diff3ChangeType.ConflictArraySize;
                    return;
                }
                itemCount = newLength;
            }

            if (asset2Items != null)
            {
                var newLength = asset2Items.Count;
                if (itemCount >= 0 && itemCount != newLength)
                {
                    diff3.ChangeType = Diff3ChangeType.ConflictArraySize;
                    return;
                }
                itemCount = newLength;
            }

            for (int i = 0; i < itemCount; i++)
            {
                AddItem(diff3, DiffNode(baseItems == null ? null : baseItems[i],
                    asset1Items == null ? null : asset1Items[i],
                    asset2Items == null ? null : asset2Items[i]));
            }
        }


        /// <summary>
        /// Adds a member to this instance.
        /// </summary>
        /// <param name="thisObject">The this object.</param>
        /// <param name="member">The member.</param>
        /// <exception cref="System.ArgumentNullException">member</exception>
        private static void AddMember(Diff3Node thisObject, Diff3Node member)
        {
            if (member == null) throw new ArgumentNullException("member");
            if (thisObject.Members == null)
                thisObject.Members = new List<Diff3Node>();

            member.Parent = thisObject;
            if (member.ChangeType != Diff3ChangeType.None)
            {
                thisObject.ChangeType = Diff3ChangeType.Children;
            }
            thisObject.Members.Add(member);
        }

        /// <summary>
        /// Adds an item (array, list or dictionary item) to this instance.
        /// </summary>
        /// <param name="thisObject">The this object.</param>
        /// <param name="item">The item.</param>
        /// <exception cref="System.ArgumentNullException">item</exception>
        private static void AddItem(Diff3Node thisObject, Diff3Node item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (thisObject.Items == null)
                thisObject.Items = new List<Diff3Node>();

            item.Parent = thisObject;
            if (item.ChangeType != Diff3ChangeType.None)
            {
                thisObject.ChangeType = Diff3ChangeType.Children;
            }
            thisObject.Items.Add(item);
        }

        private NodeDescription GetNodeDescription(DataVisitNode node)
        {
            if (node == null)
            {
                return new NodeDescription();
            }

            var instanceType = node.InstanceType;
            if (NullableDescriptor.IsNullable(instanceType))
            {
                instanceType = Nullable.GetUnderlyingType(instanceType);
            }

            return new NodeDescription(node.Instance, instanceType);
        }

        private struct NodeDescription
        {
            public NodeDescription(object instance, Type type)
            {
                Instance = instance;
                Type = type;
            }

            public readonly object Instance;

            public readonly Type Type;
        }

        private struct Diff3DictionaryItem
        {
            public DataVisitDictionaryItem Base;

            public DataVisitDictionaryItem Asset1;

            public DataVisitDictionaryItem Asset2;
        }

        private class NodeEqualityComparer : IEqualityComparer<DataVisitNode>
        {
            private Dictionary<KeyComparison, bool> equalityCache = new Dictionary<KeyComparison, bool>();
            private AssetDiff diffManager;

            public NodeEqualityComparer(AssetDiff diffManager)
            {
                if (diffManager == null) throw new ArgumentNullException("diffManager");
                this.diffManager = diffManager;
            }

            public void Reset()
            {
                equalityCache.Clear();
            }

            public bool Equals(DataVisitNode x, DataVisitNode y)
            {
                var key = new KeyComparison(x, y);
                bool result;
                if (equalityCache.TryGetValue(key, out result))
                {
                    return result;
                }

                var diff3 = diffManager.DiffNode(x, y, null);
                result = !diff3.FindDifferences().Any();
                equalityCache.Add(key, result);
                return result;
            }

            public int GetHashCode(DataVisitNode obj)
            {
                return obj == null ? 0 : obj.GetHashCode();
            }

            private struct KeyComparison : IEquatable<KeyComparison>
            {
                public KeyComparison(DataVisitNode node1, DataVisitNode node2)
                {
                    Node1 = node1;
                    Node2 = node2;
                }

                public readonly DataVisitNode Node1;

                public readonly DataVisitNode Node2;


                public bool Equals(KeyComparison other)
                {
                    return ReferenceEquals(Node1, other.Node1) && ReferenceEquals(Node2, other.Node2);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    return obj is KeyComparison && Equals((KeyComparison)obj);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        return ((Node1 != null ? Node1.GetHashCode() : 0) * 397) ^ (Node2 != null ? Node2.GetHashCode() : 0);
                    }
                }

                public static bool operator ==(KeyComparison left, KeyComparison right)
                {
                    return left.Equals(right);
                }

                public static bool operator !=(KeyComparison left, KeyComparison right)
                {
                    return !left.Equals(right);
                }
            }
        }
    }
}