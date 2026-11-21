namespace P3k.FF7R.Editor.Tools
{
   using System.Collections.Generic;
   using System.IO;

   using UnityEditor;

   using UnityEngine;

   ///<summary>
   /// Pick a folder and a list of words to leave out; Go then gives every .cs file under that folder a namespace built
   /// from the file's own folder path, minus the filtered words. Files without a namespace get one, files with one are
   /// renamed. Folder Assets/P3k/FF7R/Runtime/Utilities with the words Assets and Runtime becomes P3k.FF7R.Utilities.
   /// Usings and fully qualified names in the other scripts under Assets are updated to match.
   ///</summary>
   internal sealed class NamespaceSyncWindow : EditorWindow
   {
      private const string FOLDER_PREF = "P3k.NamespaceSync.Folder";

      private const string FILTER_PREF = "P3k.NamespaceSync.Filter";

      private const string DRY_RUN_PREF = "P3k.NamespaceSync.DryRun";

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
            "Every .cs file under Assets may be edited: usings and fully qualified names are updated to follow the "
            + "namespace changes. Commit or back up first.",
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
         var startPath = IsFolderValid() ? ProjectPaths.ToAbsolutePath(_folder) : Application.dataPath;
         var picked = EditorUtility.OpenFolderPanel("Folder to scan for .cs files", startPath, string.Empty);

         if (string.IsNullOrEmpty(picked))
         {
            return;
         }

         var relativePath = ProjectPaths.ToProjectRelativePath(picked);
         if (relativePath == null)
         {
            EditorUtility.DisplayDialog("Namespace Sync", "The folder must be inside this Unity project.", "OK");
            return;
         }

         _folder = relativePath;
         GUI.FocusControl(null);
      }

      ///<summary>
      /// Tells whether the folder field names an existing folder inside the Assets folder.
      ///</summary>
      private bool IsFolderValid()
      {
         if (string.IsNullOrWhiteSpace(_folder))
         {
            return false;
         }

         var absolutePath = ProjectPaths.ToAbsolutePath(_folder);
         var relativePath = ProjectPaths.ToProjectRelativePath(absolutePath);

         return relativePath != null && relativePath.StartsWith("Assets") && Directory.Exists(absolutePath);
      }

      ///<summary>
      /// Runs the pass over the folder and shows what it did in the log.
      ///</summary>
      private void Run()
      {
         OnDisable();

         var run = new NamespaceSyncRun(_folder, NamespaceNamer.ParseFilterWords(_filterWords), _dryRun);
         _log.Clear();
         _log.AddRange(run.Execute());

         Debug.Log("[Namespace Sync] " + _log[0]);
      }
   }
}
