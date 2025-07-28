using System.Collections.Generic;
using System;

namespace SkillzBot.Utils
{
    public class TrieNode
    {
        public Dictionary<char, TrieNode> Children = new Dictionary<char, TrieNode>();
        public bool IsEndOfWord = false;
        public string Word = null;
    }

    public class BannedWordsTrie
    {
        private readonly TrieNode root = new TrieNode();

        public void BuildTrie(HashSet<string> dictionary)
        {
            Console.WriteLine("Building Trie for fast word matching...");
            foreach (var word in dictionary)
            {
                InsertWord(word.ToLower());
            }
            Console.WriteLine("Trie built successfully!");
        }

        private void InsertWord(string word)
        {
            var current = root;
            foreach (char c in word)
            {
                if (!current.Children.ContainsKey(c))
                    current.Children[c] = new TrieNode();
                current = current.Children[c];
            }
            current.IsEndOfWord = true;
            current.Word = word;
        }

        public string FindBannedWord(string text)
        {
            text = text.ToLower();
            for (int i = 0; i < text.Length; i++)
            {
                var result = SearchFromPosition(text, i);
                if (result != null)
                    return result;
            }

            return null;
        }

        private string SearchFromPosition(string text, int startPos)
        {
            var current = root;

            for (int i = startPos; i < text.Length; i++)
            {
                char c = text[i];

                if (!current.Children.ContainsKey(c))
                    break;

                current = current.Children[c];

                if (current.IsEndOfWord)
                    return current.Word;
            }
            return null;
        }
    }
}
