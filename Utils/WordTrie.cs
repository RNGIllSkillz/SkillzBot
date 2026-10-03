using System;
using System.Collections.Generic;

namespace SkillzBot.Utils
{
    /// <summary>
    /// Prefix tree over already-normalized words. Supports earliest-match and
    /// longest-match-at-position queries used by the profanity detector.
    /// </summary>
    public sealed class WordTrie
    {
        private sealed class Node
        {
            public Dictionary<char, Node> Children;
            public string Word; // non-null marks the end of a word; holds the original spelling
        }

        private readonly Node _root = new Node();
        public int Count { get; private set; }
        public int MaxWordLength { get; private set; }

        /// <param name="normalized">Normalized form used for matching.</param>
        /// <param name="original">Original spelling reported back on a hit.</param>
        public void Insert(string normalized, string original = null)
        {
            if (string.IsNullOrEmpty(normalized)) return;
            var node = _root;
            foreach (char c in normalized)
            {
                node.Children ??= new Dictionary<char, Node>();
                if (!node.Children.TryGetValue(c, out var child))
                {
                    child = new Node();
                    node.Children[c] = child;
                }
                node = child;
            }
            if (node.Word == null)
            {
                Count++;
                if (normalized.Length > MaxWordLength) MaxWordLength = normalized.Length;
            }
            node.Word ??= original ?? normalized;
        }

        /// <summary>
        /// Finds the earliest word occurrence whose start index is at or after <paramref name="from"/>.
        /// At a given start position the shortest word wins.
        /// </summary>
        public bool FindFirst(ReadOnlySpan<char> text, int from, out int index, out int length, out string word)
        {
            for (int i = Math.Max(0, from); i < text.Length; i++)
            {
                var node = _root;
                for (int j = i; j < text.Length; j++)
                {
                    if (node.Children == null || !node.Children.TryGetValue(text[j], out node)) break;
                    if (node.Word != null)
                    {
                        index = i;
                        length = j - i + 1;
                        word = node.Word;
                        return true;
                    }
                }
            }
            index = -1;
            length = 0;
            word = null;
            return false;
        }

        /// <summary>
        /// Length of the longest word starting exactly at <paramref name="start"/> and ending
        /// before <paramref name="end"/>, or 0 when none matches.
        /// </summary>
        public int LongestMatchAt(ReadOnlySpan<char> text, int start, int end)
        {
            int best = 0;
            var node = _root;
            for (int j = start; j < end; j++)
            {
                if (node.Children == null || !node.Children.TryGetValue(text[j], out node)) break;
                if (node.Word != null) best = j - start + 1;
            }
            return best;
        }
    }
}
