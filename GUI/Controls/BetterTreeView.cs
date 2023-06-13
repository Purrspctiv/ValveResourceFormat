using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;
using SteamDatabase.ValvePak;

namespace GUI.Controls
{
    /// <summary>
    /// Represents a TreeView with the ability to have its contents searched.
    /// </summary>
    partial class BetterTreeView : TreeView
    {
        private Dictionary<string, string> ExtensionIconList;

        public VrfGuiContext VrfGuiContext { get; set; }

        public BetterTreeView()
        {
            InitializeComponent();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
        }

        /// <summary>
        /// Performs a breadth-first-search on the TreeView's nodes in search of the passed value. The matching conditions are based on the passed search type parameter.
        /// </summary>
        /// <param name="value">Value to search for in the TreeView. Matching on this value is based on the search type.</param>
        /// <param name="searchType">Determines the matching of the value. For example, full/partial text search or full path search.</param>
        /// <returns>A collection of nodes who match the conditions based on the search type.</returns>
        public IList<BetterTreeNode> Search(string value, SearchType searchType)
        {
            IList<BetterTreeNode> results = new List<BetterTreeNode>();

            if (searchType == SearchType.FileNamePartialMatch || searchType == SearchType.FullPath)
            {
                value = value.ToUpperInvariant().Replace('\\', Package.DirectorySeparatorChar);
            }

            // If only file name search is selected, but entered text contains a slash, search full path
            if (searchType == SearchType.FileNamePartialMatch && value.Contains(Package.DirectorySeparatorChar, StringComparison.InvariantCulture))
            {
                searchType = SearchType.FullPath;
            }

            if (searchType == SearchType.FileNameExactMatch)
            {
                results = Nodes.Find(value, true).Cast<BetterTreeNode>().ToList();
            }
            else if (searchType == SearchType.FileNamePartialMatch)
            {
                bool MatchFunction(BetterTreeNode node) => node.Text.Contains(value, StringComparison.InvariantCultureIgnoreCase);
                results = Search(MatchFunction);
            }
            else if (searchType == SearchType.FullPath)
            {
                bool MatchFunction(BetterTreeNode node) => node.FullPath.Contains(value, StringComparison.InvariantCultureIgnoreCase);
                results = Search(MatchFunction);
            }
            else if (searchType == SearchType.Regex)
            {
                var regex = new Regex(value, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

                bool MatchFunction(BetterTreeNode node) => regex.IsMatch(node.Text);
                results = Search(MatchFunction);
            }
            else if (searchType == SearchType.FileContents)
            {
                results = SearchFileContents(Encoding.UTF8.GetBytes(value));
            }
            else if (searchType == SearchType.FileContentsHex)
            {
                // TODO: Optimize this
                value = value.Replace(" ", "", StringComparison.Ordinal);

                var bytes = Enumerable.Range(0, value.Length)
                     .Where(x => x % 2 == 0)
                     .Select(x => Convert.ToByte(value.Substring(x, 2), 16))
                     .ToArray();

                results = SearchFileContents(bytes);
            }

            return results;
        }

        /// <summary>
        /// Performs a breadth-first-search on the TreeView's nodes in search of the passed value. The matching conditions are based the passed function.
        /// </summary>
        /// <param name="matchFunction">Function which performs matching on the TreeNode. Returns true if there's a match.</param>
        /// <returns>Returns matched nodes.</returns>
        private IList<BetterTreeNode> Search(Func<BetterTreeNode, bool> matchFunction)
        {
            var searchQueue = new Queue<BetterTreeNode>();

            // queue up every child of the root to begin the search
            foreach (BetterTreeNode childNode in Nodes)
            {
                searchQueue.Enqueue(childNode);
            }

            var matchedNodes = new List<BetterTreeNode>();

            // while there are items in the queue to search
            while (searchQueue.Count > 0)
            {
                var currentNode = searchQueue.Dequeue();

                // if our match function is true, add the node to our matches
                if (matchFunction(currentNode))
                {
                    matchedNodes.Add(currentNode);
                }

                // if the node being inspected has children, queue them all up
                if (currentNode.Nodes.Count < 1)
                {
                    continue;
                }

                foreach (BetterTreeNode childNode in currentNode.Nodes)
                {
                    searchQueue.Enqueue(childNode);
                }
            }

            return matchedNodes.AsReadOnly();
        }

        private IList<BetterTreeNode> SearchFileContents(byte[] pattern)
        {
            if (pattern.Length < 3)
            {
                throw new Exception("Search input is too short.");
            }

            if (VrfGuiContext.ParentGuiContext != null)
            {
                throw new Exception("Inner paks are not supported.");
            }

            var results = new List<BetterTreeNode>();

            using var progressDialog = new GenericProgressForm
            {
                Text = "Searching file contents..."
            };
            progressDialog.OnProcess += (_, __) =>
            {
                Console.WriteLine("Pattern search");

                var maxArchiveIndex = -1;
                var sortedEntriesPerArchive = new Dictionary<int, List<PackageEntry>>();

                foreach (var extensions in VrfGuiContext.CurrentPackage.Entries.Values)
                {
                    foreach (var entry in extensions)
                    {
                        if (entry.ArchiveIndex != 0x7FFF && entry.ArchiveIndex > maxArchiveIndex)
                        {
                            maxArchiveIndex = entry.ArchiveIndex;
                        }

                        if (entry.Length == 0)
                        {
                            continue;
                        }

                        if (!sortedEntriesPerArchive.TryGetValue(entry.ArchiveIndex, out var archiveEntries))
                        {
                            archiveEntries = new();
                            sortedEntriesPerArchive.Add(entry.ArchiveIndex, archiveEntries);
                        }

                        archiveEntries.Add(entry);
                    }
                }

                foreach (var archiveEntries in sortedEntriesPerArchive.Values)
                {
                    archiveEntries.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                }

                var matches = new HashSet<PackageEntry>();

                if (sortedEntriesPerArchive.TryGetValue(0x7FFF, out var sortedEntriesInDirVpk))
                {
                    var fileName = $"{VrfGuiContext.CurrentPackage.FileName}{(VrfGuiContext.CurrentPackage.IsDirVPK ? "_dir" : "")}.vpk";

                    progressDialog.SetProgress($"Searching '{fileName}'");

                    var archiveMatches = SearchForContentsInFile(fileName, pattern, sortedEntriesInDirVpk);
                    matches.UnionWith(archiveMatches);
                }

                if (maxArchiveIndex > -1)
                {
                    var archivesScanned = 0;

                    Parallel.For(
                        0,
                        maxArchiveIndex,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = 3
                        },
                        archiveIndex =>
                        {
                            var fileName = $"{VrfGuiContext.CurrentPackage.FileName}_{archiveIndex:D3}.vpk";

                            var archiveMatches = SearchForContentsInFile(fileName, pattern, sortedEntriesPerArchive[archiveIndex]);

                            if (archiveMatches.Count > 0)
                            {
                                lock (archiveMatches)
                                {
                                    matches.UnionWith(archiveMatches);
                                }
                            }

                            Interlocked.Increment(ref archivesScanned);
                            progressDialog.SetProgress($"Searched {archivesScanned} vpks out of {maxArchiveIndex}, found {matches.Count} matches so far");
                        }
                    );
                }

                Console.WriteLine($"Found {matches.Count} matches");

                progressDialog.SetProgress($"Found {matches.Count} matches");

                foreach (var file in matches)
                {
                    var fileName = file.GetFileName();

                    if (!ExtensionIconList.TryGetValue(file.TypeName, out var ext))
                    {
                        ext = "_default";
                    }

                    var newNode = new BetterTreeNode(fileName, file)
                    {
                        Name = fileName,
                        ImageKey = ext,
                        SelectedImageKey = ext,
                    };
                    results.Add(newNode);
                }
            };
            progressDialog.ShowDialog();

            return results;
        }

        private static HashSet<PackageEntry> SearchForContentsInFile(string fileName, byte[] pattern, List<PackageEntry> archiveEntries)
        {
            var matches = new HashSet<PackageEntry>();

            //using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            var data = File.ReadAllBytes(fileName).AsSpan(); // TODO: stream it

            var match = -1;
            var offset = 0;
            var lastEntryId = 0;

            do
            {
                match = data.IndexOf(pattern);

                if (match < 0)
                {
                    break;
                }

                match += pattern.Length;
                offset += match;
                data = data[match..];

                PackageEntry packageEntry = null;

                for (var entryId = lastEntryId; entryId < archiveEntries.Count; entryId++)
                {
                    if (offset >= archiveEntries[entryId].Offset)
                    {
                        lastEntryId = entryId;
                        continue;
                    }

                    break;
                }

                packageEntry = archiveEntries[lastEntryId];

                if (offset <= packageEntry.Offset + packageEntry.Length)
                {
                    matches.Add(packageEntry);
                }
            }
            while (true);

            return matches;
        }

        public void GenerateIconList(IEnumerable<string> extensions)
        {
            ExtensionIconList = new Dictionary<string, string>();

            foreach (var originalExtension in extensions)
            {
                var extension = originalExtension;

                if (extension.EndsWith("_c", StringComparison.Ordinal))
                {
                    extension = extension[0..^2];
                }

                if (!ImageList.Images.ContainsKey(extension))
                {
                    if (extension.Length > 0 && extension[0] == 'v')
                    {
                        extension = extension[1..];

                        if (!ImageList.Images.ContainsKey(extension))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                ExtensionIconList.Add(originalExtension, extension);
            }
        }

        /// <summary>
        /// Adds a node to the tree based on the passed file information. This is useful when building a directory-based tree.
        /// </summary>
        /// <param name="currentNode">Root node.</param>
        /// <param name="file">File entry.</param>
        /// <param name="skipDeletedRootFolder">If true, ignore root folder for recovered deleted files.</param>
        public void AddFileNode(BetterTreeNode currentNode, PackageEntry file, bool skipDeletedRootFolder = false)
        {
            if (!string.IsNullOrWhiteSpace(file.DirectoryName))
            {
                var subPaths = file.DirectoryName.Split(Package.DirectorySeparatorChar);

                foreach (var subPath in subPaths)
                {
                    if (skipDeletedRootFolder && subPath == Types.Viewers.Package.DELETED_FILES_FOLDER)
                    {
                        continue;
                    }

                    var subNode = (BetterTreeNode)currentNode.Nodes[subPath];

                    if (subNode == null)
                    {
                        var toAdd = new BetterTreeNode(subPath, 1)
                        {
                            Name = subPath,
                            ImageKey = "_folder",
                            SelectedImageKey = "_folder",
                        };
                        currentNode.Nodes.Add(toAdd);
                        currentNode = toAdd;
                    }
                    else
                    {
                        currentNode = subNode;
                        currentNode.ItemCount++;
                    }
                }
            }

            var fileName = file.GetFileName();

            if (!ExtensionIconList.TryGetValue(file.TypeName, out var ext))
            {
                ext = "_default";
            }

            var newNode = new BetterTreeNode(fileName, file)
            {
                Name = fileName,
                ImageKey = ext,
                SelectedImageKey = ext,
            };

            currentNode.Nodes.Add(newNode);
            currentNode = newNode;
        }
    }
}
