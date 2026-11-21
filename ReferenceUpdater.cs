namespace P3k.FF7R.Editor.Tools.NamespaceSync
{
   using System;
   using System.Collections.Generic;
   using System.Text;
   using System.Text.RegularExpressions;

   ///<summary>
   /// Keeps the rest of the project compiling after types moved to new namespaces. Call Index for every file first,
   /// then Update for every file: it adds usings the file now needs, removes usings of namespaces that no longer
   /// exist, and rewrites fully qualified names such as Old.Space.Type.
   ///</summary>
   internal sealed class ReferenceUpdater
   {
      private static readonly Regex QualifiedName = new(@"(?<![\w.@])[\p{L}_]\w*(?:\.[\p{L}_]\w*)+");

      private readonly Dictionary<string, List<TypeLocation>> _typesByName = new();

      private readonly Dictionary<string, string> _newNamespaceByQualifiedName = new();

      private readonly HashSet<string> _declaredNamespaces = new();

      private readonly HashSet<string> _renamedNamespaces = new();

      private HashSet<string> _abandonedNamespaces;

      private bool _anythingMoved;

      ///<summary>
      /// Records the types and the namespace of one file, before and after the run's edits.
      ///</summary>
      internal void Index(SourceFile file)
      {
         SourceMasker.Mask(file.OriginalText, out var originalMasked, out _);
         var oldNamespace = DeclarationReader.FindNamespace(originalMasked);
         var newNamespace = oldNamespace;

         if (file.IsChanged)
         {
            SourceMasker.Mask(file.Text, out var currentMasked, out _);
            newNamespace = DeclarationReader.FindNamespace(currentMasked);
         }

         _declaredNamespaces.Add(newNamespace);
         if (oldNamespace.Length > 0 && oldNamespace != newNamespace)
         {
            _renamedNamespaces.Add(oldNamespace);
         }

         foreach (var typeName in DeclarationReader.ReadTypeNames(originalMasked))
         {
            AddType(new TypeLocation(typeName, oldNamespace, newNamespace));
         }
      }

      ///<summary>
      /// Fixes the usings and qualified names in one file. Returns a short note of what changed, or null when the
      /// file needed nothing.
      ///</summary>
      internal string Update(SourceFile file)
      {
         if (!_anythingMoved)
         {
            return null;
         }

         var text = ReplaceQualifiedNames(file.Text, out var qualifiedCount);

         SourceMasker.Mask(file.OriginalText, out var originalMasked, out _);
         SourceMasker.Mask(text, out var currentMasked, out _);
         var imported = DeclarationReader.ReadImportedNamespaces(currentMasked);

         var needed = FindNeededNamespaces(
            DeclarationReader.ReadIdentifiers(currentMasked),
            DeclarationReader.FindNamespace(originalMasked),
            DeclarationReader.ReadImportedNamespaces(originalMasked),
            DeclarationReader.FindNamespace(currentMasked),
            imported);
         var removed = FindAbandonedImports(imported);

         text = UsingEditor.Remove(UsingEditor.Insert(text, needed), removed);
         if (string.Equals(text, file.Text, StringComparison.Ordinal))
         {
            return null;
         }

         file.Text = text;
         return $"references updated: {needed.Count} usings added, {removed.Count} removed, "
                + $"{qualifiedCount} qualified names rewritten";
      }

      ///<summary>
      /// Remembers a type, and its old qualified name so references written that way can be rewritten.
      ///</summary>
      private void AddType(TypeLocation location)
      {
         if (!_typesByName.TryGetValue(location.Name, out var locations))
         {
            locations = new List<TypeLocation>();
            _typesByName[location.Name] = locations;
         }

         locations.Add(location);
         _anythingMoved |= location.HasMoved;

         if (location.HasMoved && location.OldNamespace.Length > 0)
         {
            _newNamespaceByQualifiedName[location.OldNamespace + "." + location.Name] = location.NewNamespace;
         }
      }

      ///<summary>
      /// The namespaces this file used to see but no longer does, for the types it mentions: it saw them by being in
      /// the same or a parent namespace, by a using, or because the type had no namespace.
      ///</summary>
      private SortedSet<string> FindNeededNamespaces(
         HashSet<string> identifiers,
         string oldNamespace,
         HashSet<string> oldImports,
         string newNamespace,
         HashSet<string> newImports)
      {
         var needed = new SortedSet<string>(StringComparer.Ordinal);

         foreach (var identifier in identifiers)
         {
            if (!_typesByName.TryGetValue(identifier, out var locations))
            {
               continue;
            }

            foreach (var location in locations)
            {
               var sawBefore = Sees(oldNamespace, oldImports, location.OldNamespace);
               var seesNow = Sees(newNamespace, newImports, location.NewNamespace);
               if (sawBefore && !seesNow)
               {
                  needed.Add(location.NewNamespace);
               }
            }
         }

         return needed;
      }

      ///<summary>
      /// Tells whether code in fileNamespace, with the given usings, can use a type from typeNamespace by its short
      /// name: the type has no namespace, or the file is in that namespace or inside it, or it has a using for it.
      ///</summary>
      private static bool Sees(string fileNamespace, HashSet<string> imports, string typeNamespace)
      {
         return typeNamespace.Length == 0
                || fileNamespace == typeNamespace
                || fileNamespace.StartsWith(typeNamespace + ".", StringComparison.Ordinal)
                || imports.Contains(typeNamespace);
      }

      ///<summary>
      /// The usings in the file that name a namespace which no file declares any more.
      ///</summary>
      private HashSet<string> FindAbandonedImports(HashSet<string> imported)
      {
         _abandonedNamespaces ??= FindAbandonedNamespaces();

         var abandonedImports = new HashSet<string>(imported);
         abandonedImports.IntersectWith(_abandonedNamespaces);
         return abandonedImports;
      }

      ///<summary>
      /// Namespaces that were renamed away and that no file declares or nests another namespace under.
      ///</summary>
      private HashSet<string> FindAbandonedNamespaces()
      {
         var abandoned = new HashSet<string>(_renamedNamespaces);

         foreach (var declared in _declaredNamespaces)
         {
            abandoned.RemoveWhere(
               name => declared == name || declared.StartsWith(name + ".", StringComparison.Ordinal));
         }

         return abandoned;
      }

      ///<summary>
      /// Rewrites names written in full, such as Old.Space.Type, to the type's new namespace. Returns the new text
      /// and how many names changed.
      ///</summary>
      private string ReplaceQualifiedNames(string text, out int replacedCount)
      {
         SourceMasker.Mask(text, out var masked, out _);
         var builder = new StringBuilder();
         var copiedUpTo = 0;
         replacedCount = 0;

         foreach (Match match in QualifiedName.Matches(masked))
         {
            if (!TryFindMovedPrefix(match.Value, out var oldLength, out var replacement))
            {
               continue;
            }

            builder.Append(text, copiedUpTo, match.Index - copiedUpTo).Append(replacement);
            copiedUpTo = match.Index + oldLength;
            replacedCount++;
         }

         return builder.Append(text, copiedUpTo, text.Length - copiedUpTo).ToString();
      }

      ///<summary>
      /// Looks along a dotted name for a prefix that is an old Namespace.Type. oldLength is how many characters that
      /// prefix covers; replacement is the same type written with its new namespace.
      ///</summary>
      private bool TryFindMovedPrefix(string dottedName, out int oldLength, out string replacement)
      {
         var parts = dottedName.Split('.');
         var prefix = parts[0];

         for (var index = 1; index < parts.Length; index++)
         {
            prefix += "." + parts[index];
            if (_newNamespaceByQualifiedName.TryGetValue(prefix, out var newNamespace))
            {
               oldLength = prefix.Length;
               replacement = newNamespace + "." + parts[index];
               return true;
            }
         }

         oldLength = 0;
         replacement = null;
         return false;
      }
   }
}
