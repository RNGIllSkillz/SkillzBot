using System.Collections.Generic;

namespace SkillzBot.Utils
{
    public class AhoCorasick
    {
        private class Node
        {
            public Dictionary<char, Node> Children = new Dictionary<char, Node>();
            public Node Failure; // Link to the longest proper suffix
            public bool IsEndOfPattern;
        }

        private readonly Node _root = new Node();

        public void AddPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return;

            var node = _root;
            foreach (var c in pattern)
            {
                if (!node.Children.TryGetValue(c, out var child))
                {
                    child = new Node();
                    node.Children[c] = child;
                }
                node = child;
            }
            node.IsEndOfPattern = true;
        }

        public void Build()
        {
            var queue = new Queue<Node>();
            _root.Failure = _root;

            // Initialize depth 1
            foreach (var child in _root.Children.Values)
            {
                child.Failure = _root;
                queue.Enqueue(child);
            }

            // BFS to build failure links
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var kvp in current.Children)
                {
                    char key = kvp.Key;
                    var child = kvp.Value;
                    var tempFailure = current.Failure;

                    while (tempFailure != _root && !tempFailure.Children.ContainsKey(key))
                    {
                        tempFailure = tempFailure.Failure;
                    }

                    if (tempFailure.Children.TryGetValue(key, out var failureNode))
                    {
                        child.Failure = failureNode;
                    }
                    else
                    {
                        child.Failure = _root;
                    }

                    // Optimization: If failure node is end of pattern, this node is also a match
                    if (child.Failure.IsEndOfPattern)
                    {
                        child.IsEndOfPattern = true;
                    }

                    queue.Enqueue(child);
                }
            }
        }

        public bool ContainsAny(string text)
        {
            var node = _root;
            foreach (var c in text)
            {
                while (node != _root && !node.Children.ContainsKey(c))
                {
                    node = node.Failure;
                }

                if (node.Children.TryGetValue(c, out var nextNode))
                {
                    node = nextNode;
                }

                if (node.IsEndOfPattern)
                {
                    return true; // Match found
                }
            }
            return false;
        }
    }
}