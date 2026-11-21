namespace P3k.FF7R.Editor.Tools
{
   using System;
   using System.Collections.Generic;
   using System.IO;
   using System.Text;
   using System.Text.RegularExpressions;

   using UnityEditor;

   using UnityEngine;

   ///<summary>
   /// Pick a folder and a list of words to leave out; Go then gives every .cs file under that folder a namespace built
   /// from the file's own folder path, minus the filtered words. Files without a namespace get one, files with one are
   /// renamed. Folder Assets/P3k/FF7R/Runtime/Utilities with the words Assets and Runtime becomes P3k.FF7R.Utilities.
   ///</summary>
   internal sealed class NamespaceSyncWindow : EditorWindow
   {
      private const string FOLDER_PREF = "P3k.NamespaceSync.Folder";

      private const string FILTER_PREF = "P3k.NamespaceSync.Filter";

      private const string DRY_RUN_PREF = "P3k.NamespaceSync.DryRun";

      private const string KEYWORDS =
         "abstract as base bool break byte case catch char checked class const continue decimal default delegate do "
         + "double else enum event explicit extern false finally fixed float for foreach goto if implicit in int "
         + "interface internal is lock long namespace new null object operator out override params private protected "
         + "public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw "
         + "true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while";

      private static readonly HashSet<string> Keywords = new(KEYWORDS.Split(' '));

      private readonly List<string> _log = new();

      private string _folder = string.Empty;

      private string _filterWords = "Assets\nRuntime";

      private bool _dryRun = true;

      private Vector2 _logScroll;


      ///<summary>
      /// Opens the window from the Tools menu.
      ///</summary>
      [MenuItem("Tools/P3k/Namespace Sync")]
      private static void Open()
      {
         GetWindow<NamespaceSyncWindow>("Namespace Sync");
      }

      ///<summary>
      /// Restores the folder, the filter words and the dry run choice from the last session.
      ///</summary>
      private void OnEnable()
      {
         _folder = EditorPrefs.GetString(FOLDER_PREF, _folder);
         _filterWords = EditorPrefs.GetString(FILTER_PREF, _filterWords);
         _dryRun = EditorPrefs.GetBool(DRY_RUN_PREF, _dryRun);
      }

      ///<summary>
      /// Remembers the folder, the filter words and the dry run choice for the next session.
      ///</summary>
      private void OnDisable()
      {
         EditorPrefs.SetString(FOLDER_PREF, _folder);
         EditorPrefs.SetString(FILTER_PREF, _filterWords);
         EditorPrefs.SetBool(DRY_RUN_PREF, _dryRun);
      }

      ///<summary>
      /// Draws the folder row, the filter words, the dry run toggle, the Go button and the result log.
      ///</summary>
      private void OnGUI()
      {
         DrawFolderRow();

         EditorGUILayout.LabelField("Words to leave out of the namespace (one per line, or comma separated)");
         _filterWords = EditorGUILayout.TextArea(_filterWords, GUILayout.MinHeight(60));

         _dryRun = EditorGUILayout.ToggleLeft("Dry run (only report, do not write any file)", _dryRun);
         EditorGUILayout.HelpBox(
            "Code in other files that uses the old namespaces needs its using lines fixed afterwards. "
            + "Commit or back up first.",
            MessageType.Info);

         using (new EditorGUI.DisabledScope(!IsFolderValid()))
         {
            if (GUILayout.Button("Go", GUILayout.Height(28)))
            {
               Run();
            }
         }

         _logScroll = EditorGUILayout.BeginScrollView(_logScroll);
         EditorGUILayout.TextArea(string.Join("\n", _log), GUILayout.ExpandHeight(true));
         EditorGUILayout.EndScrollView();
      }

      ///<summary>
      /// Draws the folder text field with its Browse button. The folder is kept relative to the project root.
      ///</summary>
      private void DrawFolderRow()
      {
         using (new EditorGUILayout.HorizontalScope())
         {
            _folder = EditorGUILayout.TextField("Folder", _folder).Replace('\\', '/').Trim('/');

            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
               BrowseForFolder();
            }
         }
      }

      ///<summary>
      /// Asks for a folder with the system dialog and keeps it when it lies inside the project.
      ///</summary>
      private void BrowseForFolder()
      {
         var startPath = IsFolderValid() ? ToAbsolutePath(_folder) : Application.dataPath;
         var picked = EditorUtility.OpenFolderPanel("Folder to scan for .cs files", startPath, string.Empty);

         if (string.IsNullOrEmpty(picked))
         {
            return;
         }

         var relativePath = ToProjectRelativePath(picked);
         if (relativePath == null)
         {
            EditorUtility.DisplayDialog("Namespace Sync", "The folder must be inside this Unity project.", "OK");
            return;
         }

         _folder = relativePath;
         GUI.FocusControl(null);
      }

      ///<summary>
      /// Tells whether the folder field names an existing folder inside the project.
      ///</summary>
      private bool IsFolderValid()
      {
         return !string.IsNullOrWhiteSpace(_folder)
                && ToProjectRelativePath(ToAbsolutePath(_folder)) != null
                && Directory.Exists(ToAbsolutePath(_folder));
      }

      ///<summary>
      /// Walks every .cs file under the folder, rewrites the namespaces and fills the log with what happened.
      ///</summary>
      private void Run()
      {
         OnDisable();
         _log.Clear();

         var filterWords = ParseFilterWords(_filterWords);
         var files = Directory.GetFiles(ToAbsolutePath(_folder), "*.cs", SearchOption.AllDirectories);
         var changedCount = 0;
         var skippedCount = 0;

         try
         {
            for (var index = 0; index < files.Length; index++)
            {
               EditorUtility.DisplayProgressBar("Namespace Sync", files[index], (float)index / files.Length);

               var changed = ProcessFile(files[index], filterWords, out var note);
               changedCount += changed ? 1 : 0;
               skippedCount += !changed && note != null ? 1 : 0;

               if (note != null)
               {
                  _log.Add(ToProjectRelativePath(files[index]) + ": " + note);
               }
            }
         }
         finally
         {
            EditorUtility.ClearProgressBar();
         }

         var summary = $"{(_dryRun ? "Dry run: " : string.Empty)}{files.Length} scanned, {changedCount} changed, "
                       + $"{skippedCount} skipped";
         _log.Insert(0, summary);
         Debug.Log("[Namespace Sync] " + summary);

         if (!_dryRun)
         {
            AssetDatabase.Refresh();
         }
      }

      ///<summary>
      /// Works out the namespace for one file from its folder and writes the file when its text changes.
      /// Returns true when the file was changed; note says what happened, or is null when nothing needed doing.
      ///</summary>
      private bool ProcessFile(string filePath, HashSet<string> filterWords, out string note)
      {
         var namespaceName = BuildNamespace(ToProjectRelativePath(Path.GetDirectoryName(filePath)), filterWords);
         if (namespaceName.Length == 0)
         {
            note = "skipped, every folder in its path is filtered out";
            return false;
         }

         var text = ReadSource(filePath, out var hasByteOrderMark);
         if (!NamespaceRewriter.TryRewrite(text, namespaceName, out var newText, out note))
         {
            return false;
         }

         if (!_dryRun)
         {
            File.WriteAllText(filePath, newText, new UTF8Encoding(hasByteOrderMark));
         }

         return true;
      }

      ///<summary>
      /// Reads a source file as UTF-8 and reports whether it started with a byte order mark.
      ///</summary>
      private static string ReadSource(string filePath, out bool hasByteOrderMark)
      {
         var bytes = File.ReadAllBytes(filePath);
         hasByteOrderMark = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
         var skip = hasByteOrderMark ? 3 : 0;
         return Encoding.UTF8.GetString(bytes, skip, bytes.Length - skip);
      }

      ///<summary>
      /// Splits the filter box into a case-insensitive set of words. Commas, semicolons and white space separate them.
      ///</summary>
      private static HashSet<string> ParseFilterWords(string filterText)
      {
         var separators = new[] { ',', ';', ' ', '\t', '\r', '\n' };
         return new HashSet<string>(
            filterText.Split(separators, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
      }

      ///<summary>
      /// Turns a project relative folder such as Assets/P3k/FF7R into a dotted namespace, dropping filtered folders.
      ///</summary>
      private static string BuildNamespace(string projectRelativeFolder, HashSet<string> filterWords)
      {
         var identifiers = new List<string>();

         foreach (var folderName in projectRelativeFolder.Split('/'))
         {
            var cleanName = StripInvalidCharacters(folderName);
            var isFiltered = filterWords.Contains(folderName) || filterWords.Contains(cleanName);
            if (!isFiltered)
            {
               identifiers.AddRange(ToIdentifiers(folderName));
            }
         }

         return string.Join(".", identifiers);
      }

      ///<summary>
      /// Turns one folder name into valid namespace parts: dots split it, bad characters go, a leading digit gets an
      /// underscore and a C# keyword gets an @ prefix.
      ///</summary>
      private static IEnumerable<string> ToIdentifiers(string folderName)
      {
         foreach (var piece in folderName.Split('.'))
         {
            var identifier = StripInvalidCharacters(piece);
            if (identifier.Length == 0)
            {
               continue;
            }

            if (char.IsDigit(identifier[0]))
            {
               identifier = "_" + identifier;
            }

            yield return Keywords.Contains(identifier) ? "@" + identifier : identifier;
         }
      }

      ///<summary>
      /// Removes everything that is not a letter, a digit or an underscore.
      ///</summary>
      private static string StripInvalidCharacters(string name)
      {
         return Regex.Replace(name, @"[^\p{L}\p{Nd}_]", string.Empty);
      }

      ///<summary>
      /// Absolute path of the Unity project root.
      ///</summary>
      private static string ProjectRoot()
      {
         return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
      }

      ///<summary>
      /// Turns a project relative path into an absolute one.
      ///</summary>
      private static string ToAbsolutePath(string projectRelativePath)
      {
         return Path.GetFullPath(Path.Combine(ProjectRoot(), projectRelativePath));
      }

      ///<summary>
      /// Turns an absolute path into a project relative one with forward slashes, or null when it lies outside
      /// the project.
      ///</summary>
      private static string ToProjectRelativePath(string absolutePath)
      {
         var root = ProjectRoot().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
         var fullPath = Path.GetFullPath(absolutePath);

         return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? fullPath.Substring(root.Length).Replace('\\', '/').TrimEnd('/')
            : null;
      }
   }
}
