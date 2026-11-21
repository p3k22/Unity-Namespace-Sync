namespace P3k.FF7R.Editor.Tools.NamespaceSync
{
   using System;
   using System.IO;

   using UnityEngine;

   ///<summary>
   /// Converts between absolute paths and paths relative to the Unity project root (forward slashes, no trailing one).
   ///</summary>
   internal static class ProjectPaths
   {
      ///<summary>
      /// Absolute path of the Unity project root.
      ///</summary>
      internal static string ProjectRoot()
      {
         return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
      }

      ///<summary>
      /// Turns a project relative path into an absolute one.
      ///</summary>
      internal static string ToAbsolutePath(string projectRelativePath)
      {
         return Path.GetFullPath(Path.Combine(ProjectRoot(), projectRelativePath));
      }

      ///<summary>
      /// Turns an absolute path into a project relative one, or null when it lies outside the project.
      ///</summary>
      internal static string ToProjectRelativePath(string absolutePath)
      {
         var root = ProjectRoot().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
         var fullPath = Path.GetFullPath(absolutePath);

         return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? fullPath.Substring(root.Length).Replace('\\', '/').TrimEnd('/')
            : null;
      }
   }
}
