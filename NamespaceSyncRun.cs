namespace P3k.FF7R.Editor.Tools
{
   using System;
   using System.Collections.Generic;
   using System.IO;

   using UnityEditor;

   ///<summary>
   /// One pass of the tool. It gives every .cs file under the chosen folder its namespace, then fixes the usings and
   /// qualified names in every .cs file under Assets. With a dry run nothing is written to disk.
   ///</summary>
   internal sealed class NamespaceSyncRun
   {
      private const string TITLE = "Namespace Sync";

      private readonly string _folderPath;

      private readonly HashSet<string> _filterWords;

      private readonly bool _dryRun;

      private readonly List<string> _log = new();

      ///<summary>
      /// Prepares a pass over the folder (project relative) using the words to leave out of the namespaces.
      ///</summary>
      internal NamespaceSyncRun(string projectRelativeFolder, HashSet<string> filterWords, bool dryRun)
      {
         _folderPath = ProjectPaths.ToAbsolutePath(projectRelativeFolder);
         _filterWords = filterWords;
         _dryRun = dryRun;
      }

      ///<summary>
      /// Runs the pass and returns the log: a summary line first, then one line per file that changed or was skipped.
      ///</summary>
      internal List<string> Execute()
      {
         try
         {
            var files = LoadProjectFiles();
            var changedCount = RewriteNamespaces(files, out var scannedCount, out var skippedCount);
            var referenceCount = UpdateReferences(files);

            SaveChangedFiles(files);

            var prefix = _dryRun ? "Dry run: " : string.Empty;
            _log.Insert(
               0,
               $"{prefix}{scannedCount} scanned, {changedCount} namespaces changed, {skippedCount} skipped, "
               + $"{referenceCount} files had usings or qualified names updated");
            return _log;
         }
         finally
         {
            EditorUtility.ClearProgressBar();
         }
      }

      ///<summary>
      /// Loads every .cs file under Assets, since a using can sit in any of them.
      ///</summary>
      private static List<SourceFile> LoadProjectFiles()
      {
         var files = new List<SourceFile>();
         var paths = Directory.GetFiles(ProjectPaths.ToAbsolutePath("Assets"), "*.cs", SearchOption.AllDirectories);

         for (var index = 0; index < paths.Length; index++)
         {
            ShowProgress("Reading files", paths[index], index, paths.Length);
            files.Add(SourceFile.Load(paths[index]));
         }

         return files;
      }

      ///<summary>
      /// Gives each file under the chosen folder its namespace. Returns how many files were changed.
      ///</summary>
      private int RewriteNamespaces(List<SourceFile> files, out int scannedCount, out int skippedCount)
      {
         var changedCount = 0;
         scannedCount = 0;
         skippedCount = 0;

         foreach (var file in files)
         {
            if (!IsInChosenFolder(file))
            {
               continue;
            }

            ShowProgress("Setting namespaces", file.Path, scannedCount, files.Count);
            scannedCount++;

            var changed = RewriteNamespace(file, out var note);
            changedCount += changed ? 1 : 0;
            skippedCount += !changed && note != null ? 1 : 0;
            AddLogLine(file, note);
         }

         return changedCount;
      }

      ///<summary>
      /// Works out the namespace for one file from its folder and sets it. Returns true when the file's text changed;
      /// note says what happened, or is null when the namespace was already right.
      ///</summary>
      private bool RewriteNamespace(SourceFile file, out string note)
      {
         var folder = ProjectPaths.ToProjectRelativePath(Path.GetDirectoryName(file.Path));
         var namespaceName = NamespaceNamer.BuildNamespace(folder, _filterWords);

         if (namespaceName.Length == 0)
         {
            note = "skipped, every folder in its path is filtered out";
            return false;
         }

         if (!NamespaceRewriter.TryRewrite(file.Text, namespaceName, out var newText, out note))
         {
            return false;
         }

         file.Text = newText;
         return true;
      }

      ///<summary>
      /// Fixes usings and qualified names in all files. Returns how many files it changed.
      ///</summary>
      private int UpdateReferences(List<SourceFile> files)
      {
         var updater = new ReferenceUpdater();
         var updatedCount = 0;

         for (var index = 0; index < files.Count; index++)
         {
            ShowProgress("Reading declarations", files[index].Path, index, files.Count);
            updater.Index(files[index]);
         }

         for (var index = 0; index < files.Count; index++)
         {
            ShowProgress("Updating usings", files[index].Path, index, files.Count);

            var note = updater.Update(files[index]);
            updatedCount += note != null ? 1 : 0;
            AddLogLine(files[index], note);
         }

         return updatedCount;
      }

      ///<summary>
      /// Writes every changed file back to disk and tells Unity to import them. A dry run skips this.
      ///</summary>
      private void SaveChangedFiles(List<SourceFile> files)
      {
         if (_dryRun)
         {
            return;
         }

         foreach (var file in files)
         {
            if (file.IsChanged)
            {
               file.Save();
            }
         }

         AssetDatabase.Refresh();
      }

      ///<summary>
      /// Tells whether the file lies under the folder the user chose.
      ///</summary>
      private bool IsInChosenFolder(SourceFile file)
      {
         var folderWithSeparator = _folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
         return Path.GetFullPath(file.Path).StartsWith(folderWithSeparator, StringComparison.OrdinalIgnoreCase);
      }

      ///<summary>
      /// Adds a line to the log for the file, unless there is nothing to say.
      ///</summary>
      private void AddLogLine(SourceFile file, string note)
      {
         if (note != null)
         {
            _log.Add(ProjectPaths.ToProjectRelativePath(file.Path) + ": " + note);
         }
      }

      ///<summary>
      /// Shows the progress bar for the current step.
      ///</summary>
      private static void ShowProgress(string step, string path, int index, int total)
      {
         EditorUtility.DisplayProgressBar(TITLE + ": " + step, path, (float)index / Math.Max(total, 1));
      }
   }
}
